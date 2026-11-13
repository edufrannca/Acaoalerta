using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.Modulos.Localizacao;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Infraestrutura.Adaptadores;

/// <summary>
/// Política pública do Nominatim (OSM): no máximo 1 requisição por segundo para toda a aplicação,
/// User-Agent identificado, cache, atribuição e nada de autocompletar. Compartilhado por todas as chamadas.
/// </summary>
public static class LimitadorNominatim
{
    /// <summary>Trava e horário da última chamada, compartilhados também pela consulta reversa de localidade.</summary>
    public static readonly SemaphoreSlim Trava = new(1, 1);
    public static DateTimeOffset Ultima { get; set; } = DateTimeOffset.MinValue;

    public static async Task<T> ExecutarAsync<T>(Func<Task<T>> chamada, CancellationToken ct)
    {
        await Trava.WaitAsync(ct);
        try
        {
            var espera = Ultima + TimeSpan.FromMilliseconds(1100) - DateTimeOffset.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);
            try { return await chamada(); }
            finally { Ultima = DateTimeOffset.UtcNow; }
        }
        finally { Trava.Release(); }
    }
}

/// <summary>CEP pelo ViaCEP (sem coordenadas) e endereço pelo Nominatim (OpenStreetMap). Coordenadas nunca são inventadas.</summary>
public sealed class GeocodificadorPublico(IHttpClientFactory http, IMemoryCache cache, ILogger<GeocodificadorPublico> log) : IGeocodificador
{
    // Caixa da RMR (com folga) para restringir a busca.
    private const string CaixaRmr = "-35.35,-7.45,-34.75,-8.65";
    private static readonly Dictionary<string, string> Estados = new() { ["PE"] = "Pernambuco" };

    public async Task<EnderecoCep?> BuscarCepAsync(string cep, CancellationToken ct)
    {
        cep = EnderecoLocal.SoDigitos(cep);
        if (cep.Length != 8) return null;
        return await cache.GetOrCreateAsync($"viacep:{cep}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            using var resp = await http.CreateClient("viacep").GetAsync($"https://viacep.com.br/ws/{cep}/json/", ct);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"ViaCEP respondeu {(int)resp.StatusCode}");
            var j = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (j.TryGetProperty("erro", out _)) return null;
            string? S(string k) => j.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } t ? t : null;
            return S("localidade") is { } mun && S("uf") is { } uf ? new EnderecoCep(cep, S("logradouro"), S("bairro"), mun, uf) : null;
        });
    }

    public async Task<IReadOnlyList<ResultadoGeocodificacao>> BuscarEnderecoAsync(string? logradouro, string? numero, string? bairro, string municipio, string uf,
        string? cep, CancellationToken ct)
    {
        var estado = Estados.GetValueOrDefault(uf.ToUpperInvariant()) ?? uf;
        var rua = string.Join(' ', new[] { numero, logradouro }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        var chave = $"nominatim:{rua}|{bairro}|{municipio}|{uf}|{cep}".ToLowerInvariant();
        return await cache.GetOrCreateAsync(chave, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
            var resultados = await BuscarAsync(rua, municipio, estado, cep, ct);
            // Sem correspondência com o CEP, tenta sem ele (o OSM tem poucos CEPs no Brasil).
            if (resultados.Count == 0 && cep is not null) resultados = await BuscarAsync(rua, municipio, estado, null, ct);
            // Com bairro, ainda tenta pelo bairro (posição de bairro exige confirmação no mapa).
            if (resultados.Count == 0 && !string.IsNullOrWhiteSpace(bairro)) resultados = await BuscarLivreAsync($"{bairro}, {municipio}, {estado}", ct);
            return resultados;
        }) ?? [];
    }

    private Task<List<ResultadoGeocodificacao>> BuscarAsync(string rua, string municipio, string estado, string? cep, CancellationToken ct)
    {
        var q = new List<string> { $"city={Uri.EscapeDataString(municipio)}", $"state={Uri.EscapeDataString(estado)}" };
        if (rua.Length > 0) q.Add($"street={Uri.EscapeDataString(rua)}");
        if (cep is not null) q.Add($"postalcode={cep[..5]}-{cep[5..]}");
        return ConsultarAsync(string.Join('&', q), ct);
    }

    private Task<List<ResultadoGeocodificacao>> BuscarLivreAsync(string texto, CancellationToken ct) => ConsultarAsync($"q={Uri.EscapeDataString(texto)}", ct);

    private async Task<List<ResultadoGeocodificacao>> ConsultarAsync(string consulta, CancellationToken ct)
    {
        var url = $"https://nominatim.openstreetmap.org/search?{consulta}&countrycodes=br&format=jsonv2&addressdetails=1&limit=5&viewbox={CaixaRmr}&bounded=1&accept-language=pt-BR";
        try
        {
            var arr = await LimitadorNominatim.ExecutarAsync(async () =>
            {
                using var resp = await http.CreateClient("nominatim").GetAsync(url, ct);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            }, ct);
            var l = new List<ResultadoGeocodificacao>();
            foreach (var r in arr.EnumerateArray())
            {
                if (!double.TryParse(r.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(r.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;
                var tipo = r.TryGetProperty("addresstype", out var at) ? at.GetString() : r.TryGetProperty("type", out var t) ? t.GetString() : null;
                var (precisao, confirmar) = tipo switch
                {
                    "house" or "building" or "amenity" or "shop" => ("Número/imóvel encontrado no OpenStreetMap", false),
                    "road" or "highway" or "street" => ("Só a rua: confirme o ponto exato no mapa", true),
                    "suburb" or "neighbourhood" or "quarter" or "city_district" => ("Só o bairro: escolha o ponto no mapa", true),
                    _ => ("Localização aproximada: confirme no mapa", true),
                };
                l.Add(new ResultadoGeocodificacao(lat, lon, r.GetProperty("display_name").GetString() ?? "", precisao, confirmar));
            }
            return l;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning("Nominatim indisponível: {Erro}", e.Message);
            throw new HttpRequestException("Serviço de endereços (OpenStreetMap) indisponível agora. Escolha o ponto no mapa.", e);
        }
    }
}
