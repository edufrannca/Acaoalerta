using AlertaEmAcao.Modulos.Comum;

namespace AlertaEmAcao.Modulos.Municipios;

/// <summary>
/// Os 14 municípios da Região Metropolitana do Recife.
/// Códigos IBGE conferidos em 12/11/2026 na camada "Limite municipal" (id 7) do serviço
/// met_monitoramento_chuvas_pe da APAC (campo cd_mun).
/// </summary>
public static class MunicipiosRmr
{
    public static readonly IReadOnlyList<(string Codigo, string Nome)> Todos =
    [
        ("2611606", "Recife"),
        ("2607901", "Jaboatão dos Guararapes"),
        ("2609600", "Olinda"),
        ("2610707", "Paulista"),
        ("2603454", "Camaragibe"),
        ("2613701", "São Lourenço da Mata"),
        ("2602902", "Cabo de Santo Agostinho"),
        ("2607208", "Ipojuca"),
        ("2609402", "Moreno"),
        ("2600054", "Abreu e Lima"),
        ("2606804", "Igarassu"),
        ("2607752", "Itapissuma"),
        ("2607604", "Ilha de Itamaracá"),
        ("2601052", "Araçoiaba"),
    ];

    public static readonly IReadOnlySet<string> Codigos = Todos.Select(m => m.Codigo).ToHashSet();

    public static string? Nome(string? codigo) => Todos.FirstOrDefault(m => m.Codigo == codigo).Nome;

    public static bool Pertence(string? codigo) => codigo is not null && Codigos.Contains(codigo);
}

public sealed class Municipio
{
    public string CodigoIbge { get; private set; } = default!;
    public string Nome { get; private set; } = default!;
    public string Uf { get; private set; } = "PE";
    /// <summary>GeoJSON (MultiPolygon) do limite municipal.</summary>
    public string GeometriaGeoJson { get; private set; } = default!;
    public string FonteGeometria { get; private set; } = default!;
    public bool GeometriaOficialAoVivo { get; private set; }
    public DateTimeOffset GeometriaAtualizadaEm { get; private set; }
    /// <summary>Limitações conhecidas dos dados públicos deste município, exibidas com transparência.</summary>
    public string? ObservacoesDados { get; private set; }

    private Municipio() { }

    public Municipio(string codigo, string nome, string geoJson, string fonte, bool oficialAoVivo, DateTimeOffset em, string? observacoes = null)
    {
        if (!MunicipiosRmr.Pertence(codigo)) throw new ErroDominio("municipio_fora_rmr", $"Código {codigo} não pertence à RMR.");
        CodigoIbge = codigo; Nome = nome; GeometriaGeoJson = geoJson; FonteGeometria = fonte;
        GeometriaOficialAoVivo = oficialAoVivo; GeometriaAtualizadaEm = em; ObservacoesDados = observacoes;
    }

    public void AtualizarGeometria(string geoJson, string fonte, bool oficialAoVivo, DateTimeOffset em)
    {
        GeometriaGeoJson = geoJson; FonteGeometria = fonte; GeometriaOficialAoVivo = oficialAoVivo; GeometriaAtualizadaEm = em;
    }

    public void DefinirObservacoes(string? obs) => ObservacoesDados = obs;
}

public sealed record MunicipioIdentificado(string Codigo, string Nome, bool PorTolerancia);

/// <summary>Identifica o município de uma coordenada usando os limites carregados.</summary>
public sealed class IdentificadorMunicipio
{
    private readonly IReadOnlyList<(string Codigo, string Nome, MultiPoligonoGeo Geometria)> _limites;
    public const double ToleranciaBordaMetros = 300;

    public IdentificadorMunicipio(IEnumerable<(string Codigo, string Nome, MultiPoligonoGeo Geometria)> limites)
    {
        _limites = limites.ToList();
    }

    public MunicipioIdentificado? Identificar(PontoGeo ponto)
    {
        foreach (var (codigo, nome, geo) in _limites)
            if (geo.Contem(ponto)) return new MunicipioIdentificado(codigo, nome, false);

        // Pontos costeiros/de fronteira podem cair fora de polígonos simplificados.
        // Aceita o município mais próximo apenas dentro da tolerância, sinalizando isso.
        var maisProximo = _limites
            .Select(l => (l.Codigo, l.Nome, Dist: l.Geometria.DistanciaAproximadaBordaMetros(ponto)))
            .OrderBy(x => x.Dist)
            .FirstOrDefault();
        return maisProximo.Codigo is not null && maisProximo.Dist <= ToleranciaBordaMetros
            ? new MunicipioIdentificado(maisProximo.Codigo, maisProximo.Nome, true)
            : null;
    }
}

/// <summary>Leitura de GeoJSON de geometria (Polygon/MultiPolygon) para o tipo de domínio.</summary>
public static class LeitorGeoJson
{
    public static MultiPoligonoGeo LerGeometria(string geoJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(geoJson);
        return LerGeometria(doc.RootElement);
    }

    public static MultiPoligonoGeo LerGeometria(System.Text.Json.JsonElement geom)
    {
        var tipo = geom.GetProperty("type").GetString();
        var coords = geom.GetProperty("coordinates");
        static List<double[]> Anel(System.Text.Json.JsonElement a) =>
            a.EnumerateArray().Select(v => new[] { v[0].GetDouble(), v[1].GetDouble() }).ToList();
        static List<IReadOnlyList<double[]>> Poligono(System.Text.Json.JsonElement p) =>
            p.EnumerateArray().Select(a => (IReadOnlyList<double[]>)Anel(a)).ToList();

        return tipo switch
        {
            "Polygon" => MultiPoligonoGeo.DePoligono(Poligono(coords)),
            "MultiPolygon" => new MultiPoligonoGeo(coords.EnumerateArray().Select(p => (IReadOnlyList<IReadOnlyList<double[]>>)Poligono(p)).ToList()),
            _ => throw new ErroDominio("geometria_invalida", $"Tipo de geometria não suportado: {tipo}"),
        };
    }
}

/// <summary>Porta para obter limites municipais de uma fonte externa.</summary>
public interface IFonteLimitesMunicipais
{
    /// <summary>Retorna GeoJSON de geometria por código IBGE, ou null se a fonte estiver indisponível.</summary>
    Task<IReadOnlyDictionary<string, string>?> ObterAsync(CancellationToken ct);
    string NomeFonte { get; }
}
