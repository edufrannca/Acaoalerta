using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlertaEmAcao.Testes.Integracao;

[Collection("api")]
public class AlagamentoTestes(FabricaApi f)
{
    // Ponto sintético em Olinda, longe da estação padrão de teste (Várzea).
    private const double Lat = -7.9405, Lon = -34.8732; // Paulista (sem outros dados de teste)

    private static LeituraChuva Forte(double mm1h = 18) =>
        new("forte-" + Guid.NewGuid().ToString("N")[..6], "[TESTE] Estação Paulista", Lat + 0.004, Lon, "Paulista", "2610707", null, mm1h, mm1h + 2, null, null, 40, null, null, "APAC/SIRH (sintético)");

    private async Task<T> ComChuva<T>(LeituraChuva l, Func<Task<T>> f2)
    {
        FonteChuvaFalsa.Extras.Add(l);
        try { return await f2(); } finally { FonteChuvaFalsa.Extras.Clear(); f.Explicador.Modo = "fiel"; }
    }

    private Task<JsonElement> Indicador(string? extra = null) =>
        f.CreateClient().GetFromJsonAsync<JsonElement>($"/api/territorio/alagamento?lat={Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}{extra}")!;

    [Fact]
    public async Task Indicador_publico_com_dados_separados_sem_estimativa_e_IA_validada()
    {
        var calmo = await Indicador();
        Assert.Equal("SemEvidenciaSuficiente", calmo.GetProperty("nivel").GetString());
        Assert.Equal("regras", calmo.GetProperty("explicacaoOrigem").GetString()); // IA nem é chamada sem atenção

        var j = await ComChuva(Forte(), () => Indicador());
        Assert.Equal("Elevado", j.GetProperty("nivel").GetString());
        Assert.Equal("Paulista", j.GetProperty("regiao").GetString());
        Assert.Equal("[TESTE] Estação Paulista", j.GetProperty("estacao").GetProperty("nome").GetString());
        Assert.False(j.GetProperty("estimativaTemporal").GetProperty("disponivel").GetBoolean());
        Assert.Contains("não existem dados suficientes", j.GetProperty("estimativaTemporal").GetProperty("mensagem").GetString());
        var sep = j.GetProperty("separacao");
        foreach (var k in new[] { "chuvaObservada", "previsaoOficial", "estimativaExperimental", "alagamentoConfirmado" }) Assert.True(sep.TryGetProperty(k, out _), k);
        Assert.Equal("ia_validada", j.GetProperty("explicacaoOrigem").GetString());
        Assert.NotEmpty(j.GetProperty("orientacoes").EnumerateArray());
        Assert.True(j.GetProperty("validoAte").GetDateTimeOffset() > j.GetProperty("avaliadoEm").GetDateTimeOffset());
    }

    [Fact]
    public async Task IA_que_inventa_ou_falha_e_substituida_pelas_regras()
    {
        f.Explicador.Modo = "inventa";
        var inventa = await ComChuva(Forte(22), () => Indicador("&regiao=teste-inventa"));
        Assert.Equal("regras", inventa.GetProperty("explicacaoOrigem").GetString());
        Assert.Contains("descartada", inventa.GetProperty("explicacaoObservacao").GetString());
        Assert.DoesNotContain("85%", inventa.GetRawText());
        f.Explicador.Modo = "falha";
        var falha = await ComChuva(Forte(23), () => Indicador("&regiao=teste-falha"));
        Assert.Equal("regras", falha.GetProperty("explicacaoOrigem").GetString());
        Assert.Contains("indisponível", falha.GetProperty("explicacaoObservacao").GetString());
        Assert.Equal("Elevado", falha.GetProperty("nivel").GetString());
    }

    [Fact]
    public async Task Agravamento_avisa_dono_com_consentimento_e_familiar_vinculado_sem_duplicar_nem_abrir_SOS()
    {
        var (dono, _) = await f.NovaContaAsync("Dona do local", new { alertasPreventivos = true, notificacaoFamiliares = true });
        var (semA, _) = await f.NovaContaAsync("Sem alertas");
        // Local distante de onde a pessoa está: o indicador usa as coordenadas do LOCAL.
        var corpo = new { apelido = "Casa", latitude = Lat, longitude = Lon, tipo = "Residencia", origem = "EscolhidoNoMapa", moroAqui = true };
        var local = (await (await dono.PostAsJsonAsync("/api/eu/locais", corpo)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await semA.PostAsJsonAsync("/api/eu/locais", corpo with { moroAqui = false })).EnsureSuccessStatusCode();
        var codigo = (await (await dono.PostAsync($"/api/eu/locais/{local}/convites", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString();
        var (familiar, _) = await f.NovaContaAsync("Familiar", new { alertasPreventivos = true });
        var v = (await (await familiar.PostAsJsonAsync("/api/eu/acompanhar", new { codigo, apelido = "Casa da mãe" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await dono.PostAsJsonAsync($"/api/eu/vinculos/{v}/decisao", new { aprovar = true })).EnsureSuccessStatusCode();
        var ocorrenciasAntes = await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().CountAsync());

        // Sem chuva: estado inicial registrado, nenhum aviso.
        await f.NoEscopoAsync(sp => sp.GetRequiredService<ServicoAlertaPreventivo>().ProcessarAsync(CancellationToken.None));
        Assert.Empty((await dono.GetFromJsonAsync<JsonElement>("/api/eu/notificacoes-preventivas")).EnumerateArray());

        await ComChuva(Forte(), async () =>
        {
            await f.NoEscopoAsync(sp => sp.GetRequiredService<ServicoAlertaPreventivo>().ProcessarAsync(CancellationToken.None));
            await f.NoEscopoAsync(sp => sp.GetRequiredService<ServicoAlertaPreventivo>().ProcessarAsync(CancellationToken.None)); // repetido: sem duplicar
            return 0;
        });
        var doDono = (await dono.GetFromJsonAsync<JsonElement>("/api/eu/notificacoes-preventivas")).EnumerateArray().ToList();
        var n = Assert.Single(doDono);
        Assert.Equal("Atenção à região da residência monitorada", n.GetProperty("titulo").GetString());
        Assert.Contains("não confirma alagamento", n.GetProperty("mensagem").GetString());
        Assert.Equal($"/locais/{local}", n.GetProperty("url").GetString());
        var doFamiliar = Assert.Single((await familiar.GetFromJsonAsync<JsonElement>("/api/eu/notificacoes-preventivas")).EnumerateArray());
        Assert.Contains("Casa da mãe", doFamiliar.GetProperty("mensagem").GetString());
        Assert.Empty((await semA.GetFromJsonAsync<JsonElement>("/api/eu/notificacoes-preventivas")).EnumerateArray());
        // Nenhum SOS é criado automaticamente.
        Assert.Equal(ocorrenciasAntes, await f.NoEscopoAsync(sp => sp.GetRequiredService<AlertaDbContext>().Set<Ocorrencia>().CountAsync()));
        (await dono.PostAsync($"/api/eu/notificacoes-preventivas/{n.GetProperty("id").GetGuid()}/lida", null)).EnsureSuccessStatusCode();
    }
}
