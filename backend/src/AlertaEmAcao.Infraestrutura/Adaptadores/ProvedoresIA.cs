using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using Microsoft.Extensions.Options;

namespace AlertaEmAcao.Infraestrutura.Adaptadores;

public sealed class OpcoesIA
{
    /// <summary>"anthropic", "openai" (qualquer API compatível com /chat/completions) ou vazio (desativado).</summary>
    public string Provedor { get; set; } = "";
    public string? ChaveApi { get; set; }
    public string? Modelo { get; set; }
    public string? UrlBase { get; set; }
}

public static class PromptAnalise
{
    public const string Sistema = """
        Você apoia operadores de Defesa Civil na leitura de pedidos de socorro na Região Metropolitana do Recife.
        Regras obrigatórias:
        - Use SOMENTE as informações fornecidas. Não invente fatos, números, nomes ou endereços.
        - Cada item de "informacoesExtraidas", "possiveisPerigos" e "sinalizacoesParaAvaliacaoHumana" deve ter "evidencia":
          um trecho COPIADO LITERALMENTE da entrada (relato ou linhas de dados). Sem trecho literal, não inclua o item.
        - Você NÃO decide prioridade de atendimento nem ordena vidas. Não use idade como critério isolado.
        - Não dê ordens de evacuação. Sinalizações são para avaliação humana.
        - Liste o que está ausente ou não verificado. Preserve incertezas: use "não informado" em vez de supor.
        - Em "pessoasIdentificadas", registre só pessoas e idades citadas na entrada; condições (ferida, mobilidade reduzida,
          depende de equipamento) descrevem necessidade de assistência, nunca prioridade.
        - "explicacao" de cada sinalização deve justificar com base na evidência citada.
        - Responda apenas com um objeto JSON válido, sem texto adicional, no esquema:
        {"resumo": string (até 600 caracteres),
         "informacoesExtraidas": [{"descricao": string, "evidencia": string}],
         "pessoasEnvolvidas": number|null,
         "pessoasIdentificadas": [{"idade": number|null, "condicoes": [string], "evidencia": string}],
         "possiveisPerigos": [{"tipo": string, "descricao": string, "evidencia": string}],
         "necessidadesAssistencia": [string],
         "indiciosAgravamento": [string],
         "informacoesAusentes": [string],
         "informacoesNaoVerificadas": [string],
         "sinalizacoesParaAvaliacaoHumana": [{"codigo": one of AGUA_SUBINDO|ILHAMENTO|CORRENTEZA|RISCO_GEOLOGICO|DESABAMENTO|PESSOA_FERIDA|NECESSIDADE_MEDICA|NECESSIDADE_ASSISTENCIA|PESSOA_ISOLADA|ACESSO_BLOQUEADO|SEGURANCA_PUBLICA|LOCALIZACAO_INCERTA|INFORMACOES_CONTRADITORIAS|AGRAVAMENTO|OUTRO, "explicacao": string, "evidencia": string}]}
        """;

    public static string Usuario(EntradaAnaliseIA e) =>
        "ENTRADA (dados do pedido; cite trechos literais destas linhas como evidência):\n" + ValidadorSaidaIA.MontarCorpus(e) +
        $"\n\nHorário do pedido: {e.HorarioOcorrencia}\nÚltima localização: {e.UltimaAtualizacaoLocalizacao ?? "não informada"}";
}

public sealed class ProvedorIANulo : IProvedorIA
{
    public bool Configurado => false;
    public string Descricao => "Nenhum provedor configurado — somente regras determinísticas.";
    public Task<RespostaBrutaIA> AnalisarAsync(EntradaAnaliseIA entrada, CancellationToken ct) =>
        throw new InvalidOperationException("IA não configurada.");
}

/// <summary>Anthropic Messages API (https://api.anthropic.com/v1/messages).</summary>
public sealed class ProvedorIAAnthropic(HttpClient http, IOptions<OpcoesIA> op) : IProvedorIA
{
    public bool Configurado => !string.IsNullOrWhiteSpace(op.Value.ChaveApi);
    private string Modelo => string.IsNullOrWhiteSpace(op.Value.Modelo) ? "claude-haiku-5-5" : op.Value.Modelo;
    public string Descricao => $"Anthropic ({Modelo})";

    public async Task<RespostaBrutaIA> AnalisarAsync(EntradaAnaliseIA entrada, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var req = new HttpRequestMessage(HttpMethod.Post, (string.IsNullOrWhiteSpace(op.Value.UrlBase) ? "https://api.anthropic.com" : op.Value.UrlBase).TrimEnd('/') + "/v1/messages")
        {
            Content = JsonContent.Create(new
            {
                model = Modelo,
                max_tokens = 1500,
                temperature = 0,
                system = PromptAnalise.Sistema,
                messages = new[] { new { role = "user", content = PromptAnalise.Usuario(entrada) } },
            }),
        };
        req.Headers.Add("x-api-key", op.Value.ChaveApi);
        req.Headers.Add("anthropic-version", "2023-06-01");
        using var resp = await http.SendAsync(req, ct);
        var corpo = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Anthropic HTTP {(int)resp.StatusCode}: {corpo[..Math.Min(200, corpo.Length)]}");
        var texto = JsonNode.Parse(corpo)?["content"]?.AsArray().Select(c => c?["text"]?.GetValue<string>()).FirstOrDefault(t => t is not null)
                    ?? throw new InvalidOperationException("Resposta sem conteúdo de texto.");
        return new RespostaBrutaIA(texto, Modelo, "anthropic", sw.ElapsedMilliseconds);
    }
}

/// <summary>Falha do provedor com classificação para o painel (limite de requisições, timeout, resposta inválida).</summary>
public sealed class FalhaProvedorIA(string tipo, string mensagem) : Exception(mensagem)
{
    public string Tipo { get; } = tipo;
}

/// <summary>APIs compatíveis com OpenAI Chat Completions (Groq, OpenAI, OpenRouter, Ollama…).</summary>
public sealed class ProvedorIACompativelOpenAI(HttpClient http, IOptions<OpcoesIA> op) : IProvedorIA, Modulos.InteligenciaTerritorial.IExplicadorIA
{
    public const string UrlGroq = "https://api.groq.com/openai/v1";
    public const string ModeloGroqPadrao = "openai/gpt-oss-20b";

    private bool EhGroq => string.Equals(op.Value.Provedor, "groq", StringComparison.OrdinalIgnoreCase) || op.Value.UrlBase?.Contains("groq.com") == true;
    private string UrlBase => (string.IsNullOrWhiteSpace(op.Value.UrlBase) ? (EhGroq ? UrlGroq : "https://api.openai.com/v1") : op.Value.UrlBase).TrimEnd('/');
    public bool Configurado => !string.IsNullOrWhiteSpace(op.Value.ChaveApi) || op.Value.UrlBase?.Contains("localhost") == true;
    private string Modelo => string.IsNullOrWhiteSpace(op.Value.Modelo) ? (EhGroq ? ModeloGroqPadrao : "gpt-4o-mini") : op.Value.Modelo;
    public string Descricao => EhGroq ? $"Groq ({Modelo})" : $"API compatível com OpenAI ({Modelo})";
    private string NomeProvedor => EhGroq ? "groq" : "openai-compat";

    public Task<RespostaBrutaIA> AnalisarAsync(EntradaAnaliseIA entrada, CancellationToken ct) =>
        EnviarAsync(PromptAnalise.Sistema, PromptAnalise.Usuario(entrada), 2000, ct);

    /// <summary>Explicação curta do indicador de alagamento (mesmo provedor e chave, sem dados pessoais).</summary>
    public async Task<string> ExplicarAsync(string sistema, string usuario, CancellationToken ct) => (await EnviarAsync(sistema, usuario, 500, ct)).Json;

    private async Task<RespostaBrutaIA> EnviarAsync(string sistema, string usuario, int maxTokens, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var corpoReq = new Dictionary<string, object?>
        {
            ["model"] = Modelo,
            ["temperature"] = 0,
            ["max_completion_tokens"] = maxTokens,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new object[]
            {
                new { role = "system", content = sistema },
                new { role = "user", content = usuario },
            },
        };
        // Modelos de raciocínio (gpt-oss): esforço baixo reduz latência; o raciocínio não volta no conteúdo.
        if (Modelo.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase)) corpoReq["reasoning_effort"] = "low";
        using var req = new HttpRequestMessage(HttpMethod.Post, UrlBase + "/chat/completions") { Content = JsonContent.Create(corpoReq) };
        if (!string.IsNullOrWhiteSpace(op.Value.ChaveApi)) req.Headers.Authorization = new("Bearer", op.Value.ChaveApi);

        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, ct); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new FalhaProvedorIA("timeout", $"{NomeProvedor}: tempo de resposta excedido."); }
        using var _ = resp;
        var corpo = await resp.Content.ReadAsStringAsync(ct);
        if ((int)resp.StatusCode == 429)
        {
            var espera = resp.Headers.RetryAfter?.Delta?.TotalSeconds;
            throw new FalhaProvedorIA("limite_requisicoes", $"{NomeProvedor}: limite de requisições (HTTP 429){(espera is { } e ? $"; tentar após {e:0} s" : "")}.");
        }
        if (!resp.IsSuccessStatusCode)
        {
            var codigo = TentarLer(corpo, "error", "code") ?? TentarLer(corpo, "error", "type");
            // Groq devolve 400 json_validate_failed quando o modelo não produz JSON válido.
            var tipo = codigo == "json_validate_failed" ? "resposta_invalida" : (int)resp.StatusCode >= 500 ? "indisponivel" : "erro_http";
            throw new FalhaProvedorIA(tipo, $"{NomeProvedor}: HTTP {(int)resp.StatusCode}{(codigo is null ? "" : $" ({codigo})")}.");
        }
        var texto = JsonNode.Parse(corpo)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(texto)) throw new FalhaProvedorIA("resposta_invalida", $"{NomeProvedor}: resposta sem conteúdo.");
        var modeloResp = JsonNode.Parse(corpo)?["model"]?.GetValue<string>() ?? Modelo;
        return new RespostaBrutaIA(texto, modeloResp, NomeProvedor, sw.ElapsedMilliseconds);
    }

    private static string? TentarLer(string json, string a, string b)
    {
        try { return JsonNode.Parse(json)?[a]?[b]?.GetValue<string>(); } catch { return null; }
    }
}
