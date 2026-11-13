using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.Contratos;
using AlertaEmAcao.Modulos.Encaminhamento;
using Microsoft.Extensions.Options;

namespace AlertaEmAcao.Infraestrutura.Adaptadores;

public sealed class OpcoesIntegracaoCentrais
{
    /// <summary>URL base onde as centrais simuladas estão hospedadas (pode ser outro serviço).</summary>
    public string UrlBaseCentrais { get; set; } = "http://localhost:8080";
    public string SegredoMestre { get; set; } = "";
}

/// <summary>
/// Envia mensagens às centrais por HTTP real. Só considera "recebido" quando a central devolve 2xx
/// com protocolo no corpo. 5xx/timeout/rede → falha temporária (retentativa); 4xx → rejeição.
/// </summary>
public sealed class ClienteCentralHttp(HttpClient http, IOptions<OpcoesIntegracaoCentrais> opcoes) : IClienteCentral
{
    private static readonly JsonSerializerOptions Js = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoEnvioCentral> EnviarAsync(Instituicao destino, object mensagem, string chaveIdempotencia, CancellationToken ct)
    {
        if (destino.Canal != CanalIntegracao.HttpCentralSimulada)
            return new(false, false, null, null, null, false, "Canal institucional homologado não disponível: requer autorização formal.", 0);

        var url = destino.EndpointUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? destino.EndpointUrl
            : opcoes.Value.UrlBaseCentrais.TrimEnd('/') + destino.EndpointUrl;
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(destino.TimeoutSegundos));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(mensagem, mensagem.GetType(), options: Js) };
            req.Headers.Add(CabecalhosIntegracao.ChaveCentral, ChavesCentrais.Derivar(opcoes.Value.SegredoMestre, destino.Codigo));
            req.Headers.Add(CabecalhosIntegracao.ChaveIdempotencia, chaveIdempotencia);
            req.Headers.Add(CabecalhosIntegracao.Correlacao, chaveIdempotencia);
            using var resp = await http.SendAsync(req, cts.Token);
            var corpo = await resp.Content.ReadAsStringAsync(cts.Token);
            var ms = sw.ElapsedMilliseconds;
            var status = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode)
            {
                RespostaRecebimentoV1? r = null;
                try { r = JsonSerializer.Deserialize<RespostaRecebimentoV1>(corpo, Js); } catch (JsonException) { }
                if (r is null || string.IsNullOrWhiteSpace(r.Protocolo))
                    return new(false, true, status, null, null, false, "Resposta 2xx sem protocolo: não considerada confirmação.", ms);
                return new(true, false, status, r.Protocolo, r.RecebidoEm, r.Duplicado, null, ms);
            }
            var temporaria = status >= 500 || resp.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
            return new(false, temporaria, status, null, null, false, $"HTTP {status}: {Resumir(corpo)}", ms);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, true, null, null, null, false, $"Timeout após {destino.TimeoutSegundos}s sem resposta da central.", sw.ElapsedMilliseconds);
        }
        catch (HttpRequestException e)
        {
            return new(false, true, null, null, null, false, $"Falha de rede: {e.Message}", sw.ElapsedMilliseconds);
        }
    }

    private static string Resumir(string corpo) => corpo.Length > 300 ? corpo[..300] + "…" : corpo;
}
