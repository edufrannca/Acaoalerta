using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.Api.Endpoints;
using AlertaEmAcao.Contratos;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlertaEmAcao.Testes.Integracao;

[Collection("api")]
public class ComunidadeTestes(FabricaApi f)
{
    private static string Dispositivo() => "disp-" + Guid.NewGuid().ToString("N");

    private static readonly object Completo = new
    {
        quantidadePessoas = 4, criancas = 2, idosos = 1, mobilidadeReduzida = true, ilhado = true, aguaSubindo = true,
        necessidades = new[] { "agua", "transporte_assistido" }, descricao = "Maria, telefone 81 99999-0000, remédio de pressão",
        referenciaLocal = "casa azul ao lado da igreja",
    };

    private async Task<JsonElement> Pedidos(HttpClient c, double lat, double lon, double raio = 5) =>
        await c.GetFromJsonAsync<JsonElement>($"/api/comunidade/pedidos?lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&raioKm={raio}");

    [Fact]
    public async Task Pedido_so_aparece_para_a_comunidade_com_autorizacao_explicita_e_sem_dados_pessoais()
    {
        var disp = Dispositivo();
        var c = f.ClienteDispositivo(disp);
        var id = Guid.NewGuid();
        const double lat = -8.112345, lon = -34.901234;
        (await c.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(id, lat, lon, Completo))).EnsureSuccessStatusCode();

        var publico = f.CreateClient();
        var antes = await Pedidos(publico, -8.11, -34.90);
        Assert.DoesNotContain(antes.GetProperty("pedidos").EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
        Assert.True(antes.GetProperty("naoCompartilhados").GetInt32() >= 1);

        // Outro aparelho não pode autorizar em nome do solicitante.
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClienteDispositivo(Dispositivo()).PutAsJsonAsync($"/api/ocorrencias/{id}/compartilhamento",
            new { autorizado = true, incluirPessoas = true, incluirNecessidades = true })).StatusCode);

        (await c.PutAsJsonAsync($"/api/ocorrencias/{id}/compartilhamento", new { autorizado = true, incluirPessoas = true, incluirNecessidades = true })).EnsureSuccessStatusCode();
        var texto = await (await publico.GetAsync("/api/comunidade/pedidos?lat=-8.11&lon=-34.90&raioKm=5")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("8.112345", texto);
        Assert.DoesNotContain("Maria", texto);
        Assert.DoesNotContain("99999", texto);
        Assert.DoesNotContain("pressão", texto);
        Assert.DoesNotContain("igreja", texto);
        Assert.DoesNotContain(disp, texto);

        var depois = JsonDocument.Parse(texto).RootElement;
        var item = depois.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        Assert.Contains("Alagamento", item.GetProperty("tipos")[0].GetString());
        Assert.Equal(4, item.GetProperty("pessoas").GetInt32());
        Assert.Equal("2 a 4", item.GetProperty("criancas").GetString());
        Assert.Equal("1", item.GetProperty("idosos").GetString());
        Assert.Equal("1", item.GetProperty("mobilidadeReduzida").GetString());
        Assert.Equal(2, item.GetProperty("necessidades").GetArrayLength());
        Assert.True(item.GetProperty("podeOferecerAjuda").GetBoolean());
        Assert.Equal("Não verificado", item.GetProperty("verificacao").GetString());
        Assert.Equal(0, item.GetProperty("horarioAproximado").GetDateTimeOffset().Minute % 15);

        // A área pública contém o ponto registrado.
        var area = item.GetProperty("area");
        var centro = new PontoGeo(area.GetProperty("latitude").GetDouble(), area.GetProperty("longitude").GetDouble());
        Assert.True(centro.DistanciaMetros(new PontoGeo(lat, lon)) <= area.GetProperty("raioMetros").GetInt32());

        // Busca centrada no GPS de quem consulta: a ~25 km, fora do raio de 5 km.
        var longe = await Pedidos(publico, -7.90, -34.90);
        Assert.DoesNotContain(longe.GetProperty("pedidos").EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);

        // Revogação: deixa de aparecer.
        (await c.PutAsJsonAsync($"/api/ocorrencias/{id}/compartilhamento", new { autorizado = false })).EnsureSuccessStatusCode();
        var revogado = await Pedidos(publico, -8.11, -34.90);
        Assert.DoesNotContain(revogado.GetProperty("pedidos").EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Compartilhamento_parcial_respeita_escolhas_e_conta_ficticia_fica_fora_da_lista_publica()
    {
        var c = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        var corpo = new { id, criadaEm = DateTimeOffset.UtcNow, latitude = -8.05, longitude = -34.95, precisaoMetros = 20, complemento = Completo,
            compartilhamento = new { autorizado = true, incluirPessoas = false, incluirNecessidades = true } };
        (await c.PostAsJsonAsync("/api/ocorrencias", corpo)).EnsureSuccessStatusCode();
        var item = (await Pedidos(f.CreateClient(), -8.05, -34.95)).GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("pessoas").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("criancas").ValueKind);
        Assert.Equal(2, item.GetProperty("necessidades").GetArrayLength());

        // Pedido de conta fictícia (área de demonstração): só aparece quando a lista demonstrativa é pedida.
        var demo = await f.ClienteDemoAsync("cidadao");
        demo.DefaultRequestHeaders.Add("X-Dispositivo-Id", Dispositivo());
        var idDemo = Guid.NewGuid();
        (await demo.PostAsJsonAsync("/api/ocorrencias", corpo with { id = idDemo })).EnsureSuccessStatusCode();
        var pub = await Pedidos(f.CreateClient(), -8.05, -34.95);
        Assert.DoesNotContain(pub.GetProperty("pedidos").EnumerateArray(), p => p.GetProperty("id").GetGuid() == idDemo);
        var dem = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/comunidade/pedidos?lat=-8.05&lon=-34.95&incluirDemonstrativos=true");
        var itemDemo = dem.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == idDemo);
        Assert.True(itemDemo.GetProperty("demonstrativo").GetBoolean());
        Assert.DoesNotContain(dem.GetProperty("pedidos").EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Oferta_de_ajuda_passa_por_aceite_encaminhamento_e_acompanhamento_sem_expor_o_solicitante()
    {
        var solicitante = f.ClienteDispositivo(Dispositivo());
        var id = Guid.NewGuid();
        (await solicitante.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(id, -8.0631, -34.8711, Completo))).EnsureSuccessStatusCode();
        await f.DespacharAsync();

        var voluntario = f.ClienteDispositivo(Dispositivo());
        var oferta = new { id = Guid.NewGuid(), tipo = "DoacaoAgua", descricao = "10 garrafas de água", disponibilidade = "hoje à tarde", contatoAutorizado = "81 98888-7777", cienteSeguranca = true };
        // Pedido ainda não compartilhado: não aceita ofertas de desconhecidos.
        Assert.Equal(HttpStatusCode.BadRequest, (await voluntario.PostAsJsonAsync($"/api/comunidade/pedidos/{id}/ofertas", oferta)).StatusCode);
        (await solicitante.PutAsJsonAsync($"/api/ocorrencias/{id}/compartilhamento", new { autorizado = true, incluirPessoas = true, incluirNecessidades = true })).EnsureSuccessStatusCode();
        // Sem ciência das orientações de segurança: recusado.
        Assert.Equal(HttpStatusCode.BadRequest, (await voluntario.PostAsJsonAsync($"/api/comunidade/pedidos/{id}/ofertas", oferta with { cienteSeguranca = false })).StatusCode);
        // O próprio solicitante não oferta ao seu pedido.
        Assert.Equal(HttpStatusCode.BadRequest, (await solicitante.PostAsJsonAsync($"/api/comunidade/pedidos/{id}/ofertas", oferta with { id = Guid.NewGuid() })).StatusCode);

        var r = await voluntario.PostAsJsonAsync($"/api/comunidade/pedidos/{id}/ofertas", oferta);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AguardandoAvaliacao", j.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("instrucaoEquipe").ValueKind);
        var ofertaId = j.GetProperty("id").GetGuid();

        var publico = await Pedidos(f.CreateClient(), -8.0631, -34.8711);
        Assert.Equal(1, publico.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("ofertasEmAndamento").GetInt32());

        // Central sem relação com o pedido não decide.
        var olinda = await f.ClienteDemoAsync("defesa-civil-olinda");
        Assert.Equal(HttpStatusCode.Forbidden, (await olinda.PostAsJsonAsync($"/api/painel/ofertas/{ofertaId}/decisao", new { aceita = true })).StatusCode);

        var recife = await f.ClienteDemoAsync("defesa-civil-recife");
        var lista = await recife.GetFromJsonAsync<JsonElement>("/api/painel/ofertas-comunitarias");
        var naEquipe = lista.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == ofertaId);
        Assert.Equal("81 98888-7777", naEquipe.GetProperty("contatoAutorizado").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await recife.PostAsJsonAsync($"/api/painel/ofertas/{ofertaId}/encaminhamento", new { instrucao = "Entregar no ponto X" })).StatusCode);
        (await recife.PostAsJsonAsync($"/api/painel/ofertas/{ofertaId}/decisao", new { aceita = true, nota = "Obrigado" })).EnsureSuccessStatusCode();
        (await recife.PostAsJsonAsync($"/api/painel/ofertas/{ofertaId}/encaminhamento", new { instrucao = "Entregar na escola municipal (ponto de apoio seguro), portão 2." })).EnsureSuccessStatusCode();

        var minhas = await voluntario.GetStringAsync("/api/apoio/ofertas/minhas");
        Assert.Contains("Encaminhada", minhas);
        Assert.DoesNotContain("-8.0631", minhas);
        Assert.DoesNotContain("igreja", minhas);
        var concl = await voluntario.PostAsJsonAsync($"/api/apoio/ofertas/{ofertaId}/acompanhamento", new { acao = "concluida", nota = "Entregue no portão 2" });
        var jc = await concl.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Concluida", jc.GetProperty("status").GetString());
        Assert.Contains("escola municipal", jc.GetProperty("instrucaoEquipe").GetString());
        // Outro aparelho não altera a oferta.
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ClienteDispositivo(Dispositivo()).PostAsJsonAsync($"/api/apoio/ofertas/{ofertaId}/acompanhamento", new { acao = "cancelada" })).StatusCode);

        var det = await recife.GetFromJsonAsync<JsonElement>($"/api/painel/ocorrencias/{id}");
        Assert.Single(det.GetProperty("ofertasComunitarias").EnumerateArray());
    }

    private static PedidoQrV1 Qr(Guid id, string dispositivoEmissor, double? lat, double? lon)
    {
        var p = new PedidoQrV1(ComunidadeEndpoints.VersaoQr, id, Ocorrencia.PseudonimoEmissor(dispositivoEmissor), DateTimeOffset.UtcNow.AddMinutes(-3),
            lat, lon, lat is null ? null : 18, lat is null ? null : DateTimeOffset.UtcNow.AddMinutes(-3),
            new() { ["ilhado"] = true, ["aguaSubindo"] = false }, 3, ["agua"], new CompartilhamentoComunitario(true, true, true), "");
        return p with { H = ComunidadeEndpoints.HashQr(p) };
    }

    [Fact]
    public async Task Pedido_lido_por_QR_preserva_GPS_do_emissor_separa_o_do_retransmissor_e_nao_duplica()
    {
        var emissor = Dispositivo();
        var id = Guid.NewGuid();
        var qr = Qr(id, emissor, -8.0402, -34.9301);
        var ajudante = f.ClienteDispositivo(Dispositivo());

        var adulterado = qr with { Pessoas = 9 };
        var r422 = await ajudante.PostAsJsonAsync("/api/retransmissoes/qr", new { pedido = adulterado });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r422.StatusCode);

        // GPS do ajudante a ~30 m, com precisão de 20 m: leituras compatíveis, mas guardadas separadamente.
        var r = await ajudante.PostAsJsonAsync("/api/retransmissoes/qr", new { pedido = qr, latitudeRetransmissor = -8.0404, longitudeRetransmissor = -34.9303, precisaoRetransmissor = 20 });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(j.GetProperty("duplicado").GetBoolean());
        Assert.Equal(4, j.GetProperty("nivelComprovado").GetInt32());

        // Segundo aparelho lê o mesmo QR: não duplica.
        var r2 = await f.ClienteDispositivo(Dispositivo()).PostAsJsonAsync("/api/retransmissoes/qr", new { pedido = qr });
        Assert.True((await r2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicado").GetBoolean());

        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Equal(CanalRecebimento.ProximidadeQr, o.Canal);
        Assert.Equal(-8.0402, o.Latitude!.Value, 6);
        Assert.Equal(-8.0404, o.PosicaoRetransmissor!.Latitude, 6);
        Assert.True(o.Compartilhamento.Autorizado);
        Assert.DoesNotContain(emissor, o.DispositivoId);

        // O emissor volta a ter internet e sincroniza o MESMO pedido: reconhecido como dono, sem duplicar.
        var proprio = f.ClienteDispositivo(emissor);
        var sync = await proprio.PostAsJsonAsync("/api/ocorrencias", FluxoSocorroTestes.Pedido(id, -8.0402, -34.9301));
        Assert.Equal(HttpStatusCode.OK, sync.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await proprio.GetAsync($"/api/ocorrencias/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ajudante.GetAsync($"/api/ocorrencias/{id}")).StatusCode);
        var total = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().CountAsync(x => x.Id == id));
        Assert.Equal(1, total);

        // Operador vê as duas posições e a comparação pelas margens de precisão.
        await f.DespacharAsync();
        var det = await (await f.ClienteDemoAsync("defesa-civil-recife")).GetFromJsonAsync<JsonElement>($"/api/painel/ocorrencias/{id}");
        var pr = det.GetProperty("ocorrencia").GetProperty("posicaoRetransmissor");
        Assert.True(pr.GetProperty("comparacao").GetProperty("compativeis").GetBoolean());
        Assert.Contains("Não é a posição", pr.GetProperty("observacao").GetString());
    }

    [Fact]
    public async Task QR_sem_coordenadas_registra_localizacao_nao_informada_mesmo_com_GPS_do_ajudante()
    {
        var id = Guid.NewGuid();
        var qr = Qr(id, Dispositivo(), null, null);
        var r = await f.ClienteDispositivo(Dispositivo()).PostAsJsonAsync("/api/retransmissoes/qr",
            new { pedido = qr, latitudeRetransmissor = -8.06, longitudeRetransmissor = -34.88, precisaoRetransmissor = 10 });
        Assert.Contains("não informada", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("localizacaoEmissor").GetString());
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Null(o.Latitude);
        Assert.NotNull(o.PosicaoRetransmissor);
    }

    [Fact]
    public async Task Gateway_v2_guarda_GPS_do_receptor_separado_e_nao_o_usa_como_posicao_da_familia()
    {
        var ocorrencia = Guid.NewGuid();
        var agora = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var m = new MensagemProximidadeV2
        {
            VersaoProtocolo = ProtocoloProximidade.Versao, IdentificadorMensagem = Guid.NewGuid().ToString(), IdentificadorOcorrencia = ocorrencia.ToString(),
            TipoMensagem = ProtocoloProximidade.Tipos.PedidoSocorro, Municipio = null, CoordenadasAutorizadas = null,
            HorarioCriacao = agora, HorarioUltimaAtualizacao = agora, RemetentePseudonimizado = ProtocoloProximidade.Pseudonimizar("tablet-" + ocorrencia),
            NumeroTentativas = 0, EstadoTransmissao = ProtocoloProximidade.Estados.SalvoNoAparelho,
        };
        m = m with { IntegridadeMensagem = ProtocoloProximidade.CalcularIntegridade(m, ProtocoloProximidade.ChaveGrupo("ALERTA-DEMO-2026")) };
        var corpo = new EntregaGatewayV2(m, ProtocoloProximidade.Pseudonimizar("iphone-B"), agora, "nearby", null, "android", "ios", new CoordenadasAutorizadasV2(-8.06, -34.88, 8));
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/gateway/v2/mensagens") { Content = JsonContent.Create(corpo) };
        req.Headers.Add("X-Gateway-Receptor", "gateway-gps");
        req.Headers.Add("X-Gateway-Chave", ChavesCentrais.Derivar("segredo-de-teste", "gateway:gateway-gps"));
        (await f.CreateClient().SendAsync(req)).EnsureSuccessStatusCode();
        var o = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().AsNoTracking().FirstAsync(x => x.Id == ocorrencia));
        Assert.Null(o.Latitude);
        Assert.Equal(-8.06, o.PosicaoRetransmissor!.Latitude, 6);
        Assert.Contains(o.Historico, h => h.Tipo == "localizacao");
    }
}

public class ParidadeQrTestes
{
    [Fact]
    public void Texto_canonico_do_QR_e_identico_ao_da_PWA()
    {
        // Mesmo vetor de apps/web/src/lib/comunidade.test.ts.
        var p = new AlertaEmAcao.Api.Endpoints.PedidoQrV1("aea-qr/1", Guid.Parse("0193A5B0-1111-7222-8333-444455556666"), "p:abc",
            DateTimeOffset.Parse("2026-11-13T12:34:56.789Z"), -8.0402, -34.9301, 18, null,
            new() { ["ilhado"] = true, ["aguaSubindo"] = false, ["acessoBloqueado"] = true }, 3, ["transporte_assistido", "agua"],
            new CompartilhamentoComunitario(true, false, true), "");
        Assert.Equal("aea-qr/1|0193a5b0-1111-7222-8333-444455556666|p:abc|2026-11-13T12:34:56Z|-8.0402|-34.9301|18|acessoBloqueado,ilhado|3|agua,transporte_assistido|101|",
            AlertaEmAcao.Api.Endpoints.ComunidadeEndpoints.CanonicoQr(p));
    }
}
