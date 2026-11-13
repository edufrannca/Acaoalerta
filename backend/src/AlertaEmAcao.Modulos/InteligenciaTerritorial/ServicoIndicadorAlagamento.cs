using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Ocorrencias;
using System.Collections.Concurrent;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Municipios;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Modulos.InteligenciaTerritorial;

/// <summary>Porta para a IA explicar (não decidir) o indicador. Implementada pelo provedor compatível com OpenAI (Groq).</summary>
public interface IExplicadorIA
{
    bool Configurado { get; }
    Task<string> ExplicarAsync(string sistema, string usuario, CancellationToken ct);
}

public sealed class ExplicadorIANulo : IExplicadorIA
{
    public bool Configurado => false;
    public Task<string> ExplicarAsync(string sistema, string usuario, CancellationToken ct) => throw new InvalidOperationException("IA não configurada.");
}

public sealed record ExplicacaoIndicador(string Texto, string Origem, string? Motivo);

/// <summary>Valida a explicação da IA: só pode usar números presentes nos fatos e não pode prever, estimar probabilidade ou inventar alagamento.</summary>
public static class ValidadorExplicacao
{
    private static readonly Regex Numero = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);
    private static readonly string[] Proibidos =
    [
        "%", "probabilidade", "chance de", "certamente", "vai alagar", "irá alagar", "ficará alagad", "vai inundar", "irá inundar",
        "em minutos", "nas próximas horas vai", "está alagad", "estão alagad", "está ilhad", "estão ilhad",
    ];

    public static (bool Ok, string? Motivo, string? Texto) Validar(string json, IReadOnlySet<string> numerosPermitidos, bool alagamentoConfirmado)
    {
        string? texto;
        try { texto = JsonDocument.Parse(json).RootElement.GetProperty("explicacao").GetString(); }
        catch { return (false, "resposta_invalida", null); }
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > 700) return (false, "tamanho_invalido", null);
        var baixo = texto.ToLowerInvariant();
        foreach (var p in Proibidos)
            if (baixo.Contains(p) && !(alagamentoConfirmado && p.StartsWith("está"))) return (false, $"termo_proibido:{p}", null);
        foreach (Match m in Numero.Matches(texto))
            if (!numerosPermitidos.Contains(Normalizar(m.Value))) return (false, $"numero_inventado:{m.Value}", null);
        return (true, null, texto.Trim());
    }

    public static string Normalizar(string n) =>
        double.TryParse(n.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v.ToString("0.##", CultureInfo.InvariantCulture) : n;
}

public sealed class ServicoIndicadorAlagamento(IUnidadeTrabalho uow, ICatalogoMunicipios catalogo, IFonteChuva fonteChuva, IExplicadorIA ia,
    IRelogio relogio, ILogger<ServicoIndicadorAlagamento> log)
{
    // Cache em memória das explicações (10 min): evita chamadas repetidas à IA para os mesmos fatos.
    private static readonly ConcurrentDictionary<string, (ExplicacaoIndicador E, DateTimeOffset Ate)> Cache = new();

    public async Task<IndicadorAlagamento> AvaliarAsync(PontoGeo ponto, string? regiao, CancellationToken ct)
    {
        var agora = relogio.Agora;
        var mun = catalogo.Identificar(ponto);
        var chuva = await fonteChuva.ObterRmrAsync(ct);
        var avisos = await uow.ListarAsync(uow.Consulta<Aviso>().Where(a => a.FimEm >= agora && a.InicioEm <= agora), ct);
        var desde = agora.AddHours(-6);
        const double r = 0.012;
        var occ = (await uow.ListarAsync(uow.Consulta<Ocorrencia>()
                .Where(o => o.RecebidaNoServidorEm >= desde && o.Latitude != null && o.Verificacao != VerificacaoOcorrencia.DescartadaPorOperador
                            && o.Latitude > ponto.Latitude - r && o.Latitude < ponto.Latitude + r && o.Longitude > ponto.Longitude - r && o.Longitude < ponto.Longitude + r)
                .Select(o => new OcorrenciaProxima(o.Latitude!.Value, o.Longitude!.Value, o.Verificacao == VerificacaoOcorrencia.VerificadaPorOperador, o.RecebidaNoServidorEm)), ct))
            .Where(o => new PontoGeo(o.Latitude, o.Longitude).DistanciaMetros(ponto) <= 1_000).ToList();
        string? susc = null;
        if (mun is not null)
        {
            const double d = 0.004;
            var areas = await uow.ListarAsync(uow.Consulta<AreaRiscoDocumentada>().Where(a => a.MunicipioCodigo == mun.Codigo
                && a.Latitude > ponto.Latitude - d && a.Latitude < ponto.Latitude + d && a.Longitude > ponto.Longitude - d && a.Longitude < ponto.Longitude + d), ct);
            var perto = areas.Where(a => a.Ponto.DistanciaMetros(ponto) <= AvaliadorContextoTerritorial.RaioSuscetibilidadeMetros).ToList();
            if (perto.Count > 0) susc = $"{perto.Count} registro(s) de área de risco a até {AvaliadorContextoTerritorial.RaioSuscetibilidadeMetros:0} m ({perto[0].Fonte}).";
        }
        return MotorIndicadorAlagamento.Avaliar(ponto, regiao ?? mun?.Nome ?? "Fora da Região Metropolitana do Recife", chuva.Leituras, avisos, mun?.Codigo,
            occ.Count(o => o.Verificada), occ.Count(o => !o.Verificada), susc, chuva.Disponivel, agora);
    }

    /// <summary>Explicação em linguagem simples. A IA só é chamada com nível ≥ Atenção (economia de cota), com cache de 10 min.</summary>
    public async Task<ExplicacaoIndicador> ExplicarAsync(IndicadorAlagamento i, CancellationToken ct)
    {
        var basica = Deterministica(i);
        if (i.Nivel == NivelAtencaoAlagamento.SemEvidenciaSuficiente || !ia.Configurado) return basica;
        var (fatos, numeros) = Fatos(i);
        var chave = "explicacao-alagamento:" + fatos.GetHashCode();
        if (Cache.TryGetValue(chave, out var em) && em.Ate > relogio.Agora) return em.E;
        ExplicacaoIndicador resultado;
        try
        {
            var json = await ia.ExplicarAsync(PromptSistema, fatos, ct);
            var (ok, motivo, texto) = ValidadorExplicacao.Validar(json, numeros, i.OcorrenciasVerificadas > 0);
            resultado = ok ? new ExplicacaoIndicador(texto!, "ia_validada", null) : basica with { Motivo = $"Explicação da IA descartada ({motivo})." };
            if (!ok) log.LogWarning("Explicação da IA descartada: {Motivo}", motivo);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            resultado = basica with { Motivo = "IA indisponível; explicação gerada pelas regras." };
            log.LogWarning("Falha na IA ao explicar o indicador: {Erro}", e.Message);
        }
        if (Cache.Count > 500) Cache.Clear();
        Cache[chave] = (resultado, relogio.Agora.AddMinutes(10));
        return resultado;
    }

    public const string PromptSistema =
        "Você explica, em português simples e em até 3 frases, um indicador de atenção a alagamentos calculado por regras. " +
        "Use SOMENTE os fatos fornecidos. Não invente medidas, horários, probabilidades, percentuais ou ocorrências. " +
        "Não diga que uma rua ou casa vai alagar nem estime quando. Não mude o nível. " +
        "Responda apenas JSON: {\"explicacao\": \"...\"}.";

    public static ExplicacaoIndicador Deterministica(IndicadorAlagamento i) =>
        new($"{i.Rotulo}: {string.Join(" ", i.Justificativa.Take(3))} Tendência: {i.TendenciaDescricao.ToLowerInvariant()}. {i.Estimativa.Mensagem}", "regras", null);

    public static (string Fatos, IReadOnlySet<string> Numeros) Fatos(IndicadorAlagamento i)
    {
        var f = new List<string> { $"Região: {i.Regiao}", $"Nível: {i.Rotulo}", $"Intensidade na última hora: {i.IntensidadeDescricao}", $"Tendência: {i.TendenciaDescricao}" };
        if (i.Estacao is { } e)
        {
            f.Add($"Estação: {e.Nome}, distância {e.DistanciaMetros / 1000.0:0.0} km");
            f.Add($"Chuva 1h: {e.Mm1h} mm; 3h: {e.Mm3h} mm; 24h: {e.Mm24h} mm; 72h: {e.Mm72h} mm");
        }
        f.AddRange(i.Avisos.Select(a => $"Aviso: {a.Titulo} ({a.Severidade})"));
        f.Add($"Ocorrências verificadas a até 1 km: {i.OcorrenciasVerificadas}");
        f.AddRange(i.Justificativa.Select(j => "Critério: " + j));
        f.Add("Estimativa de tempo: indisponível (sem modelo validado).");
        var texto = string.Join("\n", f);
        var numeros = new HashSet<string>(Regex.Matches(texto, @"\d+(?:[.,]\d+)?").Select(m => ValidadorExplicacao.Normalizar(m.Value)));
        return (texto, numeros);
    }
}
