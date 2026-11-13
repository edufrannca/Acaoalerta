using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.CentraisSimuladas;
using AlertaEmAcao.Contratos;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlertaEmAcao.Testes.Integracao;

[Collection("api")]
public class FluxoSocorroTestes(FabricaApi f)
{
    public static object Pedido(Guid id, double? lat, double? lon, object? complemento = null) => new
    {
        id, criadaEm = DateTimeOffset.UtcNow, latitude = lat, longitude = lon, precisaoMetros = 12, complemento,
    };

    private static string Dispositivo() => "disp-" + Guid.NewGuid().ToString("N");

    private async Task<JsonElement> Obter(HttpClient c, Guid id) =>
        await (await c.GetAsync($"/api/ocorrencias/{id}")).Content.ReadFromJsonAsync<JsonElement>();

    private async Task DefinirModo(string central, string modo, int atraso = 0)
    {
        var admin = await f.ClienteDemoAsync("admin");
        var r = await admin.PutAsJsonAsync($"/simulador/centrais/{central}/modo", new { modo, atrasoMs = atraso });
        r.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Banco_migrado_com_postgis_e_14_municipios()
    {
        var c = f.CreateClient();
        var h = await c.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", h.GetProperty("status").GetString());
        var m = await c.GetFromJsonAsync<JsonElement>("/api/municipios");
        Assert.Equal(14, m.GetArrayLength());
        var limites = await c.GetFromJsonAsync<JsonElement>("/api/municipios/limites");
        Assert.Equal(14, limites.GetProperty("features").GetArrayLength());
    }

    [Fact]
    public async Task Socorro_em_recife_com_agua_subindo_chega_a_defesa_civil_e_bombeiros_com_protocolo()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        var r = await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.1262, -34.9478, new { ilhado = true, aguaSubindo = true, quantidadePessoas = 3, descricao = "Estamos na laje" }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var corpo = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RecebidaPeloServidor", corpo.GetProperty("status").GetString());
        // Antes do despacho, nada pode estar "recebido" pelas centrais.
        Assert.All(corpo.GetProperty("encaminhamentos").EnumerateArray(), e => Assert.Equal("Pendente", e.GetProperty("status").GetString()));

        await f.DespacharAsync();
        var o = await Obter(c, id);
        Assert.Equal("ConfirmadaPorTodas", o.GetProperty("status").GetString());
        var encs = o.GetProperty("encaminhamentos").EnumerateArray().ToList();
        Assert.Equal(["CBM-SIM", "DC-RECIFE"], encs.Select(e => e.GetProperty("central").GetString()).Order().ToList());
        Assert.All(encs, e => Assert.StartsWith("SIM-", e.GetProperty("protocolo").GetString()));

        // A central simulada realmente persistiu a mensagem.
        var recebidos = await f.NoEscopoAsync(sp => sp.GetRequiredService<SimuladorDbContext>().Recebimentos.Where(x => x.IdentificadorReferencia == id).ToListAsync());
        Assert.Equal(2, recebidos.Count);
        Assert.All(recebidos, x => Assert.Equal("DEMONSTRACAO", JsonDocument.Parse(x.PayloadJson).RootElement.GetProperty("ambiente").GetString()));
    }

    [Fact]
    public async Task Reenvio_do_mesmo_pedido_e_idempotente()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        var corpo = Pedido(id, -8.0145, -34.8556, new { descricao = "alagamento na rua" });
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/ocorrencias", corpo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/ocorrencias", corpo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/ocorrencias", corpo)).StatusCode);
        await f.DespacharAsync();
        var n = await f.NoEscopoAsync(async sp =>
        {
            var db = sp.GetRequiredService<AlertaDbContext>();
            return (await db.Set<Ocorrencia>().CountAsync(o => o.Id == id), await db.Set<EncaminhamentoInstitucional>().CountAsync(e => e.ReferenciaId == id));
        });
        Assert.Equal(1, n.Item1);
        Assert.Equal(2, n.Item2); // DC-OLINDA + CBM (alagamento)
    }

    [Fact]
    public async Task Outro_dispositivo_nao_reutiliza_nem_le_o_pedido()
    {
        var id = Guid.NewGuid();
        await f.ClienteDispositivo(Dispositivo()).PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.06, -34.87));
        var outro = f.ClienteDispositivo(Dispositivo());
        Assert.Equal(HttpStatusCode.Forbidden, (await outro.GetAsync($"/api/ocorrencias/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await outro.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.06, -34.87))).StatusCode);
    }

    [Fact]
    public async Task Pedido_sem_cadastro_sem_localizacao_e_sem_complemento_e_aceito()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var r = await c.PostAsJsonAsync("/api/ocorrencias", new { id = Guid.NewGuid(), criadaEm = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var o = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SemDestinoConfigurado", o.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Pedido_sem_identificador_de_dispositivo_e_recusado()
    {
        var r = await f.CreateClient().PostAsJsonAsync("/api/ocorrencias", Pedido(Guid.NewGuid(), -8.06, -34.87));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Todos_os_14_municipios_tem_destino_de_defesa_civil_proprio()
    {
        var pontos = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Database
            .SqlQuery<PontoMunicipio>($"SELECT \"CodigoIbge\" AS \"Codigo\", ST_Y(ST_PointOnSurface(geom)) AS \"Lat\", ST_X(ST_PointOnSurface(geom)) AS \"Lon\" FROM alerta.municipios")
            .ToListAsync());
        Assert.Equal(14, pontos.Count);
        var c = f.ClienteDispositivo(Dispositivo());
        foreach (var p in pontos)
        {
            var id = Guid.NewGuid();
            var r = await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, p.Lat, p.Lon));
            var o = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(p.Codigo, o.GetProperty("municipio").GetProperty("codigo").GetString());
            var dc = Assert.Single(o.GetProperty("encaminhamentos").EnumerateArray());
            Assert.Equal(DadosIniciais.CodigoDefesaCivil(p.Codigo), dc.GetProperty("central").GetString());
        }
        await f.DespacharAsync();
    }

    public sealed class PontoMunicipio { public string Codigo { get; set; } = ""; public double Lat { get; set; } public double Lon { get; set; } }

    [Fact]
    public async Task Falha_de_uma_central_nao_afeta_outra_e_retentativa_entrega_sem_duplicar()
    {
        await DefinirModo("DC-IPOJUCA", "Indisponivel");
        try
        {
            var c = f.ClienteDispositivo(Dispositivo());
            var id = Guid.NewGuid();
            await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.4000, -35.0620, new { pessoasFeridas = 1, descricao = "pessoa ferida" }));
            await f.DespacharAsync();
            var o = await Obter(c, id);
            Assert.Equal("ConfirmadaParcialmente", o.GetProperty("status").GetString());
            var porCentral = o.GetProperty("encaminhamentos").EnumerateArray().ToDictionary(e => e.GetProperty("central").GetString()!, e => e);
            Assert.Equal("FalhaTemporaria", porCentral["DC-IPOJUCA"].GetProperty("status").GetString());
            Assert.Contains("503", porCentral["DC-IPOJUCA"].GetProperty("ultimoErro").GetString());
            Assert.Equal("Recebido", porCentral["CBM-SIM"].GetProperty("status").GetString());
            Assert.Equal("Recebido", porCentral["SAMU-SIM"].GetProperty("status").GetString());

            await DefinirModo("DC-IPOJUCA", "Normal");
            f.Relogio.Avancar(TimeSpan.FromMinutes(2)); // passa o backoff
            await f.DespacharAsync();
            o = await Obter(c, id);
            Assert.Equal("ConfirmadaPorTodas", o.GetProperty("status").GetString());
            var recebidos = await f.NoEscopoAsync(sp => sp.GetRequiredService<SimuladorDbContext>().Recebimentos.CountAsync(x => x.IdentificadorReferencia == id));
            Assert.Equal(3, recebidos);
        }
        finally { await DefinirModo("DC-IPOJUCA", "Normal"); }
    }

    [Theory]
    [InlineData("Rejeitar", "Rejeitado", "422")]
    [InlineData("ErroAutenticacao", "Rejeitado", "401")]
    public async Task Rejeicao_da_central_e_registrada_e_nao_e_reenviada_automaticamente(string modo, string esperado, string http)
    {
        await DefinirModo("DC-MORENO", modo);
        try
        {
            var c = f.ClienteDispositivo(Dispositivo());
            var id = Guid.NewGuid();
            await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.1180, -35.0900));
            await f.DespacharAsync();
            var e = (await Obter(c, id)).GetProperty("encaminhamentos")[0];
            Assert.Equal(esperado, e.GetProperty("status").GetString());
            Assert.Contains(http, e.GetProperty("ultimoErro").GetString());
            Assert.Equal(JsonValueKind.Null, e.GetProperty("protocolo").ValueKind);
        }
        finally { await DefinirModo("DC-MORENO", "Normal"); }
    }

    [Fact]
    public async Task Atraso_controlado_ainda_entrega()
    {
        await DefinirModo("DC-PAULISTA", "Atraso", 600);
        try
        {
            var c = f.ClienteDispositivo(Dispositivo());
            var id = Guid.NewGuid();
            await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -7.9400, -34.8730));
            await f.DespacharAsync();
            var e = (await Obter(c, id)).GetProperty("encaminhamentos")[0];
            Assert.Equal("Recebido", e.GetProperty("status").GetString());
        }
        finally { await DefinirModo("DC-PAULISTA", "Normal"); }
    }

    [Fact]
    public async Task Central_simulada_valida_autenticacao_esquema_e_idempotencia()
    {
        var c = f.CreateClient();
        var chave = ChavesCentrais.Derivar("segredo-de-teste", "DC-OLINDA");
        object Msg(string mun) => new
        {
            versao = VersoesContrato.MensagemCentral, tipoMensagem = "ocorrencia", identificadorOcorrencia = Guid.NewGuid(), identificadorCorrelacao = Guid.NewGuid(),
            municipioCodigo = mun, municipioNome = "Olinda", horarioOcorrencia = DateTimeOffset.UtcNow, tiposRisco = new[] { "hidrologico" },
            pessoasEnvolvidas = new { idades = Array.Empty<int>() }, necessidades = Array.Empty<string>(), sinalizacoes = Array.Empty<object>(), ambiente = "DEMONSTRACAO",
        };
        async Task<HttpResponseMessage> Enviar(object corpo, string? k, string idem)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/simulador/centrais/DC-OLINDA/mensagens") { Content = JsonContent.Create(corpo) };
            if (k is not null) req.Headers.Add(CabecalhosIntegracao.ChaveCentral, k);
            req.Headers.Add(CabecalhosIntegracao.ChaveIdempotencia, idem);
            return await c.SendAsync(req);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(Msg("2609600"), null, "a1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(Msg("2609600"), "chave-errada", "a1")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Enviar(Msg("2611606"), chave, "a2")).StatusCode); // fora da jurisdição
        Assert.Equal(HttpStatusCode.BadRequest, (await Enviar(new { tipoMensagem = "ocorrencia", versao = "x" }, chave, "a3")).StatusCode); // fora do esquema

        var corpo = Msg("2609600");
        var r1 = await Enviar(corpo, chave, "idem-unico-1");
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var p1 = await r1.Content.ReadFromJsonAsync<RespostaRecebimentoV1>();
        var r2 = await Enviar(corpo, chave, "idem-unico-1");
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var p2 = await r2.Content.ReadFromJsonAsync<RespostaRecebimentoV1>();
        Assert.Equal(p1!.Protocolo, p2!.Protocolo);
        Assert.True(p2.Duplicado);
        Assert.Contains("SIMULADA", p1.Aviso);
    }

    [Fact]
    public async Task Complemento_posterior_adiciona_destino_e_atualiza_centrais_ja_acionadas()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.0631, -34.8711));
        await f.DespacharAsync();
        var r = await c.PatchAsJsonAsync($"/api/ocorrencias/{id}/complemento", new { pessoasFeridas = 1, quantidadePessoas = 2 });
        r.EnsureSuccessStatusCode();
        await f.DespacharAsync();
        var encs = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<EncaminhamentoInstitucional>().Where(e => e.ReferenciaId == id).ToListAsync());
        Assert.Contains(encs, e => e.InstituicaoCodigo == "SAMU-SIM" && e.TipoMensagem == TipoMensagem.Ocorrencia);
        Assert.Contains(encs, e => e.InstituicaoCodigo == "DC-RECIFE" && e.TipoMensagem == TipoMensagem.AtualizacaoOcorrencia && e.Status == StatusEncaminhamento.Recebido);
    }

    [Fact]
    public async Task Atualizacao_de_localizacao_respeita_frequencia_e_muda_municipio()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        await c.PostAsJsonAsync("/api/ocorrencias", new { id, criadaEm = f.Relogio.Agora, latitude = -8.0631, longitude = -34.8711, precisaoMetros = 12 });
        f.Relogio.Avancar(TimeSpan.FromSeconds(5));
        var cedo = await c.PostAsJsonAsync($"/api/ocorrencias/{id}/localizacao", new { latitude = -8.0632, longitude = -34.8712, precisaoMetros = 10, lidaEm = f.Relogio.Agora });
        Assert.False((await cedo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("aceita").GetBoolean());
        f.Relogio.Avancar(TimeSpan.FromSeconds(40));
        var depois = await c.PostAsJsonAsync($"/api/ocorrencias/{id}/localizacao", new { latitude = -8.0145, longitude = -34.8556, precisaoMetros = 10, lidaEm = f.Relogio.Agora });
        var j = await depois.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(j.GetProperty("aceita").GetBoolean());
        Assert.Equal("2609600", j.GetProperty("municipioCodigo").GetString());
    }

    [Fact]
    public async Task Ia_valida_e_registrada_e_falha_da_ia_nao_bloqueia()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        f.IA.Falhar = false;
        f.IA.Resposta = _ => """
            {"resumo":"Pessoa relata alagamento.","informacoesExtraidas":[{"descricao":"Alagamento","evidencia":"alagamento na cozinha"}],"pessoasEnvolvidas":null,
             "possiveisPerigos":[],"necessidadesAssistencia":[],"indiciosAgravamento":[],"informacoesAusentes":["quantidade de pessoas"],"informacoesNaoVerificadas":[],
             "sinalizacoesParaAvaliacaoHumana":[{"codigo":"INCENDIO","explicacao":"inventado","evidencia":"fogo"}]}
            """;
        await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.0631, -34.8711, new { descricao = "Tem alagamento na cozinha" }));
        await f.DespacharAsync(); // transmissão independe da IA
        await f.NoEscopoAsync(sp => sp.GetRequiredService<ServicoAnalise>().ProcessarPendentesAsync(50, CancellationToken.None));
        var a = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<AnaliseOcorrencia>().FirstAsync(x => x.OcorrenciaId == id));
        Assert.Equal(StatusAnaliseIA.Concluida, a.StatusIA);
        Assert.Single(a.ResultadoIA!.InformacoesExtraidas);
        Assert.Empty(a.ResultadoIA.SinalizacoesParaAvaliacaoHumana);

        var id2 = Guid.NewGuid();
        f.IA.Falhar = true;
        await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id2, -8.0631, -34.8711, new { aguaSubindo = true }));
        await f.NoEscopoAsync(sp => sp.GetRequiredService<ServicoAnalise>().ProcessarPendentesAsync(50, CancellationToken.None));
        await f.DespacharAsync();
        f.IA.Falhar = false;
        var a2 = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<AnaliseOcorrencia>().FirstAsync(x => x.OcorrenciaId == id2));
        Assert.Equal(StatusAnaliseIA.Falhou, a2.StatusIA);
        Assert.Contains(a2.Sinalizacoes, s => s.Codigo == "AGUA_SUBINDO");
        Assert.Equal("ConfirmadaPorTodas", (await Obter(c, id2)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Dados_sensiveis_sao_cifrados_no_banco()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        await c.PostAsJsonAsync("/api/ocorrencias", Pedido(id, -8.0631, -34.8711, new { descricao = "Minha avó usa oxigênio" }));
        var bruto = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Database
            .SqlQuery<string>($"SELECT \"Complemento\" AS \"Value\" FROM alerta.ocorrencias WHERE \"Id\" = {id}").FirstAsync());
        Assert.StartsWith("v1:", bruto);
        Assert.DoesNotContain("oxig", bruto);
        // E é lido normalmente pelo domínio.
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().FirstAsync(x => x.Id == id));
        Assert.Contains("oxigênio", o.Complemento.Descricao);
    }

    [Fact]
    public async Task Gateway_simulado_registra_pedido_retransmitido()
    {
        var c = f.CreateClient();
        var id = Guid.NewGuid();
        var pacote = new PacoteGatewayV1(VersoesContrato.PacoteGateway, id, "celular-A-teste", "celular-B-teste", 1, DateTimeOffset.UtcNow, -8.0631, -34.8711, 30, 2, true, false, "ilhados", "nearby");
        var sem = await c.PostAsJsonAsync("/api/gateway/pacotes", pacote);
        Assert.Equal(HttpStatusCode.Unauthorized, sem.StatusCode);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/pacotes") { Content = JsonContent.Create(pacote) };
        req.Headers.Add("X-Gateway-Chave", ChavesCentrais.Derivar("segredo-de-teste", "gateway:celular-B-teste"));
        var r = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().FirstAsync(x => x.Id == id));
        Assert.Equal(CanalRecebimento.ProximidadeNearby, o.Canal);
        Assert.Contains("celular-B-teste", o.RetransmitidaPor);
    }

    [Fact]
    public async Task Estou_seguro_e_registrado_de_forma_idempotente()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var corpo = new { id = Guid.NewGuid(), status = "Seguro", declaradaEm = DateTimeOffset.UtcNow, latitude = -8.06, longitude = -34.87, desejaAjudar = true };
        var r1 = await c.PostAsJsonAsync("/api/situacoes", corpo);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Contains("Não certifica", await r1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/situacoes", corpo)).StatusCode);
    }

    private static HttpRequestMessage EntregaV2(object corpo, string receptor = "gateway-teste-v2", string? chave = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/v2/mensagens") { Content = JsonContent.Create(corpo) };
        req.Headers.Add("X-Gateway-Receptor", receptor);
        req.Headers.Add("X-Gateway-Chave", chave ?? ChavesCentrais.Derivar("segredo-de-teste", "gateway:" + receptor));
        return req;
    }

    private static MensagemProximidadeV2 MensagemV2(Guid ocorrencia, Guid? mensagem = null, string codigo = "ALERTA-DEMO-2026")
    {
        var agora = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var dados = JsonDocument.Parse("""{"ficticio":true,"quantidadePessoas":3,"idades":[4,40,79],"aguaSubindo":true,"ilhado":true,"descricao":"[FICTÍCIO] água subindo"}""").RootElement.Clone();
        var m = new MensagemProximidadeV2
        {
            VersaoProtocolo = ProtocoloProximidade.Versao, IdentificadorMensagem = (mensagem ?? Guid.NewGuid()).ToString(), IdentificadorOcorrencia = ocorrencia.ToString(),
            TipoMensagem = ProtocoloProximidade.Tipos.PedidoSocorro, Municipio = "2611606", CoordenadasAutorizadas = new(-8.0631, -34.8711, 25),
            HorarioCriacao = agora, HorarioUltimaAtualizacao = agora, RemetentePseudonimizado = ProtocoloProximidade.Pseudonimizar("teste-" + ocorrencia),
            DadosEstruturados = dados, NumeroTentativas = 0, EstadoTransmissao = ProtocoloProximidade.Estados.SalvoNoAparelho,
        };
        return m with { IntegridadeMensagem = ProtocoloProximidade.CalcularIntegridade(m, ProtocoloProximidade.ChaveGrupo(codigo)) };
    }

    [Fact]
    public async Task Gateway_v2_registra_niveis_1_a_5_sem_duplicar_e_rejeita_integridade_ou_chave_invalidas()
    {
        var c = f.CreateClient();
        var ocorrencia = Guid.NewGuid();
        var m = MensagemV2(ocorrencia);
        var corpo = new EntregaGatewayV2(m, ProtocoloProximidade.Pseudonimizar("receptor-B"), DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), "ble", "Aparelho de teste", "android", "ios");

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(EntregaV2(corpo, chave: "chave-errada"))).StatusCode);
        var adulterada = corpo with { Mensagem = m with { Municipio = "2609600" } };
        var r422 = await c.SendAsync(EntregaV2(adulterada));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r422.StatusCode);
        Assert.Contains("integridade_invalida", await r422.Content.ReadAsStringAsync());
        var outroGrupo = corpo with { Mensagem = MensagemV2(Guid.NewGuid(), codigo: "OUTRO-GRUPO") };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await c.SendAsync(EntregaV2(outroGrupo))).StatusCode);

        var r1 = await c.SendAsync(EntregaV2(corpo));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var j1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, j1.GetProperty("nivelComprovado").GetInt32());
        Assert.Equal("RECEBIDO_PELO_SERVIDOR", j1.GetProperty("estadoTransmissao").GetString());
        Assert.False(j1.GetProperty("duplicado").GetBoolean());

        // Reenvio (outra tentativa ou outro gateway) não duplica ocorrência nem confirmações.
        var r2 = await c.SendAsync(EntregaV2(corpo with { Mensagem = m with { NumeroTentativas = 3 } }, receptor: "gateway-teste-v2-outro"));
        Assert.True((await r2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicado").GetBoolean());

        await f.DespacharAsync();
        await f.DespacharAsync();
        var conf = new HttpRequestMessage(HttpMethod.Get, $"/api/gateway/v2/ocorrencias/{ocorrencia}/confirmacoes");
        conf.Headers.Add("X-Gateway-Receptor", "gateway-teste-v2");
        conf.Headers.Add("X-Gateway-Chave", ChavesCentrais.Derivar("segredo-de-teste", "gateway:gateway-teste-v2"));
        var jc = await (await c.SendAsync(conf)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, jc.GetProperty("nivelComprovado").GetInt32());
        var niveis = jc.GetProperty("confirmacoes").EnumerateArray().Select(x => x.GetProperty("nivel").GetInt32()).ToList();
        Assert.Equal(1, niveis.Count(n => n == 1));
        Assert.Equal(1, niveis.Count(n => n == 2));
        Assert.Equal(1, niveis.Count(n => n == 3));
        Assert.Equal(1, niveis.Count(n => n == 4));
        Assert.Contains(5, niveis);
        Assert.All(jc.GetProperty("confirmacoes").EnumerateArray().Where(x => x.GetProperty("nivel").GetInt32() == 5),
            x => Assert.StartsWith("SIM-", x.GetProperty("comprovante").GetString()));
        Assert.DoesNotContain(niveis, n => n > 5);

        var total = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().CountAsync(x => x.Id == ocorrencia));
        Assert.Equal(1, total);
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().FirstAsync(x => x.Id == ocorrencia));
        Assert.Equal(CanalRecebimento.ProximidadeNearby, o.Canal);
        Assert.Equal(3, o.Complemento.Pessoas!.Count);
        Assert.True(o.Complemento.Ilhado);

        var desconhecida = new HttpRequestMessage(HttpMethod.Get, $"/api/gateway/v2/ocorrencias/{Guid.NewGuid()}/confirmacoes");
        desconhecida.Headers.Add("X-Gateway-Receptor", "gateway-teste-v2");
        desconhecida.Headers.Add("X-Gateway-Chave", ChavesCentrais.Derivar("segredo-de-teste", "gateway:gateway-teste-v2"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(desconhecida)).StatusCode);
    }
}
