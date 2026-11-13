using System.Globalization;
using System.Reflection;
using System.Text.Json;
using AlertaEmAcao.Modulos.Abrigos;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Municipios;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Infraestrutura.Adaptadores;

/// <summary>Endereços das fontes públicas. Conferidos em 12/11/2026.</summary>
public static class UrlsFontes
{
    public const string ApacBase = "https://geoportal.apac.pe.gov.br/server/rest/services/met_monitoramento_chuvas_pe/MapServer";
    public const string RmrEnvelope = "-35.30,-8.65,-34.78,-7.62";
    public static string ApacChuva => $"{ApacBase}/4/query?where=1%3D1&geometry={RmrEnvelope}&geometryType=esriGeometryEnvelope&inSR=4674&spatialRel=esriSpatialRelIntersects&outFields=*&returnGeometry=false&f=json";
    public static string ApacLimites => $"{ApacBase}/7/query?where=cd_mun+IN+({string.Join(",", MunicipiosRmr.Codigos.Select(c => $"%27{c}%27"))})&outFields=cd_mun,nm_mun&returnGeometry=true&maxAllowableOffset=0.0005&geometryPrecision=5&outSR=4326&f=geojson";
    public const string RecifeAreasRisco = "https://dados.recife.pe.gov.br/api/3/action/datastore_search?resource_id=75344435-aca8-4aef-ab2c-9521d6e5ff18&limit=1000";
    public const string RecifeAreasRiscoPagina = "https://dados.recife.pe.gov.br/dataset/defesa-civil";
    public const string CopAbrigos = "https://esigportal2.recife.pe.gov.br/arcgis/rest/services/COP/Abrigos_Ativos/FeatureServer/0/query?where=1%3D1&outFields=objectid,no_entidade,tipo,endereco,latitude,longitude,status,estagio_operacional,in_acessibilidade_inexistente,last_edited_date&returnGeometry=false&f=json";
}

public abstract class FonteHttpBase(HttpClient http, IRegistroFontes registro, IRelogio relogio, ILogger log)
{
    protected IRelogio Relogio => relogio;

    protected async Task<(JsonDocument? Doc, string? Erro)> ObterJsonAsync(string nomeFonte, string url, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(12));
        try
        {
            using var resp = await http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode)
                return (null, Falhar(nomeFonte, url, $"HTTP {(int)resp.StatusCode}"));
            var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var err))
                return (null, Falhar(nomeFonte, url, $"Erro da fonte: {err}"));
            return (doc, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return (null, Falhar(nomeFonte, url, $"{e.GetType().Name}: {e.Message}"));
        }
    }

    protected string Falhar(string nome, string url, string erro)
    {
        log.LogWarning("Fonte {Fonte} indisponível: {Erro}", nome, erro);
        registro.Registrar(nome, false, erro, null, url, relogio.Agora);
        return erro;
    }

    protected void Sucesso(string nome, string url, int registros) => registro.Registrar(nome, true, null, registros, url, relogio.Agora);

    protected static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    protected static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

/// <summary>Chuva acumulada por estação (APAC/SIRH). Cache de 5 minutos; mantém último resultado bom se a fonte cair.</summary>
public sealed class FonteChuvaApac(HttpClient http, IRegistroFontes registro, IRelogio relogio, ILogger<FonteChuvaApac> log)
    : FonteHttpBase(http, registro, relogio, log), IFonteChuva
{
    private ResultadoFonteChuva? _ultimo;
    private readonly SemaphoreSlim _trava = new(1, 1);
    public static readonly TimeSpan Cache = TimeSpan.FromMinutes(5);

    public async Task<ResultadoFonteChuva> ObterRmrAsync(CancellationToken ct)
    {
        if (_ultimo is { Disponivel: true } u && Relogio.Agora - u.ConsultadoEm < Cache) return u;
        await _trava.WaitAsync(ct);
        try
        {
            if (_ultimo is { Disponivel: true } u2 && Relogio.Agora - u2.ConsultadoEm < Cache) return u2;
            var (doc, erro) = await ObterJsonAsync(NomesFontes.ApacChuva, UrlsFontes.ApacChuva, ct);
            if (doc is null)
            {
                // Mantém leituras anteriores, marcando a fonte como indisponível agora.
                _ultimo = new ResultadoFonteChuva(_ultimo?.Leituras ?? [], _ultimo?.ConsultadoEm ?? Relogio.Agora, false, erro, NomesFontes.ApacChuva, UrlsFontes.ApacChuva);
                return _ultimo;
            }
            using (doc)
            {
                var leituras = Interpretar(doc.RootElement);
                Sucesso(NomesFontes.ApacChuva, UrlsFontes.ApacChuva, leituras.Count);
                _ultimo = new ResultadoFonteChuva(leituras, Relogio.Agora, true, null, NomesFontes.ApacChuva, UrlsFontes.ApacChuva);
                return _ultimo;
            }
        }
        finally { _trava.Release(); }
    }

    public static List<LeituraChuva> Interpretar(JsonElement raiz)
    {
        var lista = new List<LeituraChuva>();
        if (!raiz.TryGetProperty("features", out var fs)) return lista;
        foreach (var f in fs.EnumerateArray())
        {
            var a = f.TryGetProperty("attributes", out var at) ? at : f.GetProperty("properties");
            var cod = Str(a, "cod_municipio");
            if (!MunicipiosRmr.Pertence(cod)) continue;
            var lat = Num(a, "latitude"); var lon = Num(a, "longitude");
            if (lat is null || lon is null) continue;
            lista.Add(new LeituraChuva(
                Str(a, "codigo_gmmc") ?? Num(a, "objectid")?.ToString(CultureInfo.InvariantCulture) ?? Guid.NewGuid().ToString(),
                Str(a, "nome") ?? "Estação", lat.Value, lon.Value, Str(a, "municipio"), cod,
                InterpretadorApac.DataHora(Str(a, "ultima_leitura_data_hora")),
                InterpretadorApac.Milimetros(Num(a, "hora_1")), InterpretadorApac.Milimetros(Num(a, "horas_3")),
                InterpretadorApac.Milimetros(Num(a, "horas_6")), InterpretadorApac.Milimetros(Num(a, "horas_12")),
                InterpretadorApac.Milimetros(Num(a, "horas_24")), InterpretadorApac.Milimetros(Num(a, "horas_48")),
                InterpretadorApac.Milimetros(Num(a, "horas_72")), "APAC/SIRH"));
        }
        return lista;
    }
}

/// <summary>Limites municipais oficiais (APAC, camada 7). Fallback: cópia local embarcada da mesma camada.</summary>
public sealed class FonteLimitesApac(HttpClient http, IRegistroFontes registro, IRelogio relogio, ILogger<FonteLimitesApac> log)
    : FonteHttpBase(http, registro, relogio, log), IFonteLimitesMunicipais
{
    public string NomeFonte => NomesFontes.ApacLimites;

    public async Task<IReadOnlyDictionary<string, string>?> ObterAsync(CancellationToken ct)
    {
        var (doc, _) = await ObterJsonAsync(NomeFonte, UrlsFontes.ApacLimites, ct, TimeSpan.FromSeconds(20));
        if (doc is null) return null;
        using (doc)
        {
            var r = LerFeatureCollection(doc.RootElement, "cd_mun");
            if (r.Count != 14) { Falhar(NomeFonte, UrlsFontes.ApacLimites, $"Esperados 14 municípios, recebidos {r.Count}."); return null; }
            Sucesso(NomeFonte, UrlsFontes.ApacLimites, r.Count);
            return r;
        }
    }

    public static Dictionary<string, string> LerFeatureCollection(JsonElement fc, string campoCodigo)
    {
        var r = new Dictionary<string, string>();
        foreach (var f in fc.GetProperty("features").EnumerateArray())
        {
            var cod = Str(f.GetProperty("properties"), campoCodigo);
            if (!MunicipiosRmr.Pertence(cod)) continue;
            var geom = f.GetProperty("geometry");
            LeitorGeoJson.LerGeometria(geom); // valida
            r[cod!] = geom.GetRawText();
        }
        return r;
    }

    public static Dictionary<string, string> CarregarCopiaLocal()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("AlertaEmAcao.Infraestrutura.Dados.limites-rmr-apac.geojson")
                      ?? throw new InvalidOperationException("Recurso de limites municipais não encontrado.");
        using var doc = JsonDocument.Parse(s);
        return LerFeatureCollection(doc.RootElement, "codigoIbge");
    }
}

public sealed class FonteAvisosInmet(HttpClient http, IRegistroFontes registro, IRelogio relogio, ILogger<FonteAvisosInmet> log)
    : FonteHttpBase(http, registro, relogio, log), IFonteAvisosOficiais
{
    public async Task<(List<Aviso> Avisos, bool Disponivel, string? Erro)> ObterAsync(CancellationToken ct)
    {
        var (doc, erro) = await ObterJsonAsync(NomesFontes.InmetAvisos, ParserAvisosInmet.Url, ct, TimeSpan.FromSeconds(20));
        if (doc is null) return ([], false, erro);
        using (doc)
        {
            var avisos = ParserAvisosInmet.Interpretar(doc.RootElement.GetRawText(), Relogio.Agora);
            return (avisos, true, null);
        }
    }
}

public sealed class FonteAreasRiscoRecife(HttpClient http, IRegistroFontes registro, IRelogio relogio, ILogger<FonteAreasRiscoRecife> log)
    : FonteHttpBase(http, registro, relogio, log), IFonteAreasRisco
{
    public async Task<(List<AreaRiscoDocumentada> Areas, bool Disponivel, string? Erro)> ObterAsync(CancellationToken ct)
    {
        var (doc, erro) = await ObterJsonAsync(NomesFontes.RecifeAreasRisco, UrlsFontes.RecifeAreasRisco, ct, TimeSpan.FromSeconds(25));
        if (doc is null) return ([], false, erro);
        using (doc)
        {
            var lista = Interpretar(doc.RootElement, Relogio.Agora);
            Sucesso(NomesFontes.RecifeAreasRisco, UrlsFontes.RecifeAreasRisco, lista.Count);
            return (lista, true, null);
        }
    }

    public static List<AreaRiscoDocumentada> Interpretar(JsonElement raiz, DateTimeOffset agora)
    {
        var lista = new List<AreaRiscoDocumentada>();
        if (!raiz.TryGetProperty("result", out var res) || !res.TryGetProperty("records", out var recs)) return lista;
        foreach (var r in recs.EnumerateArray())
        {
            double? lat = Num(r, "latitude") ?? (double.TryParse(Str(r, "latitude"), CultureInfo.InvariantCulture, out var la) ? la : null);
            double? lon = Num(r, "longitude") ?? (double.TryParse(Str(r, "longitude"), CultureInfo.InvariantCulture, out var lo) ? lo : null);
            if (!PontoGeo.TentarCriar(lat, lon, out var p)) continue;
            var id = r.TryGetProperty("_id", out var idv) ? idv.GetRawText() : Guid.NewGuid().ToString();
            lista.Add(AreaRiscoDocumentada.Criar("Defesa Civil do Recife (SEDEC) — Dados Abertos Recife", UrlsFontes.RecifeAreasRiscoPagina,
                $"regional-sul-{id}", Str(r, "descricao") ?? "Área de risco", Str(r, "bairro"), Str(r, "localidade"), p, "2611606", "ODbL", agora));
        }
        return lista;
    }
}

public sealed class FonteAbrigosCopRecife(HttpClient http, IRegistroFontes registro, IRelogio relogio, ILogger<FonteAbrigosCopRecife> log)
    : FonteHttpBase(http, registro, relogio, log), IFonteAbrigos
{
    public async Task<(List<Abrigo> Abrigos, bool Disponivel, string? Erro)> ObterAsync(CancellationToken ct)
    {
        var (doc, erro) = await ObterJsonAsync(NomesFontes.CopRecifeAbrigos, UrlsFontes.CopAbrigos, ct, TimeSpan.FromSeconds(20));
        if (doc is null) return ([], false, erro);
        using (doc)
        {
            var lista = Interpretar(doc.RootElement, Relogio.Agora);
            Sucesso(NomesFontes.CopRecifeAbrigos, UrlsFontes.CopAbrigos, lista.Count);
            return (lista, true, null);
        }
    }

    public static List<Abrigo> Interpretar(JsonElement raiz, DateTimeOffset agora)
    {
        var lista = new List<Abrigo>();
        if (!raiz.TryGetProperty("features", out var fs)) return lista;
        foreach (var f in fs.EnumerateArray())
        {
            var a = f.GetProperty("attributes");
            if (!PontoGeo.TentarCriar(Num(a, "latitude"), Num(a, "longitude"), out var p)) continue;
            var acess = a.TryGetProperty("in_acessibilidade_inexistente", out var ai) && ai.ValueKind == JsonValueKind.Number
                ? (ai.GetInt32() == 1 ? Acessibilidade.Inexistente : Acessibilidade.Possui) : Acessibilidade.NaoInformada;
            DateTimeOffset? editado = Num(a, "last_edited_date") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : null;
            // Campos de coordenadores/contatos pessoais da base NÃO são importados (minimização de dados).
            lista.Add(Abrigo.Criar($"cop-{Num(a, "objectid")}", Str(a, "no_entidade") ?? "Abrigo", Str(a, "tipo"), Str(a, "endereco"), "2611606", p, acess,
                "COP Recife — Abrigos (ArcGIS)", "https://esigportal2.recife.pe.gov.br/arcgis/rest/services/COP/Abrigos_Ativos/FeatureServer/0",
                Str(a, "status"), Str(a, "estagio_operacional"), editado, agora, ficticio: false));
        }
        return lista;
    }
}
