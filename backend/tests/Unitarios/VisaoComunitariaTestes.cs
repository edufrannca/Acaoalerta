using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Testes.Unitarios;

public class VisaoComunitariaTestes
{
    [Fact]
    public void Area_publica_sempre_contem_o_ponto_registrado_e_nao_e_centrada_nele()
    {
        var rnd = new Random(42);
        var centradas = 0;
        for (var i = 0; i < 5000; i++)
        {
            var p = new PontoGeo(-8.4 + rnd.NextDouble() * 0.8, -35.2 + rnd.NextDouble() * 0.4);
            double? prec = rnd.Next(4) == 0 ? null : rnd.NextDouble() * 500;
            var a = AreaAproximada.De(p, prec);
            Assert.True(a.Contem(p), $"ponto fora da área: {p}");
            Assert.True(a.RaioMetros >= AreaAproximada.RaioMinimoMetros);
            // A margem do GPS também fica dentro do círculo.
            Assert.True(a.Centro.DistanciaMetros(p) + (prec ?? 0) <= a.RaioMetros + 1);
            if (a.Centro.DistanciaMetros(p) < 1) centradas++;
        }
        Assert.True(centradas < 10);
    }

    [Fact]
    public void Pontos_proximos_compartilham_a_mesma_area_e_o_centro_nao_revela_a_posicao()
    {
        var a = AreaAproximada.De(new PontoGeo(-8.0641, -34.8711), 12);
        var b = AreaAproximada.De(new PontoGeo(-8.0646, -34.8714), 15);
        Assert.Equal(a.Centro, b.Centro);
    }

    [Theory]
    [InlineData(0.00010, 15, 20, true)]   // ~11 m, mesma sala: compatível
    [InlineData(0.00040, 15, 20, true)]   // ~44 m com margens 15+20+10
    [InlineData(0.00100, 15, 20, false)]  // ~111 m
    [InlineData(0.00100, 80, 60, true)]   // GPS ruim em ambos: margem maior
    public void Leituras_sao_comparadas_pelas_margens_de_precisao(double dLat, double precA, double precB, bool esperado)
    {
        var c = ComparacaoPosicoes.Comparar(new PontoGeo(-8.05, -34.9), precA, new PontoGeo(-8.05 + dLat, -34.9), precB);
        Assert.Equal(esperado, c.Compativeis);
    }

    [Fact]
    public void Faixas_e_tipos_nao_revelam_detalhes_individuais()
    {
        Assert.Null(ResumoComunitario.Faixa(0));
        Assert.Equal("1", ResumoComunitario.Faixa(1));
        Assert.Equal("2 a 4", ResumoComunitario.Faixa(3));
        Assert.Equal("5 ou mais", ResumoComunitario.Faixa(12));
        var c = new ComplementoOcorrencia { RiscoSegurancaPublica = true, EquipamentoMedicoEssencial = true };
        Assert.Equal(["Pedido de socorro (tipo não informado)"], ResumoComunitario.TiposEmergencia(c));
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 14, 30, 0, TimeSpan.FromHours(-3)),
            ResumoComunitario.HorarioAproximado(new DateTimeOffset(2026, 11, 13, 14, 44, 59, TimeSpan.FromHours(-3))));
    }

    [Fact]
    public void Necessidade_desconhecida_e_rejeitada()
    {
        Assert.Throws<ErroDominio>(() => new ComplementoOcorrencia { Necessidades = ["helicoptero"] }.Validar());
        new ComplementoOcorrencia { Necessidades = ["agua", "abrigo"] }.Validar();
    }

    [Fact]
    public void Oferta_comunitaria_segue_aceite_encaminhamento_e_acompanhamento()
    {
        var agora = DateTimeOffset.UtcNow;
        Assert.Throws<ErroDominio>(() => OfertaAjuda.RegistrarParaPedido(Guid.NewGuid(), null, "d1", Guid.NewGuid(), "2611606", TipoAjuda.DoacaoAgua, "água", null, null, false, agora));
        var o = OfertaAjuda.RegistrarParaPedido(Guid.NewGuid(), null, "d1", Guid.NewGuid(), "2611606", TipoAjuda.DoacaoAgua, "10 garrafas", null, null, true, agora);
        Assert.Equal(StatusOferta.AguardandoAvaliacao, o.Status);
        Assert.Throws<ErroDominio>(() => o.Encaminhar("ponto seguro", "eq", agora));
        Assert.Throws<ErroDominio>(() => o.Acompanhar("concluida", null, agora));
        o.AvaliarPelaEquipe(true, null, "eq", agora);
        o.Encaminhar("Entregar no ponto de apoio", "eq", agora);
        o.Acompanhar("nota", "saindo agora", agora);
        o.Acompanhar("concluida", null, agora);
        Assert.Equal(StatusOferta.Concluida, o.Status);
        Assert.Throws<ErroDominio>(() => o.Acompanhar("cancelada", null, agora));
    }

    [Fact]
    public void Pseudonimo_do_emissor_e_estavel_e_reconhece_o_dono()
    {
        var p = Ocorrencia.PseudonimoEmissor("aparelho-123456789012");
        Assert.Equal(p, Ocorrencia.PseudonimoEmissor("aparelho-123456789012"));
        Assert.NotEqual(p, Ocorrencia.PseudonimoEmissor("aparelho-999999999999"));
        Assert.StartsWith("p:", p);
        Assert.DoesNotContain("aparelho", p);
    }
}
