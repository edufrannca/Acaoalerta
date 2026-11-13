using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Monitoramento;

namespace AlertaEmAcao.Modulos.InteligenciaTerritorial;

/// <summary>Área/ponto de risco documentado por fonte pública (ex.: Defesa Civil do Recife, Dados Abertos).</summary>
public sealed class AreaRiscoDocumentada
{
    public Guid Id { get; private set; }
    public string Fonte { get; private set; } = default!;
    public string? FonteUrl { get; private set; }
    public string IdExterno { get; private set; } = default!;
    public string Descricao { get; private set; } = default!;
    public string? Bairro { get; private set; }
    public string? Localidade { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string MunicipioCodigo { get; private set; } = default!;
    public string Licenca { get; private set; } = default!;
    public DateTimeOffset ObtidaEm { get; private set; }

    private AreaRiscoDocumentada() { }

    public static AreaRiscoDocumentada Criar(string fonte, string? url, string idExterno, string descricao, string? bairro, string? localidade,
        PontoGeo p, string municipio, string licenca, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(), Fonte = fonte, FonteUrl = url, IdExterno = idExterno, Descricao = descricao, Bairro = bairro,
        Localidade = localidade, Latitude = p.Latitude, Longitude = p.Longitude, MunicipioCodigo = municipio, Licenca = licenca, ObtidaEm = agora,
    };

    public PontoGeo Ponto => new(Latitude, Longitude);
}

public enum TipoEstadoInformativo
{
    SuscetibilidadeConhecida,
    AvisoOficialVigente,
    AvisoDemonstrativoVigente,
    OcorrenciaRelatada,
    OcorrenciaVerificada,
    InformacoesDesatualizadas,
    CondicaoDesconhecida,
}

public sealed record EstadoInformativo(TipoEstadoInformativo Tipo, string Titulo, string Detalhe, ReferenciaFonte? Fonte);

public sealed record ContextoTerritorial(
    string? MunicipioCodigo,
    string? MunicipioNome,
    bool ForaDaRmr,
    IReadOnlyList<EstadoInformativo> Estados,
    IReadOnlyList<string> OrientacoesPreventivas,
    IReadOnlyList<string> LimitacoesConhecidas,
    LeituraChuva? EstacaoMaisProxima,
    double? DistanciaEstacaoKm,
    DateTimeOffset AvaliadoEm);

public sealed record OcorrenciaProxima(double Latitude, double Longitude, bool Verificada, DateTimeOffset Em);

/// <summary>
/// Combina dados geográficos e climáticos em estados informativos. Nunca conclui que uma
/// área é "segura": na ausência de registros, informa "Condição desconhecida".
/// </summary>
public static class AvaliadorContextoTerritorial
{
    public const double RaioSuscetibilidadeMetros = 150;
    public const double RaioOcorrenciasMetros = 1000;

    public static ContextoTerritorial Avaliar(PontoGeo ponto, string? municipioCodigo, string? municipioNome,
        IEnumerable<AreaRiscoDocumentada> areas, IEnumerable<Aviso> avisos, IEnumerable<LeituraChuva> leituras,
        IEnumerable<OcorrenciaProxima> ocorrencias, bool fonteAreasDisponivel, bool fonteChuvaDisponivel, DateTimeOffset agora)
    {
        var estados = new List<EstadoInformativo>();
        var limitacoes = new List<string>();

        if (municipioCodigo is null)
        {
            return new ContextoTerritorial(null, null, true,
                [new(TipoEstadoInformativo.CondicaoDesconhecida, "Fora da RMR", "Esta localização não foi identificada nos 14 municípios da Região Metropolitana do Recife.", null)],
                [], ["Cobertura do Alerta em Ação restrita à RMR."], null, null, agora);
        }

        var proximas = areas.Where(a => a.MunicipioCodigo == municipioCodigo)
            .Select(a => (Area: a, Dist: a.Ponto.DistanciaMetros(ponto)))
            .Where(x => x.Dist <= RaioSuscetibilidadeMetros).OrderBy(x => x.Dist).ToList();
        if (proximas.Count > 0)
        {
            var a = proximas[0].Area;
            estados.Add(new(TipoEstadoInformativo.SuscetibilidadeConhecida, "Suscetibilidade conhecida",
                $"{proximas.Count} registro(s) de área de risco a até {RaioSuscetibilidadeMetros:0} m. Mais próximo: {a.Descricao}" +
                $"{(a.Localidade is null ? "" : $" — {a.Localidade}")}{(a.Bairro is null ? "" : $", {a.Bairro}")} ({proximas[0].Dist:0} m).",
                new ReferenciaFonte(a.Fonte, a.FonteUrl, a.ObtidaEm, true)));
        }

        foreach (var aviso in avisos.Where(x => x.Vigente(agora) && x.Abrange(ponto, municipioCodigo)))
        {
            estados.Add(new(aviso.Oficial ? TipoEstadoInformativo.AvisoOficialVigente : TipoEstadoInformativo.AvisoDemonstrativoVigente,
                aviso.Oficial ? "Aviso oficial vigente" : "Aviso DEMONSTRATIVO vigente (não oficial)",
                $"{aviso.Titulo} — {aviso.Severidade}. Vigente até {aviso.FimEm.ToOffset(TimeSpan.FromHours(-3)):dd/MM HH:mm}.",
                new ReferenciaFonte(aviso.FonteNome, aviso.FonteUrl, aviso.ObtidoEm, aviso.Oficial, !aviso.Oficial)));
        }

        var occ = ocorrencias.Where(o => agora - o.Em <= TimeSpan.FromHours(6))
            .Where(o => new PontoGeo(o.Latitude, o.Longitude).DistanciaMetros(ponto) <= RaioOcorrenciasMetros).ToList();
        var verificadas = occ.Count(o => o.Verificada);
        var relatadas = occ.Count - verificadas;
        if (relatadas > 0)
            estados.Add(new(TipoEstadoInformativo.OcorrenciaRelatada, "Ocorrência relatada",
                $"{relatadas} pedido(s) de socorro a até 1 km nas últimas 6 h, ainda não confirmados. Detalhes aparecem em \"Pedidos de ajuda\" só quando o solicitante compartilha.",
                new ReferenciaFonte("Alerta em Ação — relatos de cidadãos", null, agora, false)));
        if (verificadas > 0)
            estados.Add(new(TipoEstadoInformativo.OcorrenciaVerificada, "Ocorrência verificada",
                $"{verificadas} ocorrência(s) confirmada(s) por operador a até 1 km nas últimas 6 h.",
                new ReferenciaFonte("Alerta em Ação — verificação por operador (ambiente demonstrativo)", null, agora, false, true)));

        var lista = leituras.ToList();
        var maisProxima = lista
            .Select(l => (L: l, D: new PontoGeo(l.Latitude, l.Longitude).DistanciaMetros(ponto)))
            .OrderBy(x => x.L.Estado(agora) == EstadoLeitura.Atual ? 0 : 1).ThenBy(x => x.D)
            .FirstOrDefault();
        if (!fonteChuvaDisponivel)
            estados.Add(new(TipoEstadoInformativo.InformacoesDesatualizadas, "Monitoramento de chuva indisponível",
                "Não foi possível consultar a APAC agora. Dados de chuva podem estar ausentes ou antigos.", null));
        else if (maisProxima.L is null || maisProxima.D > 10_000)
            estados.Add(new(TipoEstadoInformativo.InformacoesDesatualizadas, "Sem estação próxima", "Nenhuma estação pluviométrica a até 10 km.", null));
        else if (maisProxima.L.Estado(agora) != EstadoLeitura.Atual)
            estados.Add(new(TipoEstadoInformativo.InformacoesDesatualizadas, "Informações desatualizadas",
                $"A leitura mais recente da estação mais próxima ({maisProxima.L.Nome}) é de " +
                $"{(maisProxima.L.UltimaLeituraEm is null ? "data desconhecida" : maisProxima.L.UltimaLeituraEm.Value.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM HH:mm"))}.",
                new ReferenciaFonte(maisProxima.L.Fonte, null, maisProxima.L.UltimaLeituraEm, true)));

        if (!fonteAreasDisponivel)
            limitacoes.Add("Base de áreas de risco indisponível no momento.");
        if (municipioCodigo != "2611606")
            limitacoes.Add("Não localizamos base pública aberta de setores de risco para este município; ausência de registro não significa ausência de risco.");
        else
            limitacoes.Add("Base aberta de áreas de risco do Recife consultada cobre a Regional Sul; demais regionais podem não estar representadas.");

        if (!estados.Any(e => e.Tipo is TipoEstadoInformativo.SuscetibilidadeConhecida or TipoEstadoInformativo.AvisoOficialVigente
                or TipoEstadoInformativo.AvisoDemonstrativoVigente or TipoEstadoInformativo.OcorrenciaRelatada or TipoEstadoInformativo.OcorrenciaVerificada))
            estados.Add(new(TipoEstadoInformativo.CondicaoDesconhecida, "Condição desconhecida",
                "Não há dados suficientes sobre risco neste ponto. A ausência de registros não comprova ausência de perigo.", null));

        return new ContextoTerritorial(municipioCodigo, municipioNome, false, estados, OrientacoesPreventivas.Para(estados), limitacoes,
            maisProxima.L, maisProxima.L is null ? null : Math.Round(maisProxima.D / 1000, 1), agora);
    }
}

/// <summary>
/// Orientações preventivas previamente aprovadas (adaptadas de orientações públicas de Defesa Civil).
/// A IA pode apenas simplificar estes textos; não cria ordens de evacuação.
/// </summary>
public static class OrientacoesPreventivas
{
    public static readonly string[] Gerais =
    [
        "Tenha à mão documentos, remédios de uso contínuo, lanterna e carregador em saco plástico.",
        "Combine com a família um ponto de encontro seguro e um contato de referência.",
        "Em emergência, ligue 193 (Bombeiros) ou 199 (Defesa Civil). Para urgências médicas, 192 (SAMU).",
        "Siga sempre as orientações da Defesa Civil do seu município.",
    ];

    public static readonly string[] Alagamento =
    [
        "Não atravesse ruas alagadas a pé ou de carro — a correnteza pode arrastar pessoas e veículos.",
        "Desligue a energia elétrica se a água estiver entrando em casa.",
        "Procure locais mais altos e evite contato com a água (risco de leptospirose).",
    ];

    public static readonly string[] Encosta =
    [
        "Observe rachaduras novas em paredes ou no solo, portas emperrando, árvores ou postes inclinando: são sinais de deslizamento.",
        "Durante chuva forte em área de encosta, se houver sinais de movimento de terra, saia do local e avise a Defesa Civil (199).",
        "Não faça cortes ou aterros no barranco e não jogue lixo nas encostas.",
    ];

    public static IReadOnlyList<string> Para(IEnumerable<EstadoInformativo> estados)
    {
        var l = new List<string>();
        var texto = string.Join(" ", estados.Select(e => e.Detalhe)).ToLowerInvariant();
        if (texto.Contains("risco") || texto.Contains("encosta") || texto.Contains("desliz")) l.AddRange(Encosta);
        if (texto.Contains("chuva") || texto.Contains("alag") || texto.Contains("tempestade") || texto.Contains("acumulado")) l.AddRange(Alagamento);
        l.AddRange(Gerais);
        return l.Distinct().ToList();
    }
}

public interface IFonteAreasRisco
{
    Task<(List<AreaRiscoDocumentada> Areas, bool Disponivel, string? Erro)> ObterAsync(CancellationToken ct);
}
