using AlertaEmAcao.Infraestrutura.Adaptadores;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace AlertaEmAcao.Testes.AvaliacaoIA;

/// <summary>
/// Avaliação da camada de IA. A aprovação NÃO depende de outra IA: usamos respostas
/// pré-definidas (boas, inventadas, malformadas) contra o validador determinístico, e um
/// conjunto de casos fictícios revisados. O teste com modelo real só roda se IA__CHAVEAPI estiver definida.
/// </summary>
public class AvaliacaoIATestes(ITestOutputHelper saida)
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    public sealed record CasoAvaliacao(string Nome, ComplementoOcorrencia Complemento, bool ComLocal, string[] SinalizacoesEsperadas, string[] SinalizacoesProibidas);

    public static readonly CasoAvaliacao[] Casos =
    [
        new("Pessoa ilhada com água subindo", new() { Ilhado = true, AguaSubindo = true, QuantidadePessoas = 2, Descricao = "Estamos ilhados em cima da laje, água subindo." }, true,
            ["AGUA_SUBINDO_COM_ILHAMENTO"], []),
        new("Criança e pessoa idosa na residência", new() { QuantidadePessoas = 3, Pessoas = [new(6, false, false, false), new(81, false, true, true), new(35, false, false, false)], Descricao = "Casa alagando devagar." }, true,
            ["NECESSIDADE_ASSISTENCIA", "RISCO_HIDROLOGICO"], ["AGUA_SUBINDO"]),
        new("Pessoa ferida", new() { PessoasFeridas = 1, QuantidadePessoas = 2, Descricao = "Minha mãe caiu e está sangrando." }, true, ["PESSOA_FERIDA"], []),
        new("Deslizamento relatado", new() { RiscoDeslizamento = true, Descricao = "Deslizamento de barreira atrás da casa." }, true, ["RISCO_GEOLOGICO"], ["DESABAMENTO"]),
        new("Barreira com sinais de instabilidade", new() { BarreiraComSinaisInstabilidade = true, Descricao = "Rachadura nova na barreira e porta emperrando." }, true, ["RISCO_GEOLOGICO"], []),
        new("Rua bloqueada", new() { AcessoBloqueado = true, Descricao = "Árvore caída, rua bloqueada, não passa carro." }, true, ["ACESSO_BLOQUEADO"], ["PESSOA_FERIDA"]),
        new("Localização desconhecida", new() { Descricao = "Preciso de ajuda, alagou tudo", ReferenciaLocal = "perto da igreja" }, false, ["LOCALIZACAO_DESCONHECIDA"], []),
        new("Relato incompleto", new(), true, ["RELATO_INCOMPLETO"], ["PESSOA_FERIDA", "AGUA_SUBINDO"]),
        new("Informações contraditórias", new() { Sozinho = true, QuantidadePessoas = 5 }, true, ["INFORMACOES_CONTRADITORIAS"], []),
        new("Idade sozinha não é ameaça imediata", new() { QuantidadePessoas = 1, IdadeSolicitante = 90, Descricao = "Estou bem, só sem energia." }, true, [], ["AGUA_SUBINDO", "PESSOA_FERIDA", "DESABAMENTO"]),
    ];

    public static TheoryData<int> IndicesCasos() => new(Enumerable.Range(0, Casos.Length));

    private static Ocorrencia Montar(CasoAvaliacao c) => Ocorrencia.Registrar(Guid.NewGuid(), null, "dispositivo-avaliacao", Agora,
        c.ComLocal ? new PosicaoDispositivo(-8.06, -34.87, 15, Agora, "gps") : null, c.ComLocal ? "2611606" : null, c.Complemento,
        CanalRecebimento.Internet, null, null, true, Agora);

    [Theory]
    [MemberData(nameof(IndicesCasos))]
    public void Regras_deterministicas_nos_casos_revisados(int i)
    {
        var caso = Casos[i];
        var codigos = MotorRegrasUrgencia.Avaliar(Montar(caso), null, Agora).Select(s => s.Codigo).ToHashSet();
        saida.WriteLine($"{caso.Nome}: {string.Join(", ", codigos)}");
        Assert.All(caso.SinalizacoesEsperadas, e => Assert.Contains(e, codigos));
        Assert.All(caso.SinalizacoesProibidas, p => Assert.DoesNotContain(p, codigos));
    }

    private static EntradaAnaliseIA Entrada(CasoAvaliacao c)
    {
        var o = Montar(c);
        var a = new AnaliseOcorrencia(o.Id);
        a.AplicarRegras(MotorRegrasUrgencia.Avaliar(o, null, Agora), null, [], true, Agora);
        return ServicoAnalise.MontarEntrada(o, a);
    }

    [Fact]
    public void Resposta_bem_ancorada_e_aceita()
    {
        var e = Entrada(Casos[0]);
        var json = """
        {"resumo":"Duas pessoas ilhadas em laje com água subindo.","informacoesExtraidas":[{"descricao":"Ilhamento","evidencia":"Estamos ilhados em cima da laje"}],
         "pessoasEnvolvidas":2,"possiveisPerigos":[{"tipo":"hidrologico","descricao":"Elevação da água","evidencia":"água subindo"}],
         "necessidadesAssistencia":["retirada por barco"],"indiciosAgravamento":["água subindo"],"informacoesAusentes":["idades"],"informacoesNaoVerificadas":["nível da água"],
         "sinalizacoesParaAvaliacaoHumana":[{"codigo":"ILHAMENTO","explicacao":"Ilhados","evidencia":"Situação de ilhamento: sim"}]}
        """;
        var v = ValidadorSaidaIA.Validar(json, e);
        Assert.True(v.Valido);
        Assert.Equal(0, v.ItensDescartados);
        Assert.Equal(2, v.Resultado!.PessoasEnvolvidas);
    }

    [Fact]
    public void Informacao_inventada_e_descartada()
    {
        var e = Entrada(Casos[0]);
        var json = """
        {"resumo":"Pessoas ilhadas.","informacoesExtraidas":[{"descricao":"Há um cachorro","evidencia":"tem um cachorro preso"},{"descricao":"Ilhados","evidencia":"ilhados em cima da laje"}],
         "pessoasEnvolvidas":7,"possiveisPerigos":[{"tipo":"eletrico","descricao":"Fios caídos","evidencia":"fio de alta tensão caído"}],
         "necessidadesAssistencia":[],"indiciosAgravamento":[],"informacoesAusentes":[],"informacoesNaoVerificadas":[],
         "sinalizacoesParaAvaliacaoHumana":[{"codigo":"INCENDIO","explicacao":"x","evidencia":"água subindo"},{"codigo":"PESSOA_FERIDA","explicacao":"x","evidencia":"alguém ferido"}]}
        """;
        var v = ValidadorSaidaIA.Validar(json, e);
        Assert.True(v.Valido);
        Assert.Single(v.Resultado!.InformacoesExtraidas);      // cachorro inventado removido
        Assert.Empty(v.Resultado.PossiveisPerigos);            // fios inventados removidos
        Assert.Empty(v.Resultado.SinalizacoesParaAvaliacaoHumana); // código inválido + evidência inexistente
        Assert.Null(v.Resultado.PessoasEnvolvidas);            // 7 não foi informado
        Assert.True(v.ItensDescartados >= 5);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("{\"resumo\": 123}")]
    [InlineData("{\"informacoesExtraidas\": []}")]
    [InlineData("")]
    public void Resposta_malformada_e_rejeitada(string json) =>
        Assert.False(ValidadorSaidaIA.Validar(json, Entrada(Casos[2])).Valido);

    [Fact]
    public void Resposta_com_texto_em_volta_do_json_e_aceita()
    {
        var json = "Aqui está:\n```json\n{\"resumo\":\"Pessoa ferida.\",\"informacoesExtraidas\":[],\"possiveisPerigos\":[],\"necessidadesAssistencia\":[],\"indiciosAgravamento\":[],\"informacoesAusentes\":[],\"informacoesNaoVerificadas\":[],\"sinalizacoesParaAvaliacaoHumana\":[{\"codigo\":\"PESSOA_FERIDA\",\"explicacao\":\"x\",\"evidencia\":\"está sangrando\"}]}\n```";
        var v = ValidadorSaidaIA.Validar(json, Entrada(Casos[2]));
        Assert.True(v.Valido);
        Assert.Single(v.Resultado!.SinalizacoesParaAvaliacaoHumana);
    }

    [Fact]
    public void Ia_indisponivel_nao_impede_regras()
    {
        var o = Montar(Casos[0]);
        var a = new AnaliseOcorrencia(o.Id);
        a.AplicarRegras(MotorRegrasUrgencia.Avaliar(o, null, Agora), null, [], true, Agora);
        a.RegistrarFalhaIA("timeout", Agora);
        Assert.Equal(StatusAnaliseIA.Falhou, a.StatusIA);
        Assert.True(a.PrecisaIA); // ainda haverá nova tentativa
        Assert.Contains(a.Sinalizacoes, s => s.Codigo == "AGUA_SUBINDO_COM_ILHAMENTO");
    }

    /// <summary>
    /// Inferência REAL — executada somente quando IA__CHAVEAPI está definida (no CI, via secret do repositório).
    /// Provedor padrão: Groq (openai/gpt-oss-20b). Grava um relatório sem credenciais em IA__RELATORIO, se definido.
    /// </summary>
    [Fact]
    public async Task Modelo_real_produz_saida_valida_e_ancorada_quando_configurado()
    {
        var chave = Environment.GetEnvironmentVariable("IA__CHAVEAPI");
        if (string.IsNullOrWhiteSpace(chave)) { saida.WriteLine("IGNORADO: IA__CHAVEAPI não definida — avaliação com modelo real não executada."); return; }
        var provedor = Environment.GetEnvironmentVariable("IA__PROVEDOR") ?? "groq";
        var op = Options.Create(new OpcoesIA { Provedor = provedor, ChaveApi = chave, Modelo = Environment.GetEnvironmentVariable("IA__MODELO"), UrlBase = Environment.GetEnvironmentVariable("IA__URLBASE") });
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        IProvedorIA p = provedor == "anthropic" ? new ProvedorIAAnthropic(http, op) : new ProvedorIACompativelOpenAI(http, op);
        var aprovados = 0;
        var registros = new List<object>();
        foreach (var caso in Casos)
        {
            var e = Entrada(caso);
            try
            {
                RespostaBrutaIA? r = null;
                for (var tentativa = 1; tentativa <= 3 && r is null; tentativa++)
                {
                    try { r = await p.AnalisarAsync(e, CancellationToken.None); }
                    catch (FalhaProvedorIA f) when (f.Tipo == "limite_requisicoes" && tentativa < 3) { saida.WriteLine($"{caso.Nome}: {f.Message} — aguardando"); await Task.Delay(TimeSpan.FromSeconds(20)); }
                }
                var v = ValidadorSaidaIA.Validar(r!.Json, e);
                saida.WriteLine($"{caso.Nome}: modelo={r.Modelo} válido={v.Valido} descartados={v.ItensDescartados} ({r.DuracaoMs} ms) {string.Join(" | ", v.Problemas)}");
                if (v.Valido) aprovados++;
                registros.Add(new
                {
                    caso = caso.Nome, modelo = r.Modelo, provedor = r.Provedor, duracaoMs = r.DuracaoMs, valido = v.Valido,
                    itensDescartados = v.ItensDescartados, problemas = v.Problemas,
                    sinalizacoesIA = v.Resultado?.SinalizacoesParaAvaliacaoHumana.Select(s => s.Codigo),
                    pessoasIdentificadas = v.Resultado?.PessoasIdentificadas?.Count,
                    resumo = v.Resultado?.Resumo,
                });
            }
            catch (Exception ex)
            {
                saida.WriteLine($"{caso.Nome}: FALHA {ex.GetType().Name}: {ex.Message}");
                registros.Add(new { caso = caso.Nome, falha = $"{ex.GetType().Name}: {ex.Message}" });
            }
        }
        var arquivo = Environment.GetEnvironmentVariable("IA__RELATORIO");
        if (!string.IsNullOrWhiteSpace(arquivo))
            await File.WriteAllTextAsync(arquivo, System.Text.Json.JsonSerializer.Serialize(new
            {
                executadoEm = DateTimeOffset.UtcNow, provedor, descricao = p.Descricao, casos = Casos.Length, validos = aprovados, resultados = registros,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Assert.True(aprovados >= Casos.Length - 1, $"Somente {aprovados}/{Casos.Length} respostas válidas.");
    }

    // ---- Falhas do provedor (sem rede: respostas HTTP controladas) ----
    private sealed class RespostaFixa(Func<HttpRequestMessage, Task<HttpResponseMessage>> f) : HttpMessageHandler
    {
        public HttpRequestMessage? Ultima;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Ultima = r; return f(r); }
    }

    private static ProvedorIACompativelOpenAI Groq(RespostaFixa h, TimeSpan? timeout = null) =>
        new(new HttpClient(h) { Timeout = timeout ?? TimeSpan.FromSeconds(30) }, Options.Create(new OpcoesIA { Provedor = "groq", ChaveApi = "chave-de-teste" }));

    [Fact]
    public async Task Groq_usa_endpoint_e_modelo_padrao_sem_expor_dados_pessoais()
    {
        string? corpo = null;
        var h = new RespostaFixa(async r =>
        {
            corpo = await r.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"model":"openai/gpt-oss-20b","choices":[{"message":{"content":"{\"resumo\":\"ok\"}"}}]}""") };
        });
        var r = await Groq(h).AnalisarAsync(Entrada(Casos[0]), CancellationToken.None);
        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", h.Ultima!.RequestUri!.ToString());
        Assert.Equal("Bearer", h.Ultima.Headers.Authorization!.Scheme);
        Assert.Contains("\"model\":\"openai/gpt-oss-20b\"", corpo);
        Assert.Contains("json_object", corpo);
        Assert.DoesNotContain("dispositivo-avaliacao", corpo); // id do dispositivo não vai ao modelo
        Assert.Equal("groq", r.Provedor);
    }

    [Fact]
    public async Task Groq_limite_de_requisicoes_vira_falha_classificada()
    {
        var h = new RespostaFixa(_ =>
        {
            var m = new HttpResponseMessage((System.Net.HttpStatusCode)429) { Content = new StringContent("""{"error":{"message":"Rate limit","type":"tokens","code":"rate_limit_exceeded"}}""") };
            m.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(m);
        });
        var ex = await Assert.ThrowsAsync<FalhaProvedorIA>(() => Groq(h).AnalisarAsync(Entrada(Casos[0]), CancellationToken.None));
        Assert.Equal("limite_requisicoes", ex.Tipo);
        Assert.Contains("7 s", ex.Message);
        Assert.DoesNotContain("chave-de-teste", ex.Message);
    }

    [Fact]
    public async Task Groq_timeout_e_json_invalido_viram_falhas_classificadas()
    {
        var lento = new RespostaFixa(async r => { await Task.Delay(TimeSpan.FromSeconds(5)); return new HttpResponseMessage(System.Net.HttpStatusCode.OK); });
        var t = await Assert.ThrowsAsync<FalhaProvedorIA>(() => Groq(lento, TimeSpan.FromMilliseconds(200)).AnalisarAsync(Entrada(Casos[0]), CancellationToken.None));
        Assert.Equal("timeout", t.Tipo);

        var invalido = new RespostaFixa(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
            { Content = new StringContent("""{"error":{"message":"Failed to generate JSON","type":"invalid_request_error","code":"json_validate_failed"}}""") }));
        var j = await Assert.ThrowsAsync<FalhaProvedorIA>(() => Groq(invalido).AnalisarAsync(Entrada(Casos[0]), CancellationToken.None));
        Assert.Equal("resposta_invalida", j.Tipo);

        var indisponivel = new RespostaFixa(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("") }));
        Assert.Equal("indisponivel", (await Assert.ThrowsAsync<FalhaProvedorIA>(() => Groq(indisponivel).AnalisarAsync(Entrada(Casos[0]), CancellationToken.None))).Tipo);
    }

    [Fact]
    public void Pessoas_e_idades_so_sao_aceitas_quando_informadas()
    {
        var e = Entrada(Casos[1]); // idades 6, 81 e 35
        var json = """
        {"resumo":"Três pessoas, casa alagando.","informacoesExtraidas":[],"pessoasEnvolvidas":3,"possiveisPerigos":[],
         "necessidadesAssistencia":[],"indiciosAgravamento":[],"informacoesAusentes":[],"informacoesNaoVerificadas":[],"sinalizacoesParaAvaliacaoHumana":[],
         "pessoasIdentificadas":[{"idade":81,"condicoes":["mobilidade reduzida"],"evidencia":"Pessoa 2: idade 81"},
                                 {"idade":44,"condicoes":[],"evidencia":"Pessoa 3: idade 35"},
                                 {"idade":6,"condicoes":[],"evidencia":"criança no colo"}]}
        """;
        var v = ValidadorSaidaIA.Validar(json, e);
        Assert.True(v.Valido);
        var p = Assert.Single(v.Resultado!.PessoasIdentificadas!);
        Assert.Equal(81, p.Idade);
        Assert.Equal(2, v.ItensDescartados); // idade inventada (44) e evidência inexistente
    }

    [Fact]
    public void Complemento_reabre_a_analise_e_falhas_esperam_antes_de_nova_tentativa()
    {
        var a = new AnaliseOcorrencia(Guid.NewGuid());
        a.AplicarRegras([], null, [], true, Agora);
        a.RegistrarFalhaIA("limite", Agora);
        Assert.False(a.ProntaParaTentar(Agora.AddSeconds(5)));
        Assert.True(a.ProntaParaTentar(Agora.AddSeconds(16)));
        a.RegistrarFalhaIA("limite", Agora);
        Assert.False(a.ProntaParaTentar(Agora.AddSeconds(20)));
        Assert.True(a.ProntaParaTentar(Agora.AddSeconds(31)));
        a.AplicarRegras([], null, [], true, Agora); // complemento com informação nova
        Assert.Equal(StatusAnaliseIA.Pendente, a.StatusIA);
        Assert.Equal(0, a.TentativasIA);
    }
}
