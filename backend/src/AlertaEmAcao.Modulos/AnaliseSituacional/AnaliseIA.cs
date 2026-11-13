using System.Text.Json;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Modulos.AnaliseSituacional;

public enum StatusAnaliseIA
{
    /// <summary>Nenhum provedor de IA configurado: somente regras determinísticas.</summary>
    NaoConfigurada,
    Pendente,
    Concluida,
    /// <summary>Provedor falhou (timeout, erro HTTP). Ocorrência segue normalmente.</summary>
    Falhou,
    /// <summary>Resposta não passou na validação de esquema/evidências e foi descartada.</summary>
    RejeitadaPelaValidacao,
}

/// <summary>Entrada enviada ao modelo — contém apenas o necessário (minimização de dados).</summary>
public sealed record EntradaAnaliseIA(
    string OcorrenciaId,
    string? Municipio,
    string HorarioOcorrencia,
    string? UltimaAtualizacaoLocalizacao,
    bool PossuiCoordenadas,
    ComplementoOcorrencia Complemento,
    ContextoTerritorialResumo? Contexto,
    IReadOnlyList<string> SinalizacoesRegras);

public sealed record RespostaBrutaIA(string Json, string Modelo, string Provedor, long DuracaoMs);

public interface IProvedorIA
{
    bool Configurado { get; }
    string Descricao { get; }
    Task<RespostaBrutaIA> AnalisarAsync(EntradaAnaliseIA entrada, CancellationToken ct);
}

// ---- Esquema de saída esperado da IA ----
public sealed record FatoExtraido(string Descricao, string Evidencia);
public sealed record PerigoIdentificado(string Tipo, string Descricao, string Evidencia);
public sealed record SinalizacaoIA(string Codigo, string Explicacao, string Evidencia);

public sealed record ResultadoAnaliseIA
{
    public required string Resumo { get; init; }
    public required List<FatoExtraido> InformacoesExtraidas { get; init; }
    public int? PessoasEnvolvidas { get; init; }
    public required List<PerigoIdentificado> PossiveisPerigos { get; init; }
    public required List<string> NecessidadesAssistencia { get; init; }
    public required List<string> IndiciosAgravamento { get; init; }
    public required List<string> InformacoesAusentes { get; init; }
    public required List<string> InformacoesNaoVerificadas { get; init; }
    public required List<SinalizacaoIA> SinalizacoesParaAvaliacaoHumana { get; init; }
}

public sealed record ResultadoValidacaoIA(bool Valido, ResultadoAnaliseIA? Resultado, IReadOnlyList<string> Problemas, int ItensDescartados);

/// <summary>
/// Valida a resposta da IA: JSON bem formado, campos obrigatórios, limites, e ancoragem —
/// cada fato/perigo/sinalização precisa citar uma evidência presente na entrada. Itens sem
/// evidência verificável são descartados (proteção contra invenção). Se o resumo citar números de
/// pessoas inexistentes na entrada, a resposta inteira é rejeitada.
/// </summary>
public static class ValidadorSaidaIA
{
    private static readonly HashSet<string> CodigosPermitidos =
    [
        "AGUA_SUBINDO", "ILHAMENTO", "CORRENTEZA", "RISCO_GEOLOGICO", "DESABAMENTO", "PESSOA_FERIDA",
        "NECESSIDADE_MEDICA", "NECESSIDADE_ASSISTENCIA", "PESSOA_ISOLADA", "ACESSO_BLOQUEADO",
        "SEGURANCA_PUBLICA", "LOCALIZACAO_INCERTA", "INFORMACOES_CONTRADITORIAS", "AGRAVAMENTO", "OUTRO",
    ];

    public static ResultadoValidacaoIA Validar(string json, EntradaAnaliseIA entrada)
    {
        var problemas = new List<string>();
        ResultadoAnaliseIA? r;
        try
        {
            var limpo = ExtrairObjetoJson(json);
            r = JsonSerializer.Deserialize<ResultadoAnaliseIA>(limpo, Comum.Json.Opcoes);
        }
        catch (Exception e)
        {
            return new(false, null, [$"JSON inválido ou fora do esquema: {e.Message}"], 0);
        }
        if (r is null) return new(false, null, ["Resposta vazia."], 0);
        if (string.IsNullOrWhiteSpace(r.Resumo) || r.Resumo.Length > 800) problemas.Add("Resumo ausente ou maior que 800 caracteres.");

        var corpus = DetectorIndicios.Normalizar(MontarCorpus(entrada));
        bool Ancorada(string? evidencia) =>
            !string.IsNullOrWhiteSpace(evidencia) && evidencia.Length <= 300 &&
            corpus.Contains(DetectorIndicios.Normalizar(evidencia.Trim().Trim('"', '…', '.')), StringComparison.Ordinal);

        var descartados = 0;
        var fatos = r.InformacoesExtraidas.Where(f => Ancorada(f.Evidencia) || Contar(ref descartados)).ToList();
        var perigos = r.PossiveisPerigos.Where(p => Ancorada(p.Evidencia) || Contar(ref descartados)).ToList();
        var sinais = r.SinalizacoesParaAvaliacaoHumana
            .Where(s => (CodigosPermitidos.Contains(s.Codigo) || Contar(ref descartados)) && (Ancorada(s.Evidencia) || Contar(ref descartados)))
            .ToList();

        // Número de pessoas: só aceito se coincidir com algo informado.
        int? pessoas = r.PessoasEnvolvidas;
        var informados = NumerosInformados(entrada.Complemento);
        if (pessoas is not null && !informados.Contains(pessoas.Value))
        {
            problemas.Add($"pessoasEnvolvidas={pessoas} não corresponde a nenhum dado informado; removido.");
            pessoas = null;
            descartados++;
        }

        if (problemas.Any(p => p.StartsWith("Resumo"))) return new(false, null, problemas, descartados);
        if (descartados > 0) problemas.Add($"{descartados} item(ns) sem evidência verificável foram descartados.");

        var final = r with
        {
            InformacoesExtraidas = fatos,
            PossiveisPerigos = perigos,
            SinalizacoesParaAvaliacaoHumana = sinais,
            PessoasEnvolvidas = pessoas,
            NecessidadesAssistencia = r.NecessidadesAssistencia.Take(20).ToList(),
            IndiciosAgravamento = r.IndiciosAgravamento.Take(20).ToList(),
            InformacoesAusentes = r.InformacoesAusentes.Take(20).ToList(),
            InformacoesNaoVerificadas = r.InformacoesNaoVerificadas.Take(20).ToList(),
        };
        return new(true, final, problemas, descartados);
    }

    private static bool Contar(ref int n) { n++; return false; }

    private static HashSet<int> NumerosInformados(ComplementoOcorrencia c)
    {
        var s = new HashSet<int>();
        if (c.QuantidadePessoas is { } q) s.Add(q);
        if (c.Pessoas is { Count: > 0 } p) s.Add(p.Count);
        if (c.Sozinho == true) s.Add(1);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(c.Descricao ?? "", @"\b(\d{1,3})\b"))
            s.Add(int.Parse(m.Value));
        return s;
    }

    public static string MontarCorpus(EntradaAnaliseIA e)
    {
        // A evidência pode citar o relato livre ou a representação textual dos campos estruturados.
        var c = e.Complemento;
        var partes = new List<string?> { c.Descricao, c.OutrosRiscos, c.ReferenciaLocal, e.Municipio };
        partes.AddRange(DetectorIndicios.Detectar(c).SelectMany(i => i.Evidencias).Select(ev => ev.Trecho));
        partes.AddRange(e.SinalizacoesRegras);
        if (c.QuantidadePessoas is { } q) partes.Add($"Quantidade de pessoas: {q}");
        if (c.IdadeSolicitante is { } idade) partes.Add($"Idade do solicitante: {idade}");
        if (c.Pessoas is { } ps) partes.AddRange(ps.Select((p, i) => $"Pessoa {i + 1}: idade {p.Idade?.ToString() ?? "não informada"}{(p.Ferida ? ", ferida" : "")}{(p.MobilidadeReduzida ? ", mobilidade reduzida" : "")}"));
        if (!e.PossuiCoordenadas) partes.Add("Sem coordenadas");
        if (e.Contexto is { } ctx)
        {
            partes.Add(ctx.DescricaoAviso);
            partes.Add(ctx.DescricaoSuscetibilidade);
            if (ctx.ChuvaUltimas24hMm is { } mm) partes.Add($"Chuva 24h: {mm} mm");
        }
        return string.Join("\n", partes.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string ExtrairObjetoJson(string texto)
    {
        var ini = texto.IndexOf('{');
        var fim = texto.LastIndexOf('}');
        if (ini < 0 || fim <= ini) throw new FormatException("Nenhum objeto JSON encontrado.");
        return texto[ini..(fim + 1)];
    }
}

/// <summary>Resultado consolidado (regras + IA) persistido por ocorrência.</summary>
public sealed class AnaliseOcorrencia
{
    public Guid OcorrenciaId { get; private set; }
    public string VersaoRegras { get; private set; } = default!;
    public List<Sinalizacao> Sinalizacoes { get; private set; } = [];
    public NivelSinalizacao? NivelMaximo { get; private set; }
    public ContextoTerritorialResumo? Contexto { get; private set; }
    public List<ReferenciaFonte> Fontes { get; private set; } = [];
    public StatusAnaliseIA StatusIA { get; private set; }
    public ResultadoAnaliseIA? ResultadoIA { get; private set; }
    public List<string> ProblemasValidacaoIA { get; private set; } = [];
    public string? ModeloIA { get; private set; }
    public long? DuracaoIAMs { get; private set; }
    public int TentativasIA { get; private set; }
    public DateTimeOffset RegrasAvaliadasEm { get; private set; }
    public DateTimeOffset? IAConcluidaEm { get; private set; }

    private AnaliseOcorrencia() { }

    public AnaliseOcorrencia(Guid ocorrenciaId) { OcorrenciaId = ocorrenciaId; }

    public void AplicarRegras(IReadOnlyList<Sinalizacao> s, ContextoTerritorialResumo? ctx, IEnumerable<ReferenciaFonte> fontes, bool iaConfigurada, DateTimeOffset agora)
    {
        VersaoRegras = MotorRegrasUrgencia.Versao;
        Sinalizacoes = s.ToList();
        NivelMaximo = MotorRegrasUrgencia.NivelMaximo(s);
        Contexto = ctx;
        Fontes = fontes.ToList();
        RegrasAvaliadasEm = agora;
        if (StatusIA != StatusAnaliseIA.Concluida)
            StatusIA = iaConfigurada ? StatusAnaliseIA.Pendente : StatusAnaliseIA.NaoConfigurada;
    }

    public void RegistrarIA(ResultadoValidacaoIA v, string modelo, long duracaoMs, DateTimeOffset agora)
    {
        TentativasIA++;
        ModeloIA = modelo;
        DuracaoIAMs = duracaoMs;
        ProblemasValidacaoIA = v.Problemas.ToList();
        IAConcluidaEm = agora;
        if (v.Valido) { ResultadoIA = v.Resultado; StatusIA = StatusAnaliseIA.Concluida; }
        else StatusIA = StatusAnaliseIA.RejeitadaPelaValidacao;
    }

    public void RegistrarFalhaIA(string erro, DateTimeOffset agora)
    {
        TentativasIA++;
        ProblemasValidacaoIA = [erro];
        IAConcluidaEm = agora;
        StatusIA = StatusAnaliseIA.Falhou;
    }

    public bool PrecisaIA => StatusIA == StatusAnaliseIA.Pendente || (StatusIA == StatusAnaliseIA.Falhou && TentativasIA < 3);
}
