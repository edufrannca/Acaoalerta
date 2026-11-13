using AlertaEmAcao.Modulos.Abrigos;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Infraestrutura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Infraestrutura.Trabalhos;

/// <summary>Sincroniza dados de referência de fontes públicas (limites, áreas de risco, abrigos).</summary>
public sealed class ServicoFontesReferencia(AlertaDbContext db, IFonteLimitesMunicipais limites, IFonteAreasRisco areas, IFonteAbrigos abrigos,
    ICatalogoMunicipios catalogo, IRelogio relogio, ILogger<ServicoFontesReferencia> log)
{
    public async Task SincronizarLimitesAsync(CancellationToken ct)
    {
        var r = await limites.ObterAsync(ct);
        if (r is null) return;
        var agora = relogio.Agora;
        foreach (var m in await db.Set<Municipio>().ToListAsync(ct))
            if (r.TryGetValue(m.CodigoIbge, out var geo))
                m.AtualizarGeometria(geo, "APAC — Limite municipal (consulta ao vivo, simplificada ~50 m)", true, agora);
        await db.SaveChangesAsync(ct);
        await catalogo.RecarregarAsync(ct);
    }

    public async Task<int> SincronizarAreasRiscoAsync(CancellationToken ct)
    {
        var (lista, ok, _) = await areas.ObterAsync(ct);
        if (!ok) return 0;
        var existentes = (await db.Set<AreaRiscoDocumentada>().Select(a => a.Fonte + "|" + a.IdExterno).ToListAsync(ct)).ToHashSet();
        var novos = lista.Where(a => !existentes.Contains(a.Fonte + "|" + a.IdExterno)).ToList();
        db.AddRange(novos);
        await db.SaveChangesAsync(ct);
        return novos.Count;
    }

    public async Task<int> SincronizarAbrigosAsync(CancellationToken ct)
    {
        var (lista, ok, _) = await abrigos.ObterAsync(ct);
        if (!ok) return 0;
        // Substitui o retrato da fonte (abrigos reais), preservando os fictícios.
        var antigos = await db.Set<Abrigo>().Where(a => !a.Ficticio && a.Fonte == (lista.FirstOrDefault() != null ? lista[0].Fonte : "")).ToListAsync(ct);
        db.RemoveRange(antigos);
        db.AddRange(lista);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Abrigos sincronizados: {N}", lista.Count);
        return lista.Count;
    }
}

public abstract class TrabalhoPeriodico(IServiceScopeFactory escopos, ILogger log, TimeSpan intervalo) : BackgroundService
{
    protected abstract Task ExecutarCicloAsync(IServiceProvider sp, CancellationToken ct);
    protected virtual Task IniciarAsync(IServiceProvider sp, CancellationToken ct) => Task.CompletedTask;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();
        try
        {
            using var s = escopos.CreateScope();
            await IniciarAsync(s.ServiceProvider, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested) { log.LogError(e, "Falha na inicialização de {Trabalho}", GetType().Name); }

        using var timer = new PeriodicTimer(intervalo);
        do
        {
            try
            {
                using var s = escopos.CreateScope();
                await ExecutarCicloAsync(s.ServiceProvider, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { log.LogError(e, "Erro no ciclo de {Trabalho}", GetType().Name); }
        } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }
}

public sealed class TrabalhoDespacho(IServiceScopeFactory e, ILogger<TrabalhoDespacho> l) : TrabalhoPeriodico(e, l, TimeSpan.FromSeconds(1))
{
    protected override Task IniciarAsync(IServiceProvider sp, CancellationToken ct) => sp.GetRequiredService<ServicoDespacho>().RecuperarInterrompidosAsync(ct);
    protected override async Task ExecutarCicloAsync(IServiceProvider sp, CancellationToken ct)
    {
        while (await sp.GetRequiredService<ServicoDespacho>().ProcessarPendentesAsync(20, ct) > 0 && !ct.IsCancellationRequested)
            sp.GetRequiredService<AlertaDbContext>().ChangeTracker.Clear();
    }
}

public sealed class TrabalhoAnaliseIA(IServiceScopeFactory e, ILogger<TrabalhoAnaliseIA> l) : TrabalhoPeriodico(e, l, TimeSpan.FromSeconds(3))
{
    protected override Task ExecutarCicloAsync(IServiceProvider sp, CancellationToken ct) => sp.GetRequiredService<ServicoAnalise>().ProcessarPendentesAsync(5, ct);
}

public sealed class TrabalhoAlertas(IServiceScopeFactory e, ILogger<TrabalhoAlertas> l) : TrabalhoPeriodico(e, l, TimeSpan.FromSeconds(5))
{
    private DateTimeOffset _ultimaSincronizacao = DateTimeOffset.MinValue;
    protected override async Task ExecutarCicloAsync(IServiceProvider sp, CancellationToken ct)
    {
        var servico = sp.GetRequiredService<ServicoAlertas>();
        if (DateTimeOffset.UtcNow - _ultimaSincronizacao > TimeSpan.FromMinutes(10))
        {
            _ultimaSincronizacao = DateTimeOffset.UtcNow;
            await servico.SincronizarOficiaisAsync(ct);
        }
        await servico.ProcessarNovosAsync(ct);
    }
}

public sealed class TrabalhoFontesReferencia(IServiceScopeFactory e, ILogger<TrabalhoFontesReferencia> l) : TrabalhoPeriodico(e, l, TimeSpan.FromMinutes(30))
{
    private int _ciclo;
    protected override async Task ExecutarCicloAsync(IServiceProvider sp, CancellationToken ct)
    {
        var s = sp.GetRequiredService<ServicoFontesReferencia>();
        if (_ciclo++ % 12 == 0) // a cada 6 h
        {
            await s.SincronizarLimitesAsync(ct);
            await s.SincronizarAreasRiscoAsync(ct);
        }
        await s.SincronizarAbrigosAsync(ct);
        await sp.GetRequiredService<IFonteChuva>().ObterRmrAsync(ct);
    }
}

/// <summary>A cada 5 min (cache da APAC também de 5 min): divulga novas leituras e avisa agravamentos nos locais monitorados.</summary>
public sealed class TrabalhoIndicadorAlagamento(IServiceScopeFactory e, ILogger<TrabalhoIndicadorAlagamento> l) : TrabalhoPeriodico(e, l, TimeSpan.FromMinutes(5))
{
    protected override Task ExecutarCicloAsync(IServiceProvider sp, CancellationToken ct) =>
        sp.GetRequiredService<Modulos.Notificacoes.ServicoAlertaPreventivo>().ProcessarAsync(ct);
}
