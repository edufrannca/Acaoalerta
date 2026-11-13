using System.Diagnostics;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Modulos.AnaliseSituacional;

public sealed class ServicoAnalise(IUnidadeTrabalho uow, ServicoTerritorio territorio, IProvedorIA ia, INotificadorTempoReal tempoReal,
    IRelogio relogio, ILogger<ServicoAnalise> log)
{
    /// <summary>Aplica as regras determinísticas (sem IA). Chamado na mesma transação do registro.</summary>
    public async Task<AnaliseOcorrencia> AvaliarRegrasAsync(Ocorrencia o, CancellationToken ct)
    {
        var agora = relogio.Agora;
        ContextoTerritorialResumo? resumo = null;
        IEnumerable<ReferenciaFonte> fontes = [];
        if (o.Ponto is { } p)
        {
            try
            {
                var ctx = await territorio.AvaliarAsync(p, ct, ignorarOcorrencia: o.Id);
                resumo = ServicoTerritorio.Resumir(ctx, agora);
                fontes = ServicoTerritorio.Fontes(ctx);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Contexto territorial é complementar: a falha nunca impede o registro.
                log.LogWarning(e, "Falha ao avaliar contexto territorial da ocorrência {Id}", o.Id);
            }
        }
        var sinais = MotorRegrasUrgencia.Avaliar(o, resumo, agora);
        var analise = await uow.PrimeiroAsync(uow.Consulta<AnaliseOcorrencia>().Where(a => a.OcorrenciaId == o.Id), ct);
        if (analise is null) { analise = new AnaliseOcorrencia(o.Id); uow.Adicionar(analise); }
        analise.AplicarRegras(sinais, resumo, fontes, ia.Configurado, agora);
        return analise;
    }

    public static EntradaAnaliseIA MontarEntrada(Ocorrencia o, AnaliseOcorrencia a) => new(
        o.Id.ToString(), MunicipiosRmr.Nome(o.MunicipioCodigo), o.CriadaNoDispositivoEm.ToString("O"),
        o.LocalizacaoLidaEm?.ToString("O"), o.Ponto is not null, o.Complemento, a.Contexto,
        a.Sinalizacoes.Select(s => $"{s.Codigo}: {s.Titulo}").ToList());

    /// <summary>Processa a IA de ocorrências pendentes (worker em segundo plano). Nunca bloqueia a transmissão.</summary>
    public async Task<int> ProcessarPendentesAsync(int limite, CancellationToken ct)
    {
        if (!ia.Configurado) return 0;
        var candidatos = await uow.ListarAsync(uow.Consulta<AnaliseOcorrencia>()
            .Where(a => a.StatusIA == StatusAnaliseIA.Pendente || (a.StatusIA == StatusAnaliseIA.Falhou && a.TentativasIA < AnaliseOcorrencia.MaximoTentativasIA))
            .OrderBy(a => a.RegrasAvaliadasEm).Take(limite * 4), ct);
        var pendentes = candidatos.Where(a => a.ProntaParaTentar(relogio.Agora)).Take(limite).ToList();
        var n = 0;
        foreach (var analise in pendentes)
        {
            var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == analise.OcorrenciaId), ct);
            if (o is null) continue;
            var entrada = MontarEntrada(o, analise);
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(45));
                var resposta = await ia.AnalisarAsync(entrada, cts.Token);
                var validacao = ValidadorSaidaIA.Validar(resposta.Json, entrada);
                analise.RegistrarIA(validacao, $"{resposta.Provedor}/{resposta.Modelo}", resposta.DuracaoMs, relogio.Agora);
                o.Registrar(relogio.Agora, "analise_ia", validacao.Valido
                    ? $"Análise de IA concluída ({resposta.Modelo}); {validacao.ItensDescartados} item(ns) sem evidência descartados."
                    : "Resposta da IA rejeitada pela validação; mantidas apenas as regras determinísticas.");
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log.LogWarning(e, "IA indisponível para ocorrência {Id}", o.Id);
                analise.RegistrarFalhaIA($"IA indisponível: {e.GetType().Name}: {e.Message}", relogio.Agora);
                o.Registrar(relogio.Agora, "analise_ia", "IA indisponível; ocorrência segue registrada e encaminhável com as regras determinísticas.");
            }
            await uow.SalvarAsync(ct);
            await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, [], "analise_ia");
            n++;
        }
        return n;
    }
}
