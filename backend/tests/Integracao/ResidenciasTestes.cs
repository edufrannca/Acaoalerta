using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlertaEmAcao.Testes.Integracao;

[Collection("api")]
public class ResidenciasTestes(FabricaApi f)
{
    private static readonly object TodosConsentimentos = new { alertasPreventivos = true, compartilhamentoOperacional = true, compartilhamentoComunitario = true, notificacaoFamiliares = true };

    private static object Residencia(string apelido = "Minha casa", double lat = -8.0631, double lon = -34.8711, bool moro = true, object? moradores = null) => new
    {
        apelido, latitude = lat, longitude = lon, tipo = "Residencia", origem = "EscolhidoNoMapa", precisao = "Escolhido no mapa", moroAqui = moro,
        endereco = new { cep = "50030-230", logradouro = "Rua do Bom Jesus", numero = "125", bairro = "Recife", municipio = "Recife", uf = "PE" },
        moradores = moradores ?? new { total = 4, criancas = 2, idosos = 1, mobilidadeReduzida = 1, dependemAssistencia = 0, contatoReferencia = "Vizinha 81 98888-0000", observacoes = "Portão azul" },
    };

    private async Task<Guid> CriarLocal(HttpClient c, object corpo)
    {
        var r = await c.PostAsJsonAsync("/api/eu/locais", corpo);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Morador com residência e familiar com vínculo aprovado.</summary>
    private async Task<(HttpClient Morador, Guid Residencia, HttpClient Familiar, Guid Vinculo)> CenarioFamiliaAsync(object? consentimentosMorador = null)
    {
        var (morador, _) = await f.NovaContaAsync("Maria Mãe", consentimentosMorador ?? TodosConsentimentos);
        var res = await CriarLocal(morador, Residencia());
        var codigo = (await (await morador.PostAsync($"/api/eu/locais/{res}/convites", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString()!;
        var (familiar, _) = await f.NovaContaAsync("Ana Filha");
        var uso = await familiar.PostAsJsonAsync("/api/eu/acompanhar", new { codigo, apelido = "Casa da minha mãe" });
        Assert.Equal(HttpStatusCode.OK, uso.StatusCode);
        var vinculo = (await uso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await morador.PostAsJsonAsync($"/api/eu/vinculos/{vinculo}/decisao", new { aprovar = true })).EnsureSuccessStatusCode();
        return (morador, res, familiar, vinculo);
    }

    private static async Task<List<JsonElement>> NotificacoesFamilia(HttpClient c) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/eu/notificacoes-familia")).EnumerateArray().ToList();

    [Fact]
    public async Task Cep_endereco_e_mapa_retornam_dados_reais_sem_inventar_coordenadas()
    {
        var (c, _) = await f.NovaContaAsync("Pessoa Teste");
        // Sem conta, ninguém consulta (protege as fontes públicas).
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/geo/cep/50030230")).StatusCode);

        var valido = await c.GetFromJsonAsync<JsonElement>("/api/geo/cep/50030-230");
        Assert.Equal("Rua do Bom Jesus", valido.GetProperty("endereco").GetProperty("logradouro").GetString());
        Assert.Equal("2611606", valido.GetProperty("municipioCodigo").GetString());
        Assert.Single(valido.GetProperty("posicoes").EnumerateArray());
        Assert.True(valido.GetProperty("exigeEscolhaNoMapa").GetBoolean()); // só a rua: confirmar o ponto

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/geo/cep/99999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/geo/cep/123")).StatusCode);

        // CEP que só identifica o município: nenhuma coordenada; escolha no mapa obrigatória.
        var semRua = await c.GetFromJsonAsync<JsonElement>("/api/geo/cep/53130000");
        Assert.Empty(semRua.GetProperty("posicoes").EnumerateArray());
        Assert.True(semRua.GetProperty("exigeEscolhaNoMapa").GetBoolean());
        Assert.Contains("mapa", semRua.GetProperty("aviso").GetString());

        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/geo/cep/01310100")).GetProperty("dentroRmr").GetBoolean());

        var texto = await c.GetFromJsonAsync<JsonElement>("/api/geo/endereco?logradouro=Rua%20do%20Bom%20Jesus&numero=125&municipio=Recife&uf=PE");
        Assert.Single(texto.GetProperty("resultados").EnumerateArray());
        var ambiguo = await c.GetFromJsonAsync<JsonElement>("/api/geo/endereco?logradouro=Rua%20da%20Aurora&municipio=Recife&uf=PE");
        Assert.True(ambiguo.GetProperty("ambiguo").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/geo/endereco?municipio=Recife")).StatusCode);

        var ponto = await c.GetFromJsonAsync<JsonElement>("/api/geo/ponto?lat=-8.0089&lon=-34.8553");
        Assert.Equal("Olinda", ponto.GetProperty("municipio").GetProperty("nome").GetString());

        // Fonte fora do ar: erro claro, o mapa continua sendo o caminho.
        f.Geo.Indisponivel = true;
        try { Assert.Equal((HttpStatusCode)503, (await c.GetAsync("/api/geo/endereco?logradouro=Rua%20X&municipio=Recife")).StatusCode); }
        finally { f.Geo.Indisponivel = false; }
    }

    [Fact]
    public async Task Locais_por_gps_mapa_edicao_moradores_limite_e_remocao()
    {
        var (c, _) = await f.NovaContaAsync("Pessoa Locais");
        // GPS atual (corpo antigo continua aceito).
        var gps = await CriarLocal(c, new { apelido = "Onde estou", latitude = -8.05, longitude = -34.9 });
        var casa = await CriarLocal(c, Residencia());
        var detalhe = await c.GetFromJsonAsync<JsonElement>($"/api/eu/locais/{casa}");
        Assert.Equal("Residencia", detalhe.GetProperty("tipo").GetString());
        Assert.Equal("EscolhidoNoMapa", detalhe.GetProperty("origem").GetString());
        Assert.Equal("50030230", detalhe.GetProperty("endereco").GetProperty("cep").GetString());
        Assert.Equal(4, detalhe.GetProperty("moradores").GetProperty("total").GetInt32());

        // Edição de moradores e de posição.
        var editar = await c.PutAsJsonAsync($"/api/eu/locais/{casa}", Residencia(lat: -8.0640, moradores: new { total = 5, criancas = 3, idosos = 1 }));
        Assert.Equal(HttpStatusCode.OK, editar.StatusCode);
        var ed = await editar.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, ed.GetProperty("moradores").GetProperty("total").GetInt32());
        Assert.Equal(-8.064, ed.GetProperty("latitude").GetDouble(), 6);
        // Grupo maior que o total: recusado.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/eu/locais/{casa}", Residencia(moradores: new { total = 2, criancas = 3 }))).StatusCode);
        // Fora da RMR: recusado.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/eu/locais", new { apelido = "SP", latitude = -23.56, longitude = -46.65 })).StatusCode);

        // Dados privados cifrados em repouso (não aparecem em texto no banco).
        var bruto = await f.NoEscopoAsync(async sp =>
        {
            var db = sp.GetRequiredService<AlertaDbContext>();
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync();
            cmd.CommandText = $"SELECT \"Endereco\" || \"Moradores\" FROM alerta.locais_monitorados WHERE \"Id\" = '{casa}'";
            return (string)(await cmd.ExecuteScalarAsync())!;
        });
        Assert.DoesNotContain("Bom Jesus", bruto);
        Assert.DoesNotContain("Portão", bruto);

        for (var i = 0; i < 3; i++) await CriarLocal(c, new { apelido = $"Local {i}", latitude = -8.03 - i * 0.01, longitude = -34.92 });
        var sexto = await c.PostAsJsonAsync("/api/eu/locais", new { apelido = "Sexto", latitude = -8.1, longitude = -34.92 });
        Assert.Equal(HttpStatusCode.BadRequest, sexto.StatusCode);
        Assert.Contains("5 locais", await sexto.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/eu/locais/{gps}")).StatusCode);
        Assert.Equal(4, (await c.GetFromJsonAsync<JsonElement>("/api/eu/locais")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/eu/locais", new { apelido = "Novo", latitude = -8.1, longitude = -34.92 })).StatusCode);

        // Outra pessoa não vê nem edita o local.
        var (outra, _) = await f.NovaContaAsync("Outra Pessoa");
        Assert.Equal(HttpStatusCode.NotFound, (await outra.GetAsync($"/api/eu/locais/{casa}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outra.PutAsJsonAsync($"/api/eu/locais/{casa}", Residencia())).StatusCode);

        // Contexto climático de um local distante usa as coordenadas do local, não a posição de quem consulta.
        var ctx = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/territorio/contexto?lat=-8.064&lon=-34.8711");
        Assert.Equal("2611606", ctx.GetProperty("municipio").GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Vinculo_exige_convite_valido_e_aprovacao_e_pode_ser_revogado()
    {
        var (morador, _) = await f.NovaContaAsync("Morador", TodosConsentimentos);
        // Convite só para a residência onde a pessoa mora.
        var trabalho = await CriarLocal(morador, new { apelido = "Trabalho", latitude = -8.05, longitude = -34.9, tipo = "Trabalho" });
        Assert.Equal(HttpStatusCode.BadRequest, (await morador.PostAsync($"/api/eu/locais/{trabalho}/convites", null)).StatusCode);
        var res = await CriarLocal(morador, Residencia());
        var conv = await (await morador.PostAsync($"/api/eu/locais/{res}/convites", null)).Content.ReadFromJsonAsync<JsonElement>();
        var codigo = conv.GetProperty("codigo").GetString()!;
        Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", codigo);

        var (familiar, _) = await f.NovaContaAsync("Familiar");
        // Código errado e código certo dão a mesma mensagem genérica quando inválidos.
        var errado = await familiar.PostAsJsonAsync("/api/eu/acompanhar", new { codigo = "AAAAA-BBBBB", apelido = "Casa" });
        Assert.Equal(HttpStatusCode.BadRequest, errado.StatusCode);
        Assert.Contains("inválido ou expirado", await errado.Content.ReadAsStringAsync());
        // O morador não usa o próprio código.
        Assert.Equal(HttpStatusCode.BadRequest, (await morador.PostAsJsonAsync("/api/eu/acompanhar", new { codigo, apelido = "x" })).StatusCode);

        var uso = await familiar.PostAsJsonAsync("/api/eu/acompanhar", new { codigo = codigo.ToLowerInvariant().Replace("-", " "), apelido = "Casa da minha mãe" });
        Assert.Equal(HttpStatusCode.OK, uso.StatusCode);
        var vinculo = (await uso.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Antes da aprovação: aguardando, sem município revelado.
        var pend = (await familiar.GetFromJsonAsync<JsonElement>("/api/eu/acompanho")).EnumerateArray().Single();
        Assert.Equal("AguardandoAprovacao", pend.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, pend.GetProperty("municipio").ValueKind);
        // Código é de uso único.
        var (terceiro, _) = await f.NovaContaAsync("Terceiro");
        Assert.Equal(HttpStatusCode.BadRequest, (await terceiro.PostAsJsonAsync("/api/eu/acompanhar", new { codigo, apelido = "x" })).StatusCode);
        // Só o morador decide.
        Assert.Equal(HttpStatusCode.NotFound, (await familiar.PostAsJsonAsync($"/api/eu/vinculos/{vinculo}/decisao", new { aprovar = true })).StatusCode);
        (await morador.PostAsJsonAsync($"/api/eu/vinculos/{vinculo}/decisao", new { aprovar = true, permissoes = new { receberNotificacoes = true, verNecessidades = true } })).EnsureSuccessStatusCode();
        Assert.Equal("Recife", (await familiar.GetFromJsonAsync<JsonElement>("/api/eu/acompanho")).EnumerateArray().Single().GetProperty("municipio").GetString());

        // Convite expirado (24 h).
        var codigo2 = (await (await morador.PostAsync($"/api/eu/locais/{res}/convites", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString()!;
        f.Relogio.Avancar(TimeSpan.FromHours(25));
        try { Assert.Equal(HttpStatusCode.BadRequest, (await terceiro.PostAsJsonAsync("/api/eu/acompanhar", new { codigo = codigo2, apelido = "x" })).StatusCode); }
        finally { f.Relogio.Avancar(TimeSpan.FromHours(-25)); }

        // Morador revoga a qualquer momento.
        Assert.Equal(HttpStatusCode.NoContent, (await morador.DeleteAsync($"/api/eu/vinculos/{vinculo}")).StatusCode);
        Assert.Equal("Revogado", (await familiar.GetFromJsonAsync<JsonElement>("/api/eu/acompanho")).EnumerateArray().Single().GetProperty("status").GetString());
        // Terceiro não vê vínculos de quem não é.
        Assert.Empty((await terceiro.GetFromJsonAsync<JsonElement>($"/api/eu/locais/{res}/vinculos")).GetProperty("vinculos").EnumerateArray());
    }

    [Fact]
    public async Task Sos_do_morador_notifica_so_familiar_autorizado_com_estados_sem_duplicar()
    {
        var (morador, res, familiar, vinculo) = await CenarioFamiliaAsync();
        var (estranho, _) = await f.NovaContaAsync("Estranho");
        // Duas famílias no mesmo endereço: nenhum vínculo automático.
        var (vizinho, _) = await f.NovaContaAsync("Vizinho do mesmo prédio", TodosConsentimentos);
        await CriarLocal(vizinho, Residencia("Meu apto"));

        var id = Guid.NewGuid();
        var corpo = FluxoSocorroTestes.Pedido(id, -8.0632, -34.8712, new { aguaSubindo = true, necessidades = new[] { "agua" }, descricao = "Relato privado" });
        (await morador.PostAsJsonAsync("/api/ocorrencias", corpo)).EnsureSuccessStatusCode();

        var ns = await NotificacoesFamilia(familiar);
        var n = Assert.Single(ns);
        Assert.Equal("Pedido de socorro na residência monitorada", n.GetProperty("titulo").GetString());
        Assert.Contains("\"Casa da minha mãe\", em Recife", n.GetProperty("mensagem").GetString());
        Assert.Contains(n.GetProperty("entregas").EnumerateArray(), e => e.GetProperty("canal").GetString() == "aplicativo");
        Assert.Empty(await NotificacoesFamilia(estranho));
        Assert.Empty(await NotificacoesFamilia(vizinho));

        // Reenvio do mesmo pedido (sincronização repetida): sem duplicar.
        (await morador.PostAsJsonAsync("/api/ocorrencias", corpo)).EnsureSuccessStatusCode();
        await f.NoEscopoAsync(sp => sp.GetRequiredService<ServicoNotificacaoFamiliar>().ProcessarAsync(id, "registrada", CancellationToken.None));
        Assert.Single(await NotificacoesFamilia(familiar));

        // Visão do familiar: o permitido; posição exata só com permissão.
        var visao = await familiar.GetFromJsonAsync<JsonElement>($"/api/eu/familia/pedidos/{id}");
        Assert.Equal("Casa da minha mãe", visao.GetProperty("residencia").GetString());
        Assert.Equal(4, visao.GetProperty("nivelComprovado").GetInt32());
        Assert.Contains("Água potável", visao.GetProperty("necessidades")[0].GetString());
        Assert.Equal(JsonValueKind.Null, visao.GetProperty("posicao").ValueKind);
        Assert.Equal(JsonValueKind.Null, visao.GetProperty("moradoresCadastrados").ValueKind);
        Assert.DoesNotContain("Relato privado", visao.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, (await estranho.GetAsync($"/api/eu/familia/pedidos/{id}")).StatusCode);

        (await morador.PutAsJsonAsync($"/api/eu/vinculos/{vinculo}/permissoes", new { receberNotificacoes = true, verNecessidades = true, verMoradores = true, verPosicaoDoPedido = true })).EnsureSuccessStatusCode();
        var comPosicao = await familiar.GetFromJsonAsync<JsonElement>($"/api/eu/familia/pedidos/{id}");
        Assert.Equal(-8.0632, comPosicao.GetProperty("posicao").GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(4, comPosicao.GetProperty("moradoresCadastrados").GetProperty("total").GetInt32());

        // Atualizações: complemento e confirmação da central simulada.
        (await morador.PatchAsJsonAsync($"/api/ocorrencias/{id}/complemento", new { quantidadePessoas = 3 })).EnsureSuccessStatusCode();
        await f.DespacharAsync();
        var depois = await NotificacoesFamilia(familiar);
        Assert.Contains(depois, x => x.GetProperty("tipo").GetString() == "complemento");
        var estado = depois.Where(x => x.GetProperty("tipo").GetString() == "estado").ToList();
        Assert.Single(estado);
        Assert.Contains("SIMULADA", estado[0].GetProperty("mensagem").GetString());
        Assert.Equal(5, (await familiar.GetFromJsonAsync<JsonElement>($"/api/eu/familia/pedidos/{id}")).GetProperty("nivelComprovado").GetInt32());

        // Notificação lida.
        (await familiar.PostAsync($"/api/eu/notificacoes-familia/{n.GetProperty("id").GetGuid()}/lida", null)).EnsureSuccessStatusCode();

        // Revogação: familiar perde acesso imediatamente.
        (await morador.DeleteAsync($"/api/eu/vinculos/{vinculo}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await familiar.GetAsync($"/api/eu/familia/pedidos/{id}")).StatusCode);
        _ = res;
    }

    [Fact]
    public async Task Sos_anonimo_perto_da_residencia_nao_e_associado_e_falha_de_push_preserva_registro()
    {
        var (morador, res, familiar, _) = await CenarioFamiliaAsync();
        var anon = Guid.NewGuid();
        (await f.ClienteDispositivo("anon-" + Guid.NewGuid()).PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(anon, -8.0631, -34.8711))).EnsureSuccessStatusCode();
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().AsNoTracking().FirstAsync(x => x.Id == anon));
        Assert.Null(o.ResidenciaId);
        Assert.Empty(await NotificacoesFamilia(familiar));

        // Familiar com push ativo, serviço de push fora do ar: a notificação continua guardada.
        (await familiar.PutAsJsonAsync("/api/eu/perfil", new { consenteAlertas = true, consenteLocalizacao = false, receberPush = true })).EnsureSuccessStatusCode();
        (await familiar.PostAsJsonAsync("/api/eu/push/assinatura", new { endpoint = $"https://push.exemplo/{Guid.NewGuid()}", p256dh = "p", auth = "a" })).EnsureSuccessStatusCode();
        f.Push.Configurado = true; f.Push.Falhar = true;
        try
        {
            var id = Guid.NewGuid();
            (await morador.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(id, -8.0631, -34.8711))).EnsureSuccessStatusCode();
            var n = Assert.Single(await NotificacoesFamilia(familiar));
            Assert.Contains(n.GetProperty("entregas").EnumerateArray(), e => e.GetProperty("canal").GetString() == "web_push" && e.GetProperty("resultado").GetString() == "falha");
            Assert.True(f.Push.Tentativas > 0);
        }
        finally { f.Push.Configurado = false; f.Push.Falhar = false; }
        _ = res;
    }

    [Fact]
    public async Task Sem_consentimento_D_nada_e_associado_e_associacao_explicita_funciona()
    {
        var (morador, res, familiar, _) = await CenarioFamiliaAsync(new { compartilhamentoComunitario = false });
        var id = Guid.NewGuid();
        (await morador.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(id, -8.0631, -34.8711))).EnsureSuccessStatusCode();
        Assert.Empty(await NotificacoesFamilia(familiar));
        Assert.False((await morador.GetFromJsonAsync<JsonElement>($"/api/ocorrencias/{id}/residencia")).GetProperty("associada").GetBoolean());
        // Associação explícita exige o consentimento D.
        Assert.Equal(HttpStatusCode.BadRequest, (await morador.PostAsJsonAsync($"/api/ocorrencias/{id}/residencia", new { associar = true })).StatusCode);
        (await morador.PutAsJsonAsync("/api/eu/consentimentos", new { finalidade = "NotificacaoFamiliares", concedido = true })).EnsureSuccessStatusCode();
        (await morador.PostAsJsonAsync($"/api/ocorrencias/{id}/residencia", new { associar = true })).EnsureSuccessStatusCode();
        Assert.Single(await NotificacoesFamilia(familiar));
        var r = await morador.GetFromJsonAsync<JsonElement>($"/api/ocorrencias/{id}/residencia");
        Assert.Equal(1, r.GetProperty("familiaresVinculados").GetInt32());
        // Revogar D: familiar deixa de ver o pedido.
        (await morador.PutAsJsonAsync("/api/eu/consentimentos", new { finalidade = "NotificacaoFamiliares", concedido = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await familiar.GetAsync($"/api/eu/familia/pedidos/{id}")).StatusCode);
        _ = res;
    }

    [Fact]
    public async Task Consentimentos_opcionais_independentes_com_historico_e_preferencia_comunitaria()
    {
        var (c, _) = await f.NovaContaAsync("Sem Autorizações");
        var cs = await c.GetFromJsonAsync<JsonElement>("/api/eu/consentimentos");
        Assert.All(cs.GetProperty("finalidades").EnumerateArray(), x => Assert.False(x.GetProperty("concedido").GetBoolean()));
        Assert.Empty(cs.GetProperty("historico").EnumerateArray());

        // Pedido sem autorização: privado para a comunidade.
        var privado = Guid.NewGuid();
        (await c.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(privado, -8.11, -34.91, new { necessidades = new[] { "agua" } }))).EnsureSuccessStatusCode();

        // Concede só C: as outras continuam negadas; histórico com versão do texto.
        (await c.PutAsJsonAsync("/api/eu/consentimentos", new { finalidade = "CompartilhamentoComunitario", concedido = true })).EnsureSuccessStatusCode();
        cs = await c.GetFromJsonAsync<JsonElement>("/api/eu/consentimentos");
        var fin = cs.GetProperty("finalidades").EnumerateArray().ToDictionary(x => x.GetProperty("finalidade").GetString()!, x => x.GetProperty("concedido").GetBoolean());
        Assert.True(fin["CompartilhamentoComunitario"]);
        Assert.False(fin["NotificacaoFamiliares"]);
        Assert.False(fin["AlertasPreventivos"]);
        var h = Assert.Single(cs.GetProperty("historico").EnumerateArray());
        Assert.Equal("2026-11-13.1", h.GetProperty("versaoTexto").GetString());
        Assert.Equal("perfil", h.GetProperty("origem").GetString());

        // Pedido antigo NÃO é publicado retroativamente; pedido novo usa a preferência sem perguntar.
        var novo = Guid.NewGuid();
        var porPedido = Guid.NewGuid();
        (await c.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(novo, -8.111, -34.911, new { necessidades = new[] { "alimentos" } }))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(porPedido, -8.112, -34.912))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync($"/api/ocorrencias/{porPedido}/compartilhamento", new { autorizado = true, incluirNecessidades = true })).EnsureSuccessStatusCode();
        var pub = await f.CreateClient().GetStringAsync("/api/comunidade/pedidos?lat=-8.111&lon=-34.911&raioKm=2");
        Assert.DoesNotContain(privado.ToString(), pub);
        Assert.Contains(novo.ToString(), pub);

        // Revogar C retira o pedido que dependia da preferência; o autorizado no próprio pedido continua.
        var rev = await (await c.PutAsJsonAsync("/api/eu/consentimentos", new { finalidade = "CompartilhamentoComunitario", concedido = false })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, rev.GetProperty("pedidosRetiradosDaComunidade").GetInt32());
        pub = await f.CreateClient().GetStringAsync("/api/comunidade/pedidos?lat=-8.111&lon=-34.911&raioKm=2");
        Assert.DoesNotContain(novo.ToString(), pub);
        Assert.Contains(porPedido.ToString(), pub);
        Assert.Equal(2, (await c.GetFromJsonAsync<JsonElement>("/api/eu/consentimentos")).GetProperty("historico").GetArrayLength());

        // Cadastro com consentimentos marcados registra origem "cadastro".
        var (c2, _) = await f.NovaContaAsync("Com Autorizações", new { alertasPreventivos = true, notificacaoFamiliares = true });
        var cs2 = await c2.GetFromJsonAsync<JsonElement>("/api/eu/consentimentos");
        Assert.Equal(2, cs2.GetProperty("historico").GetArrayLength());
        Assert.All(cs2.GetProperty("historico").EnumerateArray(), x => Assert.Equal("cadastro", x.GetProperty("origem").GetString()));
        Assert.True((await c2.GetFromJsonAsync<JsonElement>("/api/eu")).GetProperty("consenteAlertas").GetBoolean());
    }
}
