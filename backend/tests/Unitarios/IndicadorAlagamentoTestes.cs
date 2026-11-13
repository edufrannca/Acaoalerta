using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Monitoramento;

namespace AlertaEmAcao.Testes.Unitarios;

/// <summary>Dados sintéticos. Ponto em Recife; estações posicionadas por deslocamento em latitude (~111 m por 0,001°).</summary>
public class IndicadorAlagamentoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 13, 14, 0, 0, TimeSpan.FromHours(-3));
    private static readonly PontoGeo P = new(-8.0631, -34.8711);

    private static LeituraChuva Est(string id, double dLat, double? mm1h, double? mm3h, int minutosAtras = 20, double? mm24h = 30) =>
        new(id, $"Estação {id}", P.Latitude + dLat, P.Longitude, "Recife", "2611606", Agora.AddMinutes(-minutosAtras),
            mm1h, mm3h, null, null, mm24h, null, null, "APAC/SIRH (sintético)");

    private static Aviso Aviso(string severidade, string titulo = "Chuvas Intensas", OrigemAviso origem = OrigemAviso.Inmet) =>
        Modulos.Alertas.Aviso.Criar(origem, Guid.NewGuid().ToString(), titulo, severidade, null, Agora.AddHours(-1), Agora.AddHours(6), null,
            ["2611606"], [], [], "INMET", null, Agora);

    private static IndicadorAlagamento Avaliar(IEnumerable<LeituraChuva> l, IEnumerable<Aviso>? a = null, int verificadas = 0, int relatos = 0, bool fonte = true) =>
        MotorIndicadorAlagamento.Avaliar(P, "Recife", l, a ?? [], "2611606", verificadas, relatos, null, fonte, Agora);

    [Theory]
    [InlineData(0.0, IntensidadeChuva.SemChuva)]
    [InlineData(1.0, IntensidadeChuva.Fraca)]
    [InlineData(2.5, IntensidadeChuva.Moderada)]
    [InlineData(10.0, IntensidadeChuva.Forte)]
    [InlineData(50.1, IntensidadeChuva.Violenta)]
    public void Intensidade_segue_classificacao_da_OMM(double mm, IntensidadeChuva esperada) =>
        Assert.Equal(esperada, MotorIndicadorAlagamento.Classificar(mm));

    [Fact]
    public void Leitura_atual_moderada_gera_atencao_e_desatualizada_nao_gera()
    {
        var atual = Avaliar([Est("a", 0.005, 4, 8)]);
        Assert.Equal(NivelAtencaoAlagamento.Atencao, atual.Nivel);
        Assert.NotNull(atual.Estacao);
        var velha = Avaliar([Est("a", 0.005, 4, 8, minutosAtras: 180)]);
        Assert.Equal(NivelAtencaoAlagamento.SemEvidenciaSuficiente, velha.Nivel);
        Assert.Null(velha.Estacao);
        Assert.Contains(velha.Justificativa, j => j.Contains("sem leitura recente"));
    }

    [Fact]
    public void Chuva_forte_eleva_e_violenta_ou_forte_com_ocorrencia_verificada_e_critico()
    {
        Assert.Equal(NivelAtencaoAlagamento.Elevado, Avaliar([Est("a", 0.002, 15, 20)]).Nivel);
        Assert.Equal(NivelAtencaoAlagamento.Critico, Avaliar([Est("a", 0.002, 60, 70)]).Nivel);
        Assert.Equal(NivelAtencaoAlagamento.Critico, Avaliar([Est("a", 0.002, 15, 20)], verificadas: 1).Nivel);
        Assert.Equal(NivelAtencaoAlagamento.Elevado, Avaliar([Est("a", 0.002, 0, 0)], verificadas: 1).Nivel);
        // Relatos não verificados não alteram o indicador.
        Assert.Equal(NivelAtencaoAlagamento.SemEvidenciaSuficiente, Avaliar([Est("a", 0.002, 0, 0)], relatos: 5).Nivel);
    }

    [Fact]
    public void Tendencia_crescente_e_decrescente()
    {
        Assert.Equal(TendenciaChuva.Crescente, MotorIndicadorAlagamento.Tendencia(6, 8));    // anterior 1 mm/h → 6
        Assert.Equal(TendenciaChuva.Decrescente, MotorIndicadorAlagamento.Tendencia(1, 13)); // anterior 6 mm/h → 1
        Assert.Equal(TendenciaChuva.Estavel, MotorIndicadorAlagamento.Tendencia(3, 9));
        Assert.Equal(TendenciaChuva.SemChuva, MotorIndicadorAlagamento.Tendencia(0, 0));
        Assert.Equal(TendenciaChuva.Indeterminada, MotorIndicadorAlagamento.Tendencia(3, null));
        // Chuva fraca aumentando gera atenção.
        Assert.Equal(NivelAtencaoAlagamento.Atencao, Avaliar([Est("a", 0.002, 2, 2.4)]).Nivel);
    }

    [Fact]
    public void Estacao_distante_reduz_confiabilidade_e_alem_de_10km_e_ignorada()
    {
        var perto = Avaliar([Est("a", 0.01, 4, 8, minutosAtras: 30)]);
        Assert.Equal("alta", perto.Confiabilidade.Nivel);
        var media = Avaliar([Est("a", 0.07, 0, 0)]);   // ~7,8 km
        Assert.Equal("baixa", media.Confiabilidade.Nivel);
        Assert.Contains(media.Confiabilidade.Motivos, m => m.Contains("mais de 5 km"));
        var longe = Avaliar([Est("a", 0.2, 30, 40)]);  // ~22 km
        Assert.Null(longe.Estacao);
        Assert.Equal(NivelAtencaoAlagamento.SemEvidenciaSuficiente, longe.Nivel);
        Assert.Contains(longe.Justificativa, j => j.Contains("Nenhuma estação"));
    }

    [Fact]
    public void Estacoes_divergentes_sinalizam_chuva_localizada()
    {
        var i = Avaliar([Est("perto", 0.003, 0.5, 1), Est("vizinha", 0.03, 20, 30)]);
        Assert.True(i.EstacoesDivergentes);
        Assert.Equal("Estação perto", i.Estacao!.Nome);
        Assert.True(i.Nivel >= NivelAtencaoAlagamento.Atencao);
        Assert.Equal("baixa", i.Confiabilidade.Nivel);
    }

    [Fact]
    public void Dados_insuficientes_e_fonte_indisponivel_nao_inventam_risco()
    {
        var i = Avaliar([], fonte: false);
        Assert.Equal(NivelAtencaoAlagamento.SemEvidenciaSuficiente, i.Nivel);
        Assert.Contains(i.Justificativa, j => j.Contains("indisponível"));
        Assert.False(i.Estimativa.Disponivel);
    }

    [Fact]
    public void Avisos_do_INMET_por_severidade_e_somente_de_chuva()
    {
        Assert.Equal(NivelAtencaoAlagamento.Atencao, Avaliar([], [Aviso("Perigo Potencial")]).Nivel);
        Assert.Equal(NivelAtencaoAlagamento.Elevado, Avaliar([], [Aviso("Perigo")]).Nivel);
        Assert.Equal(NivelAtencaoAlagamento.Critico, Avaliar([], [Aviso("Grande Perigo")]).Nivel);
        Assert.Equal(NivelAtencaoAlagamento.SemEvidenciaSuficiente, Avaliar([], [Aviso("Perigo", "Baixa Umidade")]).Nivel);
    }

    [Fact]
    public void Nunca_ha_estimativa_temporal_sem_modelo_validado()
    {
        var i = Avaliar([Est("a", 0.002, 60, 70)]);
        Assert.False(i.Estimativa.Disponivel);
        Assert.Null(i.Estimativa.Janela);
        Assert.Equal(MotorIndicadorAlagamento.SemEstimativa, i.Estimativa.Mensagem);
        Assert.Contains(i.Limitacoes, l => l.Contains("não é previsão"));
    }

    [Fact]
    public void Validador_da_IA_rejeita_numeros_inventados_previsoes_e_aceita_texto_fiel()
    {
        var i = Avaliar([Est("a", 0.002, 15, 20)]);
        var (_, numeros) = ServicoIndicadorAlagamento.Fatos(i);
        Assert.False(ValidadorExplicacao.Validar("{\"explicacao\":\"Choveu 80 mm na última hora.\"}", numeros, false).Ok);
        Assert.False(ValidadorExplicacao.Validar("{\"explicacao\":\"Há 70% de chance de alagar.\"}", numeros, false).Ok);
        Assert.False(ValidadorExplicacao.Validar("{\"explicacao\":\"A rua vai alagar logo.\"}", numeros, false).Ok);
        Assert.False(ValidadorExplicacao.Validar("{\"explicacao\":\"A casa está alagada.\"}", numeros, false).Ok);
        Assert.False(ValidadorExplicacao.Validar("texto solto", numeros, false).Ok);
        var ok = ValidadorExplicacao.Validar("{\"explicacao\":\"A estação mediu 15 mm na última hora, chuva forte. Evite áreas que costumam alagar.\"}", numeros, false);
        Assert.True(ok.Ok, ok.Motivo);
    }
}
