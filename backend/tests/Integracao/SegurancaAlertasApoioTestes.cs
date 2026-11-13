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
public class SegurancaAlertasApoioTestes(FabricaApi f)
{
    private static string Dispositivo() => "disp-" + Guid.NewGuid().ToString("N");

    private async Task<Guid> CriarPedidoAsync(double lat, double lon, object? complemento = null)
    {
        var id = Guid.NewGuid();
        var r = await f.ClienteDispositivo(Dispositivo()).PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(id, lat, lon, complemento));
        r.EnsureSuccessStatusCode();
        await f.DespacharAsync();
        return id;
    }

    [Fact]
    public async Task Painel_exige_autenticacao_e_papel()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().GetAsync("/api/painel/ocorrencias")).StatusCode);
        var cidadao = await f.ClienteDemoAsync("cidadao");
        Assert.Equal(HttpStatusCode.Forbidden, (await cidadao.GetAsync("/api/painel/ocorrencias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cidadao.PutAsJsonAsync("/simulador/centrais/DC-RECIFE/modo", new { modo = "Indisponivel" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cidadao.PostAsJsonAsync("/api/demo/avisos", new { titulo = "x", severidade = "x", municipioCodigo = "2611606", duracaoMinutos = 30 })).StatusCode);
    }

    [Fact]
    public async Task Operador_ve_somente_ocorrencias_da_sua_central_e_admin_ve_localizacao_aproximada()
    {
        var idRecife = await CriarPedidoAsync(-8.0631, -34.8711, new { descricao = "relato sigiloso de teste" });
        var olinda = await f.ClienteDemoAsync("defesa-civil-olinda");
        Assert.Equal(HttpStatusCode.Forbidden, (await olinda.GetAsync($"/api/painel/ocorrencias/{idRecife}")).StatusCode);
        var lista = await olinda.GetFromJsonAsync<JsonElement>("/api/painel/ocorrencias");
        Assert.DoesNotContain(lista.GetProperty("ocorrencias").EnumerateArray(), o => o.GetProperty("id").GetGuid() == idRecife);

        var recife = await f.ClienteDemoAsync("defesa-civil-recife");
        var det = await recife.GetFromJsonAsync<JsonElement>($"/api/painel/ocorrencias/{idRecife}");
        Assert.Equal(-8.0631, det.GetProperty("ocorrencia").GetProperty("localizacao").GetProperty("latitude").GetDouble(), 4);
        Assert.Contains("relato sigiloso", det.GetProperty("ocorrencia").GetProperty("complemento").GetProperty("descricao").GetString());
        Assert.Contains("SIMULADA", det.GetProperty("aviso").GetString());

        var admin = await f.ClienteDemoAsync("admin");
        var detAdmin = await admin.GetFromJsonAsync<JsonElement>($"/api/painel/ocorrencias/{idRecife}");
        var loc = detAdmin.GetProperty("ocorrencia").GetProperty("localizacao");
        Assert.True(loc.GetProperty("aproximada").GetBoolean());
        Assert.NotEqual(-8.0631, loc.GetProperty("latitude").GetDouble());
        Assert.Equal(JsonValueKind.Null, detAdmin.GetProperty("ocorrencia").GetProperty("complemento").ValueKind);

        var auditoria = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Auditoria.CountAsync(a => a.Recurso == $"ocorrencia:{idRecife}"));
        Assert.True(auditoria >= 2);
    }

    [Fact]
    public async Task Mapa_comunitario_nao_expoe_localizacao_precisa_nem_dados_pessoais()
    {
        await CriarPedidoAsync(-8.126234, -34.947812, new { descricao = "dado pessoal xyz" });
        var texto = await f.CreateClient().GetStringAsync("/api/ocorrencias/comunitarias");
        Assert.DoesNotContain("8.126234", texto);
        Assert.DoesNotContain("xyz", texto);
        Assert.DoesNotContain("dispositivo", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operador_atualiza_estado_e_retorno_http_chega_ao_alerta_em_acao()
    {
        var id = await CriarPedidoAsync(-8.0631, -34.8711);
        var op = await f.ClienteDemoAsync("defesa-civil-recife");
        var recs = await op.GetFromJsonAsync<JsonElement>("/simulador/centrais/DC-RECIFE/recebimentos");
        var protocolo = recs.GetProperty("recebimentos").EnumerateArray().First(r => r.GetProperty("identificadorReferencia").GetGuid() == id).GetProperty("protocolo").GetString();
        var r = await op.PatchAsJsonAsync($"/simulador/centrais/DC-RECIFE/recebimentos/{protocolo}/estado", new { estado = "EquipeDesignadaFicticia", nota = "Equipe fictícia 01" });
        r.EnsureSuccessStatusCode();
        Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("retornoEntregue").GetBoolean());
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Equal(StatusOcorrencia.EmAtendimentoSimulado, o.Status);
        Assert.Contains(o.Historico, h => h.Descricao.Contains("Equipe fictícia 01"));

        // Retorno sem a chave correta é recusado.
        var falso = await f.CreateClient().PostAsJsonAsync("/api/integracoes/retorno-central", new { versao = "alerta-em-acao/retorno-central/v1", centralCodigo = "DC-RECIFE", protocolo, estado = "ConcluidoFicticio", em = DateTimeOffset.UtcNow, ambiente = "DEMONSTRACAO", tipoMensagem = "ocorrencia", identificadorReferencia = id });
        Assert.Equal(HttpStatusCode.Unauthorized, falso.StatusCode);
    }

    [Fact]
    public async Task Alerta_proativo_notifica_somente_quem_tem_local_monitorado_na_area_e_consentiu_sem_duplicar()
    {
        var admin = await f.ClienteDemoAsync("admin");
        // Polígono em volta do local fictício "Casa" da cidadã demo (Ibura, Recife). Não cobre Olinda.
        var poligono = """{"type":"Polygon","coordinates":[[[-34.96,-8.14],[-34.93,-8.14],[-34.93,-8.11],[-34.96,-8.11],[-34.96,-8.14]]]}""";
        var r = await admin.PostAsJsonAsync("/api/demo/avisos", new { titulo = "Chuva intensa (teste)", severidade = "Perigo", poligonoGeoJson = poligono, duracaoMinutos = 60 });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var avisoId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var n1 = await (await admin.PostAsync("/api/demo/avisos/processar", null)).Content.ReadFromJsonAsync<JsonElement>();
        var n2 = await (await admin.PostAsync("/api/demo/avisos/processar", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, n2.GetProperty("notificacoes").GetInt32());

        var notificacoes = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Notificacao>().Where(n => n.AvisoId == avisoId).ToListAsync());
        var ana = await f.ClienteDemoAsync("cidadao");
        var minhas = await ana.GetFromJsonAsync<JsonElement>("/api/eu/notificacoes");
        var minha = minhas.EnumerateArray().Single(n => n.GetProperty("aviso").GetProperty("id").GetGuid() == avisoId);
        Assert.Equal("Há um aviso para sua região. Você e sua família estão bem?", minha.GetProperty("pergunta").GetString());
        Assert.False(minha.GetProperty("avisoOficial").GetBoolean());
        Assert.StartsWith("[DEMONSTRAÇÃO]", minha.GetProperty("titulo").GetString());
        Assert.Single(notificacoes); // João (Olinda) não está na área

        var joao = await f.ClienteDemoAsync("cidadao-olinda");
        var dele = await joao.GetFromJsonAsync<JsonElement>("/api/eu/notificacoes");
        Assert.DoesNotContain(dele.EnumerateArray(), n => n.GetProperty("aviso").GetProperty("id").GetGuid() == avisoId);

        var resp = await ana.PostAsJsonAsync($"/api/eu/notificacoes/{minha.GetProperty("id").GetGuid()}/resposta", new { resposta = "EstouSeguro" });
        resp.EnsureSuccessStatusCode();
        Assert.True(n1.GetProperty("notificacoes").GetInt32() >= 1);
    }

    [Fact]
    public async Task Aviso_por_municipio_notifica_todos_os_locais_do_municipio()
    {
        var admin = await f.ClienteDemoAsync("admin");
        var r = await admin.PostAsJsonAsync("/api/demo/avisos", new { titulo = "Alagamentos em Olinda (teste)", severidade = "Perigo", municipioCodigo = "2609600", duracaoMinutos = 30 });
        var avisoId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PostAsync("/api/demo/avisos/processar", null);
        var usuarios = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Notificacao>().Where(n => n.AvisoId == avisoId).Select(n => n.UsuarioId).ToListAsync());
        Assert.Single(usuarios);
    }

    [Fact]
    public async Task Cadastro_login_familia_e_local_monitorado()
    {
        var c = f.CreateClient();
        var email = $"teste{Guid.NewGuid():N}@exemplo.com";
        var reg = await c.PostAsJsonAsync("/api/auth/registrar", new { nome = "Pessoa de Teste", email, senha = "senha-forte-123", municipioCodigo = "2611606" });
        reg.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/auth/registrar", new { nome = "Outra", email, senha = "senha-forte-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/auth/entrar", new { email, senha = "errada-errada" })).StatusCode);
        var login = await c.PostAsJsonAsync("/api/auth/entrar", new { email, senha = "senha-forte-123" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var fam = await c.PutAsJsonAsync("/api/eu/familia", new { integrantes = new[] { new { idade = 4, mobilidadeReduzida = false, dependeAssistencia = true, usaEquipamentoMedico = false } } });
        fam.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync("/api/eu/locais", new { apelido = "Casa", latitude = -8.06, longitude = -34.88 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/eu/locais", new { apelido = "Caruaru", latitude = -8.28, longitude = -35.97 })).StatusCode);

        // Pedido autenticado incorpora o cadastro familiar como referência.
        c.DefaultRequestHeaders.Add("X-Dispositivo-Id", Dispositivo());
        var id = Guid.NewGuid();
        await c.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(id, -8.06, -34.88));
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().FirstAsync(x => x.Id == id));
        Assert.Single(o.Complemento.Pessoas!);
        Assert.Equal(1, o.Complemento.Criancas);

        // Telefone cifrado em repouso e exportação LGPD com os dados do próprio titular.
        (await c.PutAsJsonAsync("/api/eu/perfil", new { nome = "Pessoa de Teste", telefone = "81999990000", municipioCodigo = "2611606", consenteAlertas = true, consenteLocalizacao = true, receberPush = false })).EnsureSuccessStatusCode();
        var bruto = await f.NoEscopoAsync(async sp =>
        {
            var db = sp.GetRequiredService<AlertaDbContext>();
            var cn = db.Database.GetDbConnection();
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "select \"Telefone\" from alerta.usuarios where \"Email\" = @e";
            var par = cmd.CreateParameter(); par.ParameterName = "e"; par.Value = email; cmd.Parameters.Add(par);
            return (string?)await cmd.ExecuteScalarAsync();
        });
        Assert.StartsWith("v1:", bruto);
        var exp = await (await c.GetAsync("/api/eu/exportar")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("81999990000", exp.GetProperty("usuario").GetProperty("telefone").GetString());
        Assert.Single(exp.GetProperty("pedidos").EnumerateArray());
        Assert.Single(exp.GetProperty("locais").EnumerateArray());
    }

    [Fact]
    public async Task Fluxo_de_apoio_comunitario_com_confirmacao_da_organizacao_simulada()
    {
        var d = Dispositivo();
        var c = f.ClienteDispositivo(d);
        var nec = (await c.GetFromJsonAsync<JsonElement>("/api/apoio/necessidades?municipio=2611606")).GetProperty("necessidades")[0];
        Assert.True(nec.GetProperty("ficticia").GetBoolean());
        var ofertaId = Guid.NewGuid();
        var r = await c.PostAsJsonAsync("/api/apoio/ofertas", new { id = ofertaId, municipioCodigo = "2611606", tipo = "DoacaoAgua", descricao = "10 garrafas de água", contatoAutorizado = "(81) 90000-0001" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        (await c.PostAsJsonAsync($"/api/apoio/ofertas/{ofertaId}/participacao", new { necessidadeId = nec.GetProperty("id").GetGuid() })).EnsureSuccessStatusCode();
        await f.DespacharAsync();
        var minhas = await c.GetFromJsonAsync<JsonElement>("/api/apoio/ofertas/minhas");
        var m = minhas.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ofertaId);
        Assert.Equal("RecebidaPelaOrganizacao", m.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, m.GetProperty("contatoAutorizado").ValueKind);

        var org = await f.ClienteDemoAsync("organizacao");
        var painel = await org.GetFromJsonAsync<JsonElement>("/api/painel/organizacao/ofertas");
        var oferta = painel.GetProperty("ofertas").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ofertaId);
        Assert.Equal("(81) 90000-0001", oferta.GetProperty("contatoAutorizado").GetString());
        var protocolo = oferta.GetProperty("protocoloOrganizacao").GetString();
        (await org.PatchAsJsonAsync($"/simulador/centrais/ORG-MAOS/recebimentos/{protocolo}/estado", new { estado = "Aceita", nota = "Traga no sábado" })).EnsureSuccessStatusCode();
        minhas = await c.GetFromJsonAsync<JsonElement>("/api/apoio/ofertas/minhas");
        Assert.Equal("ConfirmadaPelaOrganizacao", minhas.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == ofertaId).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Contexto_territorial_informa_fontes_e_nunca_diz_area_segura()
    {
        var j = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/territorio/contexto?lat=-8.0145&lon=-34.8556");
        Assert.Equal("2609600", j.GetProperty("municipio").GetProperty("codigo").GetString());
        Assert.DoesNotContain("área segura", j.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não comprova ausência de perigo", j.GetRawText());
        Assert.NotEmpty(j.GetProperty("limitacoes").EnumerateArray());
    }

    [Fact]
    public async Task Abrigos_ficticios_identificados_e_disponibilidade_nao_verificada()
    {
        var j = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/abrigos");
        var abrigos = j.GetProperty("abrigos").EnumerateArray().ToList();
        Assert.NotEmpty(abrigos);
        Assert.All(abrigos.Where(a => a.GetProperty("ficticio").GetBoolean()), a => Assert.StartsWith("[FICTÍCIO]", a.GetProperty("nome").GetString()));
        Assert.All(abrigos, a => Assert.Equal("Disponibilidade não verificada", a.GetProperty("disponibilidade").GetString()));
    }

    [Fact]
    public async Task Cabecalhos_de_seguranca_presentes()
    {
        var r = await f.CreateClient().GetAsync("/api/estado");
        Assert.True(r.Headers.Contains("X-Content-Type-Options"));
        Assert.True(r.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Entrada_invalida_retorna_400_com_codigo()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var r = await c.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(Guid.NewGuid(), -8.06, -34.87, new { quantidadePessoas = 0 }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("pessoas_invalido", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString());
    }
}
