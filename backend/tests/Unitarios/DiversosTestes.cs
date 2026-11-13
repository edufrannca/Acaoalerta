using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Monitoramento;

namespace AlertaEmAcao.Testes.Unitarios;

public class DiversosTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    // Amostra reduzida do formato real de https://apiprevmet3.inmet.gov.br/avisos/ativos (12/11/2026).
    private const string AmostraInmet = """
    {"hoje":[
      {"id":1,"codigo":"urn:oid:2.49.0.0.76.0.2026.1.1","data_inicio":"2026-11-13T00:00:00.000Z","hora_inicio":"08:00","data_fim":"2026-11-13T00:00:00.000Z","hora_fim":"23:59",
       "poligono":"{\"type\":\"Polygon\",\"coordinates\":[[[-35.3,-8.7],[-34.7,-8.7],[-34.7,-7.6],[-35.3,-7.6],[-35.3,-8.7]]]}",
       "municipios":"Recife - PE (2611606),Olinda - PE (2609600),Caruaru - PE (2604106)","descricao":"Acumulado de Chuva","aviso_cor":"#FFFE00",
       "severidade":"Perigo Potencial","riscos":["Chuva entre 20 e 30 mm/h."],"instrucoes":["Obtenha mais informações junto à Defesa Civil (telefone 199)."]},
      {"id":2,"codigo":"urn:oid:2","data_inicio":"2026-11-13T00:00:00.000Z","hora_inicio":"08:00","data_fim":"2026-11-13T00:00:00.000Z","hora_fim":"23:59",
       "poligono":null,"municipios":"Belo Horizonte - MG (3106200)","descricao":"Tempestade","severidade":"Perigo"}
    ],"futuro":[]}
    """;

    [Fact]
    public void Parser_inmet_mantem_apenas_avisos_da_rmr_e_filtra_codigos()
    {
        var avisos = ParserAvisosInmet.Interpretar(AmostraInmet, Agora);
        var a = Assert.Single(avisos);
        Assert.Equal(OrigemAviso.Inmet, a.Origem);
        Assert.True(a.Oficial);
        Assert.Equal(["2611606", "2609600"], a.MunicipiosCodigos);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.FromHours(-3)), a.InicioEm);
        Assert.Equal("Perigo Potencial", a.Severidade);
        Assert.True(a.Abrange(new PontoGeo(-8.06, -34.87), "2611606"));
        Assert.False(a.Abrange(new PontoGeo(-9.6, -35.7), null));
    }

    [Fact]
    public void Aviso_vigencia_e_expiracao()
    {
        var a = ParserAvisosInmet.Interpretar(AmostraInmet, Agora)[0];
        Assert.True(a.Vigente(new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(-3))));
        Assert.False(a.Vigente(new DateTimeOffset(2026, 11, 14, 1, 0, 0, TimeSpan.FromHours(-3)))); // expirado
        Assert.False(a.Vigente(new DateTimeOffset(2026, 11, 13, 7, 0, 0, TimeSpan.FromHours(-3)))); // ainda não começou
    }

    [Fact]
    public void Aviso_demonstrativo_e_sempre_rotulado()
    {
        var a = Aviso.Criar(OrigemAviso.Demonstrativo, "d1", "Chuva intensa", "Perigo", null, Agora, Agora.AddHours(1), null, ["2611606"], [], [], "demo", null, Agora);
        Assert.StartsWith("[DEMONSTRAÇÃO]", a.Titulo);
        Assert.False(a.Oficial);
    }

    [Fact]
    public void Aviso_sem_area_ou_vigencia_invalida_e_rejeitado()
    {
        Assert.Throws<ErroDominio>(() => Aviso.Criar(OrigemAviso.Inmet, "x", "t", "s", null, Agora, Agora.AddHours(1), null, ["2604106"], [], [], "f", null, Agora));
        Assert.Throws<ErroDominio>(() => Aviso.Criar(OrigemAviso.Inmet, "x", "t", "s", null, Agora, Agora, null, ["2611606"], [], [], "f", null, Agora));
    }

    [Theory]
    [InlineData("2026-11-12 21:10:00 21:10:00", true)]
    [InlineData("1900-01-01 21:31:59", false)]
    [InlineData(null, false)]
    [InlineData("lixo", false)]
    public void Interpretador_apac_trata_datas_sem_leitura(string? valor, bool valida)
    {
        var d = InterpretadorApac.DataHora(valor);
        Assert.Equal(valida, d is not null);
        if (valida) Assert.Equal(TimeSpan.FromHours(-3), d!.Value.Offset);
    }

    [Fact]
    public void Interpretador_apac_trata_menos_um_como_sem_dado()
    {
        Assert.Null(InterpretadorApac.Milimetros(-1));
        Assert.Equal(0.42, InterpretadorApac.Milimetros(0.4196));
    }

    [Fact]
    public void Leitura_antiga_e_marcada_desatualizada()
    {
        var l = new LeituraChuva("1", "Estação", -8, -34.9, "Recife", "2611606", Agora.AddHours(-3), 0, 0, 0, 0, 0, 0, 0, "APAC");
        Assert.Equal(EstadoLeitura.Desatualizada, l.Estado(Agora));
        Assert.Equal(EstadoLeitura.Atual, (l with { UltimaLeituraEm = Agora.AddMinutes(-20) }).Estado(Agora));
        Assert.Equal(EstadoLeitura.SemLeitura, (l with { UltimaLeituraEm = null }).Estado(Agora));
    }

    [Fact]
    public void Contexto_sem_dados_nunca_afirma_area_segura()
    {
        var c = AvaliadorContextoTerritorial.Avaliar(new PontoGeo(-8.06, -34.87), "2611606", "Recife", [], [], [], [], true, true, Agora);
        var e = Assert.Single(c.Estados, x => x.Tipo == TipoEstadoInformativo.CondicaoDesconhecida);
        Assert.Contains("não comprova ausência de perigo", e.Detalhe);
        Assert.DoesNotContain(c.Estados, x => x.Titulo.Contains("segura", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contexto_identifica_suscetibilidade_aviso_relato_e_desatualizacao()
    {
        var p = new PontoGeo(-8.128689, -34.950349);
        var aval = new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(-3));
        var area = AreaRiscoDocumentada.Criar("Defesa Civil do Recife", null, "1", "Área de Risco Nível 3", "Ibura", "Lagoa Encantada",
            new PontoGeo(-8.1287, -34.9504), "2611606", "ODbL", Agora);
        var aviso = ParserAvisosInmet.Interpretar(AmostraInmet, Agora)[0];
        var leitura = new LeituraChuva("1", "Estação", -8.13, -34.95, "Recife", "2611606", aval.AddHours(-5), 0, 0, 0, 0, 12, 0, 0, "APAC");
        var ocorrencias = new[] { new OcorrenciaProxima(-8.129, -34.951, false, aval.AddMinutes(-30)), new OcorrenciaProxima(-8.129, -34.951, true, aval.AddMinutes(-10)) };
        var c = AvaliadorContextoTerritorial.Avaliar(p, "2611606", "Recife", [area], [aviso], [leitura], ocorrencias, true, true, aval);
        var tipos = c.Estados.Select(e => e.Tipo).ToHashSet();
        Assert.Contains(TipoEstadoInformativo.SuscetibilidadeConhecida, tipos);
        Assert.Contains(TipoEstadoInformativo.AvisoOficialVigente, tipos);
        Assert.Contains(TipoEstadoInformativo.OcorrenciaRelatada, tipos);
        Assert.Contains(TipoEstadoInformativo.OcorrenciaVerificada, tipos);
        Assert.Contains(TipoEstadoInformativo.InformacoesDesatualizadas, tipos);
        Assert.DoesNotContain(TipoEstadoInformativo.CondicaoDesconhecida, tipos);
        Assert.All(c.Estados.Where(e => e.Tipo != TipoEstadoInformativo.InformacoesDesatualizadas), e => Assert.NotNull(e.Fonte));
        Assert.NotEmpty(c.OrientacoesPreventivas);
    }

    [Fact]
    public void Contexto_fora_da_rmr()
    {
        var c = AvaliadorContextoTerritorial.Avaliar(new PontoGeo(-8.28, -35.97), null, null, [], [], [], [], true, true, Agora);
        Assert.True(c.ForaDaRmr);
    }

    [Fact]
    public void Contexto_com_fonte_de_chuva_indisponivel_informa_limitacao()
    {
        var c = AvaliadorContextoTerritorial.Avaliar(new PontoGeo(-8.06, -34.87), "2609600", "Olinda", [], [], [], [], false, false, Agora);
        Assert.Contains(c.Estados, e => e.Titulo.Contains("indisponível"));
        Assert.NotEmpty(c.LimitacoesConhecidas);
    }

    [Fact]
    public void Usuario_valida_email_municipio_e_senha()
    {
        Assert.Throws<ErroDominio>(() => Usuario.Registrar("Ana", "invalido", null, "h", null, Agora));
        Assert.Throws<ErroDominio>(() => Usuario.Registrar("Ana", "ana@x.com", null, "h", "2604106", Agora));
        Assert.Throws<ErroDominio>(() => PoliticaSenha.Validar("curta"));
        Assert.Throws<ErroDominio>(() => Usuario.Registrar("Op", "op@x.com", null, "h", null, Agora, Papel.OperadorCentral));
        var u = Usuario.Registrar(" Ana ", " ANA@Exemplo.com ", "(81) 99999-0000", "h", "2611606", Agora);
        Assert.Equal("ana@exemplo.com", u.Email);
        Assert.Equal("Ana", u.Nome);
    }

    [Fact]
    public void Push_so_e_ativado_com_consentimento_de_alertas()
    {
        var u = Usuario.Registrar("Ana", "ana@x.com", null, "h", null, Agora);
        u.AtualizarPerfil(null, null, null, null, consenteAlertas: false, consenteLocalizacao: true, receberPush: true);
        Assert.False(u.ReceberPush);
    }

    [Fact]
    public void Familia_calcula_vulnerabilidades_e_valida()
    {
        var f = new GrupoFamiliar(Guid.NewGuid());
        f.Definir([new IntegranteFamilia(null, 7, false, false, false, null), new IntegranteFamilia(null, 72, true, true, true, null)], [], Agora);
        Assert.Equal(1, f.Criancas);
        Assert.Equal(1, f.Idosos);
        Assert.True(f.AlguemComMobilidadeReduzida);
        Assert.Throws<ErroDominio>(() => f.Definir([new IntegranteFamilia(null, 130, false, false, false, null)], [], Agora));
    }
}
