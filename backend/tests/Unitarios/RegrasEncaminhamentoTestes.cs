using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Testes.Unitarios;

public class RegrasEncaminhamentoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static List<Instituicao> Instituicoes()
    {
        var l = MunicipiosRmr.Todos.Select(m => Instituicao.Criar($"DC-{m.Codigo}", $"Defesa Civil {m.Nome} — CENTRAL SIMULADA", TipoInstituicao.DefesaCivilMunicipal,
            m.Codigo, m.Nome, CanalIntegracao.HttpCentralSimulada, EstadoIntegracao.Simulada, "/x")).ToList();
        l.Add(Instituicao.Criar("CBM", "Bombeiros — RECEPTOR SIMULADO", TipoInstituicao.CorpoBombeiros, null, "RMR", CanalIntegracao.HttpCentralSimulada, EstadoIntegracao.Simulada, "/x"));
        l.Add(Instituicao.Criar("SAMU", "SAMU — RECEPTOR SIMULADO", TipoInstituicao.Samu, null, "RMR", CanalIntegracao.HttpCentralSimulada, EstadoIntegracao.Simulada, "/x"));
        l.Add(Instituicao.Criar("PM", "PM — RECEPTOR SIMULADO", TipoInstituicao.PoliciaMilitar, null, "RMR", CanalIntegracao.HttpCentralSimulada, EstadoIntegracao.Simulada, "/x"));
        return l;
    }

    private static readonly List<RegraEncaminhamento> Regras =
    [
        RegraEncaminhamento.Criar(null, TipoInstituicao.DefesaCivilMunicipal, Gatilho.Sempre, "DC"),
        RegraEncaminhamento.Criar(null, TipoInstituicao.CorpoBombeiros, Gatilho.RiscoHidrologico, "CBM água"),
        RegraEncaminhamento.Criar(null, TipoInstituicao.CorpoBombeiros, Gatilho.PessoaFerida, "CBM ferido"),
        RegraEncaminhamento.Criar(null, TipoInstituicao.Samu, Gatilho.PessoaFerida, "SAMU"),
        RegraEncaminhamento.Criar(null, TipoInstituicao.PoliciaMilitar, Gatilho.SegurancaPublica, "PM"),
    ];

    [Theory]
    [MemberData(nameof(TodosMunicipios))]
    public void Cada_municipio_encaminha_para_sua_propria_defesa_civil(string codigo)
    {
        var d = RoteadorEncaminhamento.Determinar(codigo, [], Regras, Instituicoes());
        var dc = Assert.Single(d);
        Assert.Equal($"DC-{codigo}", dc.Instituicao.Codigo);
    }

    public static TheoryData<string> TodosMunicipios() => new(MunicipiosRmr.Todos.Select(m => m.Codigo));

    [Fact]
    public void Inundacao_vai_para_defesa_civil_e_bombeiros_mas_nao_para_samu_nem_pm()
    {
        var d = RoteadorEncaminhamento.Determinar("2609600", [Indicio.RiscoHidrologico], Regras, Instituicoes()).Select(x => x.Instituicao.Codigo).ToList();
        Assert.Equal(["DC-2609600", "CBM"], d);
    }

    [Fact]
    public void Pessoa_ferida_inclui_samu_e_bombeiros_sem_duplicar()
    {
        var d = RoteadorEncaminhamento.Determinar("2607208", [Indicio.PessoaFerida, Indicio.RiscoHidrologico], Regras, Instituicoes()).Select(x => x.Instituicao.Codigo).ToList();
        Assert.Equal(3, d.Count);
        Assert.Contains("SAMU", d);
        Assert.Single(d, c => c == "CBM");
    }

    [Fact]
    public void Fora_da_rmr_ou_sem_municipio_nao_encaminha()
    {
        Assert.Empty(RoteadorEncaminhamento.Determinar(null, [Indicio.PessoaFerida], Regras, Instituicoes()));
        Assert.Empty(RoteadorEncaminhamento.Determinar("2604106", [Indicio.PessoaFerida], Regras, Instituicoes())); // Caruaru
    }

    [Fact]
    public void Instituicao_desabilitada_nao_recebe()
    {
        var insts = Instituicoes();
        insts.First(i => i.Codigo == "CBM").Habilitar(false);
        var d = RoteadorEncaminhamento.Determinar("2611606", [Indicio.RiscoHidrologico], Regras, insts);
        Assert.DoesNotContain(d, x => x.Instituicao.Codigo == "CBM");
    }

    [Fact]
    public void Central_simulada_precisa_ser_identificada_no_nome() =>
        Assert.Throws<ErroDominio>(() => Instituicao.Criar("X", "Corpo de Bombeiros", TipoInstituicao.CorpoBombeiros, null, "RMR",
            CanalIntegracao.HttpCentralSimulada, EstadoIntegracao.Simulada, "/x"));

    private static EncaminhamentoInstitucional NovoEnc() =>
        EncaminhamentoInstitucional.Criar(TipoMensagem.Ocorrencia, Guid.NewGuid(), Instituicoes()[0], "teste", Agora);

    [Fact]
    public void Encaminhamento_so_fica_recebido_com_protocolo_durante_transmissao()
    {
        var e = NovoEnc();
        Assert.Throws<ErroDominio>(() => e.ConfirmarRecebimento("P1", Agora, 201, 10, Agora, false)); // sem transmitir
        e.IniciarTransmissao();
        Assert.Throws<ErroDominio>(() => e.ConfirmarRecebimento("", Agora, 201, 10, Agora, false)); // sem protocolo
        e.ConfirmarRecebimento("P1", Agora, 201, 10, Agora, false);
        Assert.Equal(StatusEncaminhamento.Recebido, e.Status);
        Assert.Equal("P1", e.Protocolo);
    }

    [Fact]
    public void Falha_temporaria_agenda_retentativa_com_backoff_e_esgota()
    {
        var e = NovoEnc();
        for (var i = 1; i <= EncaminhamentoInstitucional.MaximoTentativas; i++)
        {
            e.IniciarTransmissao();
            e.RegistrarFalha(true, 503, "indisponível", 5, Agora, Agora);
            if (i < EncaminhamentoInstitucional.MaximoTentativas)
            {
                Assert.Equal(StatusEncaminhamento.FalhaTemporaria, e.Status);
                Assert.Equal(Agora + PoliticaRetentativa.Atraso(i), e.ProximaTentativaEm);
            }
        }
        Assert.Equal(StatusEncaminhamento.Esgotado, e.Status);
        e.SolicitarReenvio(Agora);
        Assert.Equal(StatusEncaminhamento.Pendente, e.Status);
    }

    [Fact]
    public void Rejeicao_nao_reagenda()
    {
        var e = NovoEnc();
        e.IniciarTransmissao();
        e.RegistrarFalha(false, 422, "inválida", 5, Agora, Agora);
        Assert.Equal(StatusEncaminhamento.Rejeitado, e.Status);
        Assert.False(e.ProntoParaEnvio(Agora.AddHours(1)));
    }

    [Fact]
    public void Transmissao_interrompida_e_recuperada_para_reenvio()
    {
        var e = NovoEnc();
        e.IniciarTransmissao();
        e.RecuperarTransmissaoInterrompida(Agora);
        Assert.True(e.ProntoParaEnvio(Agora));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 8)]
    [InlineData(6, 60)]
    [InlineData(20, 60)]
    public void Backoff_exponencial_com_teto(int tentativa, int segundos) =>
        Assert.Equal(TimeSpan.FromSeconds(segundos), PoliticaRetentativa.Atraso(tentativa));

    [Fact]
    public void Status_da_ocorrencia_reflete_apenas_confirmacoes_reais()
    {
        var o = Ocorrencia.Registrar(Guid.NewGuid(), null, "dispositivo-0000001", Agora, null, "2611606", null, CanalRecebimento.Internet, null, null, true, Agora);
        o.RecalcularStatus([StatusEncaminhamento.Pendente, StatusEncaminhamento.Pendente], Agora);
        Assert.Equal(StatusOcorrencia.RecebidaPeloServidor, o.Status);
        o.RecalcularStatus([StatusEncaminhamento.Recebido, StatusEncaminhamento.FalhaTemporaria], Agora);
        Assert.Equal(StatusOcorrencia.ConfirmadaParcialmente, o.Status);
        o.RecalcularStatus([StatusEncaminhamento.FalhaTemporaria, StatusEncaminhamento.Rejeitado], Agora);
        Assert.Equal(StatusOcorrencia.FalhaNoEncaminhamento, o.Status);
        o.RecalcularStatus([StatusEncaminhamento.Recebido, StatusEncaminhamento.Recebido], Agora);
        Assert.Equal(StatusOcorrencia.ConfirmadaPorTodas, o.Status);
        o.RecalcularStatus([], Agora);
        Assert.Equal(StatusOcorrencia.SemDestinoConfigurado, o.Status);
    }

    [Fact]
    public void Localizacao_respeita_limite_de_frequencia()
    {
        var p0 = new PosicaoDispositivo(-8.06, -34.87, 10, Agora, "gps");
        Assert.True(PoliticaAtualizacaoLocalizacao.DeveAceitar(null, p0));
        Assert.False(PoliticaAtualizacaoLocalizacao.DeveAceitar(p0, p0 with { LidaEm = Agora.AddSeconds(5), Latitude = -8.07 }));
        Assert.False(PoliticaAtualizacaoLocalizacao.DeveAceitar(p0, p0 with { LidaEm = Agora.AddSeconds(30) })); // parado, < 2 min
        Assert.True(PoliticaAtualizacaoLocalizacao.DeveAceitar(p0, p0 with { LidaEm = Agora.AddSeconds(30), Latitude = -8.0603 })); // ~33 m
        Assert.True(PoliticaAtualizacaoLocalizacao.DeveAceitar(p0, p0 with { LidaEm = Agora.AddMinutes(3) })); // batimento
        Assert.False(PoliticaAtualizacaoLocalizacao.DeveAceitar(p0, p0 with { LidaEm = Agora.AddMinutes(-1) })); // leitura antiga
    }

    [Fact]
    public void Ocorrencia_sem_localizacao_e_aceita_e_registrada_no_historico()
    {
        var o = Ocorrencia.Registrar(Guid.NewGuid(), null, "dispositivo-0000001", Agora, null, null, null, CanalRecebimento.Internet, null, null, true, Agora);
        Assert.Null(o.Ponto);
        Assert.Contains(o.Historico, h => h.Tipo == "localizacao");
    }

    [Fact]
    public void Data_do_dispositivo_no_futuro_e_limitada_ao_horario_do_servidor()
    {
        var o = Ocorrencia.Registrar(Guid.NewGuid(), null, "dispositivo-0000001", Agora.AddDays(2), null, null, null, CanalRecebimento.Internet, null, null, true, Agora);
        Assert.Equal(Agora, o.CriadaNoDispositivoEm);
    }

    [Fact]
    public void Complemento_invalido_e_rejeitado()
    {
        Assert.Throws<ErroDominio>(() => new ComplementoOcorrencia { QuantidadePessoas = 0 }.Validar());
        Assert.Throws<ErroDominio>(() => new ComplementoOcorrencia { IdadeSolicitante = 200 }.Validar());
        Assert.Throws<ErroDominio>(() => new ComplementoOcorrencia { Pessoas = [new PessoaEnvolvida(-1, false, false, false)] }.Validar());
    }

    [Fact]
    public void Complemento_mescla_sem_apagar_dados_anteriores()
    {
        var a = new ComplementoOcorrencia { QuantidadePessoas = 3, AguaSubindo = true };
        var b = a.Mesclar(new ComplementoOcorrencia { PessoasFeridas = 1 });
        Assert.Equal(3, b.QuantidadePessoas);
        Assert.True(b.AguaSubindo);
        Assert.Equal(1, b.PessoasFeridas);
    }
}
