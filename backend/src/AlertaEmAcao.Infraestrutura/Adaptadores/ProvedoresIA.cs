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
        - Liste o que está ausente ou não verificado.
        - Responda apenas com um objeto JSON válido, sem texto adicional, no esquema:
        {"resumo": string (até 600 caracteres),
         "informacoesExtraidas": [{"descricao": string, "evidencia": string}],
         "pessoasEnvolvidas": number|null,
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
    private string Modelo => op.Value.Modelo ?? "claude-haiku-5-5";
    public string Descricao => $"Anthropic ({Modelo})";

    public async Task<RespostaBrutaIA> AnalisarAsync(EntradaAnaliseIA entrada, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var req = new HttpRequestMessage(HttpMethod.Post, (op.Value.UrlBase ?? "https://api.anthropic.com").TrimEnd('/') + "/v1/messages")
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

/// <summary>APIs compatíveis com OpenAI Chat Completions (OpenAI, Azure OpenAI, Groq, OpenRouter, Ollama…).</summary>
public sealed class ProvedorIACompativelOpenAI(HttpClient http, IOptions<OpcoesIA> op) : IProvedorIA
{
    public bool Configurado => !string.IsNullOrWhiteSpace(op.Value.ChaveApi) || op.Value.UrlBase?.Contains("localhost") == true;
    private string Modelo => op.Value.Modelo ?? "gpt-4o-mini";
    public string Descricao => $"API compatível com OpenAI ({Modelo})";

    public async Task<RespostaBrutaIA> AnalisarAsync(EntradaAnaliseIA entrada, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var req = new HttpRequestMessage(HttpMethod.Post, (op.Value.UrlBase ?? "https://api.openai.com/v1").TrimEnd('/') + "/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model = Modelo,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content = PromptAnalise.Sistema },
                    new { role = "user", content = PromptAnalise.Usuario(entrada) },
                },
            }),
        };
        if (!string.IsNullOrWhiteSpace(op.Value.ChaveApi)) req.Headers.Authorization = new("Bearer", op.Value.ChaveApi);
        using var resp = await http.SendAsync(req, ct);
        var corpo = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"Provedor HTTP {(int)resp.StatusCode}: {corpo[..Math.Min(200, corpo.Length)]}");
        var texto = JsonNode.Parse(corpo)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Resposta sem conteúdo.");
        return new RespostaBrutaIA(texto, Modelo, "openai-compat", sw.ElapsedMilliseconds);
    }
}
