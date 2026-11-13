using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Monitoramento;

namespace AlertaEmAcao.Modulos.InteligenciaTerritorial;

/// <summary>Indicador de ATENÇÃO (não é previsão de alagamento). Ordem crescente.</summary>
public enum NivelAtencaoAlagamento { SemEvidenciaSuficiente = 0, Atencao = 1, Elevado = 2, Critico = 3 }

/// <summary>Intensidade horária pela classificação da OMM (WMO Guide No. 8): fraca &lt; 2,5; moderada 2,5–10; forte 10–50; violenta &gt; 50 mm/h.</summary>
public enum IntensidadeChuva { SemChuva = 0, Fraca = 1, Moderada = 2, Forte = 3, Violenta = 4 }

public enum TendenciaChuva { Indeterminada, SemChuva, Crescente, Estavel, Decrescente }

public sealed record EstacaoUsada(string Nome, string Fonte, int DistanciaMetros, DateTimeOffset? LeituraEm, bool Atual,
    double? Mm1h, double? Mm3h, double? Mm6h, double? Mm12h, double? Mm24h, double? Mm48h, double? Mm72h);

public sealed record AvisoConsiderado(string Titulo, string Severidade, int Grau, bool Oficial, string Fonte, DateTimeOffset FimEm);

public sealed record Confiabilidade(string Nivel, IReadOnlyList<string> Motivos);

/// <summary>Estimativa temporal: só com modelo calibrado e validado historicamente. Nesta versão, nunca disponível.</summary>
public sealed record EstimativaTemporal(bool Disponivel, string Mensagem, string? Janela = null, string? Incerteza = null);

public sealed record IndicadorAlagamento(
    string Regiao,
    NivelAtencaoAlagamento Nivel,
    string Rotulo,
    IReadOnlyList<string> Justificativa,
    EstacaoUsada? Estacao,
    IntensidadeChuva Intensidade,
    string IntensidadeDescricao,
    TendenciaChuva Tendencia,
    string TendenciaDescricao,
    IReadOnlyList<AvisoConsiderado> Avisos,
    int OcorrenciasVerificadas,
    int RelatosNaoVerificados,
    string? SuscetibilidadeDocumentada,
    bool EstacoesDivergentes,
    EstimativaTemporal Estimativa,
    Confiabilidade Confiabilidade,
    IReadOnlyList<string> Limitacoes,
    IReadOnlyList<string> Orientacoes,
    DateTimeOffset AvaliadoEm,
    DateTimeOffset ValidoAte,
    string VersaoCriterios)
{
    /// <summary>As quatro naturezas de informação, sempre separadas.</summary>
    public object Separacao => new
    {
        chuvaObservada = Estacao is null ? "Sem leitura de estação utilizável." : $"Medida pela estação {Estacao.Nome} ({Estacao.Fonte}).",
        previsaoOficial = Avisos.Count == 0 ? "Nenhum aviso meteorológico aplicável." : string.Join("; ", Avisos.Select(a => $"{a.Titulo} — {a.Severidade} ({a.Fonte}{(a.Oficial ? "" : ", DEMONSTRATIVO")})")),
        estimativaExperimental = Estimativa.Mensagem,
        alagamentoConfirmado = OcorrenciasVerificadas > 0 ? $"{OcorrenciasVerificadas} ocorrência(s) verificada(s) por operador a até 1 km nas últimas 6 h (ambiente demonstrativo)." : "Nenhum alagamento confirmado por operador nesta região.",
    };
}

/// <summary>
/// Motor determinístico. Critérios (versão alagamento-2026.11.1):
/// CRÍTICO: aviso de chuva "Grande Perigo"; OU chuva violenta (&gt; 50 mm/h); OU chuva forte com ocorrência verificada a até 1 km.
/// ELEVADO: aviso "Perigo"; OU chuva forte (≥ 10 mm/h); OU ocorrência verificada a até 1 km nas últimas 6 h.
/// ATENÇÃO: aviso "Perigo Potencial"; OU chuva moderada (≥ 2,5 mm/h); OU chuva fraca com tendência crescente;
///          OU estação vizinha (≤ 5 km) com intensidade 2 classes acima da usada (chuva localizada).
/// SEM EVIDÊNCIA SUFICIENTE: nenhum critério acima, ou sem estação atual a até 10 km e sem aviso/ocorrência.
/// Acumulados de 24–72 h são exibidos como contexto, sem limite para afirmar alagamento (não há calibração local).
/// </summary>
public static class MotorIndicadorAlagamento
{
    public const string Versao = "alagamento-2026.11.1";
    public const double RaioPreferencialMetros = 5_000;
    public const double RaioMaximoMetros = 10_000;
    public const string SemEstimativa = "Há condições que exigem atenção, mas não existem dados suficientes para estimar quando poderá ocorrer um alagamento.";

    public static IntensidadeChuva Classificar(double? mm1h) => mm1h switch
    {
        null or < 0.2 => IntensidadeChuva.SemChuva,
        < 2.5 => IntensidadeChuva.Fraca,
        < 10 => IntensidadeChuva.Moderada,
        <= 50 => IntensidadeChuva.Forte,
        _ => IntensidadeChuva.Violenta,
    };

    /// <summary>Compara a última hora com a média das duas horas anteriores ((3h − 1h) / 2).</summary>
    public static TendenciaChuva Tendencia(double? mm1h, double? mm3h)
    {
        if (mm1h is null || mm3h is null || mm3h < mm1h) return TendenciaChuva.Indeterminada;
        if (mm3h < 0.2) return TendenciaChuva.SemChuva;
        var anterior = (mm3h.Value - mm1h.Value) / 2;
        if (mm1h >= 1.0 && mm1h > 1.5 * anterior) return TendenciaChuva.Crescente;
        if (anterior >= 1.0 && mm1h < 0.5 * anterior) return TendenciaChuva.Decrescente;
        return TendenciaChuva.Estavel;
    }

    /// <summary>Grau do aviso INMET pela severidade (só avisos ligados a chuva).</summary>
    public static int Grau(Aviso a)
    {
        var t = (a.Titulo + " " + string.Join(' ', a.Riscos)).ToLowerInvariant();
        if (!(t.Contains("chuva") || t.Contains("acumulado") || t.Contains("tempestade") || t.Contains("alag"))) return 0;
        var s = a.Severidade.ToLowerInvariant();
        return s.Contains("grande perigo") ? 3 : s.Contains("perigo potencial") ? 1 : s.Contains("perigo") ? 2 : 1;
    }

    public static IndicadorAlagamento Avaliar(PontoGeo ponto, string regiao, IEnumerable<LeituraChuva> leituras, IEnumerable<Aviso> avisos,
        string? municipioCodigo, int ocorrenciasVerificadas, int relatosNaoVerificados, string? suscetibilidade, bool fonteChuvaDisponivel, DateTimeOffset agora)
    {
        var lim = new List<string>
        {
            "Indicador de atenção baseado em regras; não é previsão de alagamento de uma rua ou imóvel.",
            "Não há dados integrados de relevo, drenagem ou maré nesta versão.",
            "Chuva medida numa estação não comprova alagamento no seu ponto.",
        };
        var just = new List<string>();
        var conf = new List<string>();

        var proximas = leituras.Select(l => (L: l, D: new PontoGeo(l.Latitude, l.Longitude).DistanciaMetros(ponto)))
            .Where(x => x.D <= RaioMaximoMetros).OrderBy(x => x.D).ToList();
        var atuais = proximas.Where(x => x.L.Estado(agora) == EstadoLeitura.Atual).ToList();
        var usada = atuais.FirstOrDefault();
        EstacaoUsada? est = usada.L is null ? null : new(usada.L.Nome, usada.L.Fonte, (int)Math.Round(usada.D), usada.L.UltimaLeituraEm, true,
            usada.L.Mm1h, usada.L.Mm3h, usada.L.Mm6h, usada.L.Mm12h, usada.L.Mm24h, usada.L.Mm48h, usada.L.Mm72h);

        var intensidade = est is null ? IntensidadeChuva.SemChuva : Classificar(est.Mm1h);
        var tendencia = est is null ? TendenciaChuva.Indeterminada : Tendencia(est.Mm1h, est.Mm3h);

        var avs = avisos.Where(a => a.Vigente(agora) && a.Abrange(ponto, municipioCodigo)).Select(a => (A: a, G: Grau(a))).Where(x => x.G > 0)
            .Select(x => new AvisoConsiderado(x.A.Titulo, x.A.Severidade, x.G, x.A.Oficial, x.A.FonteNome, x.A.FimEm)).OrderByDescending(a => a.Grau).ToList();
        var grau = avs.Count == 0 ? 0 : avs.Max(a => a.Grau);

        // Estações vizinhas divergentes: chuva localizada não captada pela estação usada.
        var vizinhaMax = atuais.Where(x => x.D <= RaioPreferencialMetros).Select(x => Classificar(x.L.Mm1h)).DefaultIfEmpty(IntensidadeChuva.SemChuva).Max();
        var divergentes = est is not null && vizinhaMax - intensidade >= 2;

        var nivel = NivelAtencaoAlagamento.SemEvidenciaSuficiente;
        void Subir(NivelAtencaoAlagamento n, string motivo) { if (n > nivel) nivel = n; just.Add(motivo); }

        if (grau == 3) Subir(NivelAtencaoAlagamento.Critico, "Aviso meteorológico de chuva com severidade \"Grande Perigo\" para esta região.");
        if (intensidade == IntensidadeChuva.Violenta) Subir(NivelAtencaoAlagamento.Critico, "Chuva violenta medida na última hora (acima de 50 mm/h, classificação da OMM).");
        if (intensidade >= IntensidadeChuva.Forte && ocorrenciasVerificadas > 0) Subir(NivelAtencaoAlagamento.Critico, "Chuva forte e ocorrência verificada por operador a até 1 km.");
        if (grau == 2) Subir(NivelAtencaoAlagamento.Elevado, "Aviso meteorológico de chuva com severidade \"Perigo\".");
        if (intensidade == IntensidadeChuva.Forte) Subir(NivelAtencaoAlagamento.Elevado, "Chuva forte medida na última hora (10 a 50 mm/h, classificação da OMM).");
        if (ocorrenciasVerificadas > 0) Subir(NivelAtencaoAlagamento.Elevado, "Ocorrência verificada por operador a até 1 km nas últimas 6 h.");
        if (grau == 1) Subir(NivelAtencaoAlagamento.Atencao, "Aviso meteorológico de chuva com severidade \"Perigo Potencial\".");
        if (intensidade == IntensidadeChuva.Moderada) Subir(NivelAtencaoAlagamento.Atencao, "Chuva moderada medida na última hora (2,5 a 10 mm/h, classificação da OMM).");
        if (intensidade == IntensidadeChuva.Fraca && tendencia == TendenciaChuva.Crescente) Subir(NivelAtencaoAlagamento.Atencao, "Chuva fraca, mas aumentando em relação às duas horas anteriores.");
        if (divergentes) Subir(NivelAtencaoAlagamento.Atencao, $"Estação a até 5 km mede chuva {Descrever(vizinhaMax).ToLowerInvariant()}, bem acima da estação usada: chuva localizada.");

        if (!fonteChuvaDisponivel) just.Add("Monitoramento de chuva da APAC indisponível agora.");
        if (est is null && fonteChuvaDisponivel)
            just.Add(proximas.Count == 0 ? "Nenhuma estação pluviométrica a até 10 km." : "As estações a até 10 km estão sem leitura recente (mais de 2 h).");
        if (nivel == NivelAtencaoAlagamento.SemEvidenciaSuficiente && just.Count == 0)
            just.Add(intensidade == IntensidadeChuva.SemChuva ? "Sem chuva relevante na última hora e sem avisos ou ocorrências verificadas." : "Chuva fraca, sem avisos ou ocorrências verificadas.");
        if (relatosNaoVerificados > 0) just.Add($"{relatosNaoVerificados} pedido(s) de socorro não verificado(s) a até 1 km (não altera o indicador).");
        if (suscetibilidade is not null) just.Add("Há área de risco documentada próxima (risco geológico/encostas; não é mapa de alagamento).");

        // Confiabilidade explícita.
        var nivelConf = "baixa";
        if (est is null) conf.Add("Sem leitura atual de estação próxima.");
        else
        {
            var idadeMin = est.LeituraEm is { } t ? (agora - t).TotalMinutes : 999;
            if (est.DistanciaMetros <= 2_000 && idadeMin <= 75) nivelConf = "alta";
            else if (est.DistanciaMetros <= RaioPreferencialMetros) nivelConf = "média";
            conf.Add($"Estação a {est.DistanciaMetros / 1000.0:0.0} km; leitura de {idadeMin:0} min atrás.");
            if (est.DistanciaMetros > RaioPreferencialMetros) conf.Add("Estação a mais de 5 km: a chuva no seu ponto pode ser diferente.");
        }
        if (divergentes) { nivelConf = "baixa"; conf.Add("Estações próximas divergem."); }
        if (tendencia == TendenciaChuva.Indeterminada && est is not null) conf.Add("Sem série de 3 h para calcular tendência.");

        var estimativa = new EstimativaTemporal(false, nivel == NivelAtencaoAlagamento.SemEvidenciaSuficiente
            ? "Sem condições de atenção identificadas; nenhuma estimativa temporal é feita."
            : SemEstimativa);
        var orient = nivel switch
        {
            NivelAtencaoAlagamento.Critico => new[] { "Se houver risco imediato, ligue 193 (Bombeiros) ou 199 (Defesa Civil).", "Não atravesse ruas alagadas nem áreas com correnteza.", "Desligue a energia se a água entrar em casa e vá para um local mais alto." },
            NivelAtencaoAlagamento.Elevado => new[] { "Evite deslocamentos por áreas que costumam alagar.", "Prepare documentos, remédios e carregador em local alto.", "Acompanhe os avisos oficiais." },
            NivelAtencaoAlagamento.Atencao => new[] { "Acompanhe a evolução da chuva e os avisos oficiais.", "Evite áreas que costumam alagar." },
            _ => new[] { "Mantenha os contatos de emergência à mão: 193 e 199." },
        };
        var validade = (est?.LeituraEm ?? agora) + LeituraChuva.LimiteAtualidade;
        return new IndicadorAlagamento(regiao, nivel, Rotular(nivel), just, est, intensidade, Descrever(intensidade), tendencia, Descrever(tendencia),
            avs, ocorrenciasVerificadas, relatosNaoVerificados, suscetibilidade, divergentes, estimativa, new Confiabilidade(nivelConf, conf), lim, orient,
            agora, validade < agora ? agora.AddMinutes(10) : validade, Versao);
    }

    public static string Rotular(NivelAtencaoAlagamento n) => n switch
    {
        NivelAtencaoAlagamento.Critico => "Crítico",
        NivelAtencaoAlagamento.Elevado => "Elevado",
        NivelAtencaoAlagamento.Atencao => "Atenção",
        _ => "Sem evidência suficiente",
    };

    public static string Descrever(IntensidadeChuva i) => i switch
    {
        IntensidadeChuva.Violenta => "Violenta", IntensidadeChuva.Forte => "Forte", IntensidadeChuva.Moderada => "Moderada",
        IntensidadeChuva.Fraca => "Fraca", _ => "Sem chuva",
    };

    public static string Descrever(TendenciaChuva t) => t switch
    {
        TendenciaChuva.Crescente => "Aumentando", TendenciaChuva.Decrescente => "Diminuindo", TendenciaChuva.Estavel => "Estável",
        TendenciaChuva.SemChuva => "Sem chuva nas últimas 3 h", _ => "Não calculada",
    };
}
