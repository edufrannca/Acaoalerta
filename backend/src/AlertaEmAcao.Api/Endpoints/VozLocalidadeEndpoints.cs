using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record PedidoVoz(string Texto, double? Velocidade);

/// <summary>
/// Voz de reserva (servidor): sintetiza no PRÓPRIO servidor com espeak-ng (software livre, sem serviço externo nem custo).
/// Usada quando a síntese local do aparelho falha (ex.: iPhone sem voz pt-BR ou com fala bloqueada). Só recebe textos públicos
/// exibidos na tela (avisos, situação da região, orientações) — nunca nome, telefone ou identificadores.
/// Localidade: geocodificação reversa no OpenStreetMap (Nominatim), com coordenadas arredondadas (~110 m) e cache, validada
/// contra o município identificado pelos limites oficiais da APAC. Sem confirmação, não exibimos bairro.
/// </summary>
public static class VozLocalidadeEndpoints
{
    private static readonly ConcurrentDictionary<string, byte[]> CacheVoz = new();
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Em, object Resultado)> CacheLocal = new();

    public static string? CaminhoEspeak() =>
        new[] { "/usr/bin/espeak-ng", "/usr/local/bin/espeak-ng", "/opt/homebrew/bin/espeak-ng" }.FirstOrDefault(File.Exists);

    public static void MapVozLocalidade(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Voz e localidade");

        api.MapGet("/voz/estado", () => Results.Ok(new { disponivel = CaminhoEspeak() is not null, motor = "espeak-ng (pt-BR), no servidor do Alerta em Ação" }));

        api.MapPost("/voz", async (PedidoVoz p, CancellationToken ct) =>
        {
            // Entrada inválida é 400 em qualquer ambiente; só depois verifica se o sintetizador existe.
            var texto = (p.Texto ?? "").Trim();
            if (texto.Length is 0 or > 1500) return Results.BadRequest(new { codigo = "texto_invalido", erro = "Texto vazio ou maior que 1500 caracteres." });
            var exe = CaminhoEspeak();
            if (exe is null) return Results.Problem("Voz do servidor indisponível neste ambiente.", statusCode: 503);
            var palavrasMin = (int)Math.Clamp(Math.Round(165 * (p.Velocidade ?? 1)), 90, 260);
            var chave = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{palavrasMin}|{texto}")));
            if (!CacheVoz.TryGetValue(chave, out var wav))
            {
                var psi = new ProcessStartInfo(exe) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in new[] { "-v", "pt-br", "-s", palavrasMin.ToString(CultureInfo.InvariantCulture), "--stdout" }) psi.ArgumentList.Add(a);
                using var proc = Process.Start(psi)!;
                await proc.StandardInput.WriteAsync(texto.AsMemory(), ct); // texto via stdin: sem interpretação por shell
                proc.StandardInput.Close();
                using var ms = new MemoryStream();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                await proc.StandardOutput.BaseStream.CopyToAsync(ms, cts.Token);
                await proc.WaitForExitAsync(cts.Token);
                if (proc.ExitCode != 0 || ms.Length < 100) return Results.Problem("Falha ao sintetizar a voz no servidor.", statusCode: 502);
                wav = ms.ToArray();
                if (CacheVoz.Count > 300) CacheVoz.Clear();
                CacheVoz[chave] = wav;
            }
            return Results.File(wav, "audio/wav");
        }).RequireRateLimiting("voz");

        api.MapGet("/territorio/localidade", async (double lat, double lon, ICatalogoMunicipios catalogo, IHttpClientFactory http, IRelogio relogio, CancellationToken ct) =>
        {
            if (!PontoGeo.TentarCriar(lat, lon, out var ponto)) return Results.BadRequest(new { erro = "Coordenadas inválidas." });
            var mun = catalogo.Identificar(ponto);
            object Base(string? localidade, string precisao, string? fonteLocalidade, string? obs) => new
            {
                localidade, precisao,
                municipio = mun is null ? null : new { codigo = mun.Codigo, nome = mun.Nome },
                uf = mun is null ? null : "PE",
                foraDaRmr = mun is null,
                fonteMunicipio = "Limites municipais oficiais (APAC)",
                fonteLocalidade, observacao = obs,
            };
            if (mun is null) return Results.Ok(Base(null, "coordenadas", null, "Fora dos 14 municípios da RMR."));

            var chave = $"{Math.Round(lat, 3).ToString(CultureInfo.InvariantCulture)},{Math.Round(lon, 3).ToString(CultureInfo.InvariantCulture)}";
            if (CacheLocal.TryGetValue(chave, out var c) && relogio.Agora - c.Em < TimeSpan.FromHours(24)) return Results.Ok(c.Resultado);

            object resultado;
            try
            {
                await AlertaEmAcao.Infraestrutura.Adaptadores.LimitadorNominatim.Trava.WaitAsync(ct); // política do Nominatim: no máximo 1 requisição por segundo
                try
                {
                    var espera = TimeSpan.FromSeconds(1.1) - (DateTimeOffset.UtcNow - AlertaEmAcao.Infraestrutura.Adaptadores.LimitadorNominatim.Ultima);
                    if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);
                    AlertaEmAcao.Infraestrutura.Adaptadores.LimitadorNominatim.Ultima = DateTimeOffset.UtcNow;
                    var url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=16&addressdetails=1&accept-language=pt-BR&lat={Math.Round(lat, 3).ToString(CultureInfo.InvariantCulture)}&lon={Math.Round(lon, 3).ToString(CultureInfo.InvariantCulture)}";
                    using var doc = JsonDocument.Parse(await http.CreateClient("nominatim").GetStringAsync(url, ct));
                    var end = doc.RootElement.TryGetProperty("address", out var a) ? a : default;
                    string? Campo(params string[] ks) => end.ValueKind != JsonValueKind.Object ? null
                        : ks.Select(k => end.TryGetProperty(k, out var v) ? v.GetString() : null).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                    var cidade = Campo("city", "town", "municipality", "village");
                    var bairro = Campo("suburb", "neighbourhood", "quarter", "city_district");
                    // Só aceita o bairro se o município do OSM coincidir com o município oficial do ponto.
                    var confere = cidade is not null && Normalizar(cidade) == Normalizar(mun.Nome);
                    resultado = confere && bairro is not null && Normalizar(bairro) != Normalizar(mun.Nome)
                        ? Base(bairro, "bairro", "OpenStreetMap (Nominatim) — © colaboradores do OpenStreetMap, ODbL", null)
                        : Base(null, "municipio", null, "Bairro não identificado com segurança; exibindo apenas o município.");
                }
                finally { AlertaEmAcao.Infraestrutura.Adaptadores.LimitadorNominatim.Trava.Release(); }
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Results.Ok(Base(null, "municipio", null, "Serviço de bairros indisponível agora; exibindo apenas o município."));
            }
            if (CacheLocal.Count > 5000) CacheLocal.Clear();
            CacheLocal[chave] = (relogio.Agora, resultado);
            return Results.Ok(resultado);
        });
    }

    public static string Normalizar(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(d.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
