using System.Reflection;
using System.Text.Json;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Testes.Unitarios;

public class GeoMunicipiosTestes
{
    private static readonly IdentificadorMunicipio Identificador = CarregarLimitesApac();

    /// <summary>Usa a cópia local da camada oficial da APAC embarcada na Infraestrutura (lida como arquivo).</summary>
    private static IdentificadorMunicipio CarregarLimitesApac()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src"))) dir = Directory.GetParent(dir)?.FullName;
        var caminho = Path.Combine(dir!, "src", "AlertaEmAcao.Infraestrutura", "Dados", "limites-rmr-apac.geojson");
        using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
        var limites = doc.RootElement.GetProperty("features").EnumerateArray().Select(f => (
            f.GetProperty("properties").GetProperty("codigoIbge").GetString()!,
            f.GetProperty("properties").GetProperty("nome").GetString()!,
            LeitorGeoJson.LerGeometria(f.GetProperty("geometry"))));
        return new IdentificadorMunicipio(limites);
    }

    [Theory]
    [InlineData(-8.0631, -34.8711, "2611606")] // Marco Zero, Recife
    [InlineData(-8.1180, -34.9010, "2611606")] // Boa Viagem, Recife
    [InlineData(-8.0145, -34.8556, "2609600")] // Alto da Sé, Olinda
    [InlineData(-8.1730, -34.9180, "2607901")] // Piedade, Jaboatão
    [InlineData(-8.5030, -35.0050, "2607208")] // Porto de Galinhas, Ipojuca
    [InlineData(-8.0230, -34.9810, "2603454")] // Camaragibe
    [InlineData(-7.8110, -34.8410, "2607604")] // Ilha de Itamaracá
    [InlineData(-7.9400, -34.8730, "2610707")] // Paulista
    [InlineData(-8.2850, -35.0350, "2602902")] // Cabo de Santo Agostinho
    [InlineData(-8.1180, -35.0900, "2609402")] // Moreno
    public void Identifica_municipio_correto_pelas_coordenadas(double lat, double lon, string esperado)
    {
        var m = Identificador.Identificar(new PontoGeo(lat, lon));
        Assert.NotNull(m);
        Assert.Equal(esperado, m!.Codigo);
    }

    [Theory]
    [InlineData(-8.2836, -35.9761)] // Caruaru (fora da RMR)
    [InlineData(-9.6658, -35.7353)] // Maceió
    [InlineData(-8.0500, -34.5000)] // Oceano Atlântico, longe da costa
    public void Ponto_fora_da_RMR_retorna_nulo(double lat, double lon) =>
        Assert.Null(Identificador.Identificar(new PontoGeo(lat, lon)));

    [Fact]
    public void Os_14_municipios_tem_codigos_unicos_de_7_digitos()
    {
        Assert.Equal(14, MunicipiosRmr.Todos.Count);
        Assert.Equal(14, MunicipiosRmr.Codigos.Count);
        Assert.All(MunicipiosRmr.Todos, m => Assert.Matches(@"^26\d{5}$", m.Codigo));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public void Coordenada_invalida_e_rejeitada(double lat, double lon) =>
        Assert.Throws<ErroDominio>(() => new PontoGeo(lat, lon));

    [Fact]
    public void Ponto_dentro_e_fora_de_poligono_com_buraco()
    {
        var anel = new List<double[]> { new[] { 0d, 0d }, new[] { 10d, 0d }, new[] { 10d, 10d }, new[] { 0d, 10d }, new[] { 0d, 0d } };
        var buraco = new List<double[]> { new[] { 4d, 4d }, new[] { 6d, 4d }, new[] { 6d, 6d }, new[] { 4d, 6d }, new[] { 4d, 4d } };
        var p = MultiPoligonoGeo.DePoligono([anel, buraco]);
        Assert.True(p.Contem(new PontoGeo(2, 2)));
        Assert.False(p.Contem(new PontoGeo(5, 5))); // no buraco
        Assert.False(p.Contem(new PontoGeo(11, 5)));
    }

    [Fact]
    public void Distancia_haversine_recife_olinda_aproximadamente_6km()
    {
        var d = new PontoGeo(-8.0631, -34.8711).DistanciaMetros(new PontoGeo(-8.0145, -34.8556));
        Assert.InRange(d, 5000, 6500);
    }

    [Fact]
    public void Aproximacao_nao_revela_coordenada_precisa()
    {
        var p = new PontoGeo(-8.126234, -34.947812);
        var a = p.Aproximar();
        Assert.NotEqual(p.Latitude, a.Latitude);
        Assert.True(p.DistanciaMetros(a) < 600);
    }

    [Fact]
    public void Geometria_invalida_lanca_erro()
    {
        Assert.Throws<ErroDominio>(() => LeitorGeoJson.LerGeometria("""{"type":"Point","coordinates":[0,0]}"""));
        Assert.Throws<ErroDominio>(() => LeitorGeoJson.LerGeometria("""{"type":"Polygon","coordinates":[[[0,0],[1,1]]]}"""));
    }
}
