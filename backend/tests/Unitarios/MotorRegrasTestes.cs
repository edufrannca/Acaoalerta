using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Testes.Unitarios;

/// <summary>Casos fictícios revisados manualmente (ver docs/INTELIGENCIA_ARTIFICIAL.md).</summary>
public class MotorRegrasTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static Ocorrencia Caso(ComplementoOcorrencia c, bool comLocal = true, double? precisao = 15, DateTimeOffset? lidaEm = null) =>
        Ocorrencia.Registrar(Guid.NewGuid(), null, "dispositivo-teste-01", Agora,
            comLocal ? new PosicaoDispositivo(-8.06, -34.87, precisao, lidaEm ?? Agora, "gps") : null,
            comLocal ? "2611606" : null, c, CanalRecebimento.Internet, null, null, true, Agora);

    private static HashSet<string> Codigos(Ocorrencia o, ContextoTerritorialResumo? ctx = null) =>
        MotorRegrasUrgencia.Avaliar(o, ctx, Agora).Select(s => s.Codigo).ToHashSet();

    [Fact]
    public void Pessoa_ilhada_com_agua_subindo_e_ameaca_imediata()
    {
        var s = MotorRegrasUrgencia.Avaliar(Caso(new() { Ilhado = true, AguaSubindo = true, QuantidadePessoas = 2 }), null, Agora);
        var x = Assert.Single(s, i => i.Codigo == "AGUA_SUBINDO_COM_ILHAMENTO");
        Assert.Equal(NivelSinalizacao.IndicioAmeacaImediata, x.Nivel);
        Assert.NotEmpty(x.Evidencias);
    }

    [Fact]
    public void Agua_subindo_detectada_apenas_no_relato_livre()
    {
        var c = Codigos(Caso(new() { Descricao = "A água está subindo muito rápido aqui na rua", QuantidadePessoas = 1 }));
        Assert.Contains("AGUA_SUBINDO", c);
    }

    [Fact]
    public void Crianca_e_idoso_geram_necessidade_de_assistencia_mas_nao_ameaca_imediata()
    {
        var s = MotorRegrasUrgencia.Avaliar(Caso(new() { QuantidadePessoas = 3, Pessoas = [new(5, false, false, false), new(78, false, false, false), new(40, false, false, false)] }), null, Agora);
        Assert.Contains(s, x => x.Codigo == "NECESSIDADE_ASSISTENCIA" && x.Nivel == NivelSinalizacao.Atencao);
        // Idade, isoladamente, nunca eleva a "ameaça imediata".
        Assert.DoesNotContain(s, x => x.Nivel == NivelSinalizacao.IndicioAmeacaImediata);
    }

    [Fact]
    public void Pessoa_ferida_e_ameaca_imediata()
    {
        Assert.Contains("PESSOA_FERIDA", Codigos(Caso(new() { PessoasFeridas = 1 })));
        Assert.Contains("PESSOA_FERIDA", Codigos(Caso(new() { Descricao = "meu pai está sangrando na cabeça" })));
    }

    [Fact]
    public void Deslizamento_relatado_e_risco_geologico()
    {
        var c = Codigos(Caso(new() { Descricao = "Houve um deslizamento de terra atrás da casa" }));
        Assert.Contains("RISCO_GEOLOGICO", c);
    }

    [Fact]
    public void Barreira_com_sinais_de_instabilidade()
    {
        var s = MotorRegrasUrgencia.Avaliar(Caso(new() { BarreiraComSinaisInstabilidade = true, Descricao = "Apareceu uma rachadura nova na barreira" }), null, Agora);
        Assert.Contains(s, x => x.Codigo == "RISCO_GEOLOGICO");
    }

    [Fact]
    public void Desabamento_e_ameaca_imediata()
    {
        var s = MotorRegrasUrgencia.Avaliar(Caso(new() { Descricao = "A casa do vizinho desabou" }), null, Agora);
        Assert.Contains(s, x => x.Codigo == "DESABAMENTO" && x.Nivel == NivelSinalizacao.IndicioAmeacaImediata);
    }

    [Fact]
    public void Rua_bloqueada()
    {
        Assert.Contains("ACESSO_BLOQUEADO", Codigos(Caso(new() { Descricao = "uma árvore caiu e a rua bloqueada" })));
        Assert.Contains("ACESSO_BLOQUEADO", Codigos(Caso(new() { AcessoBloqueado = true })));
    }

    [Fact]
    public void Localizacao_desconhecida_e_imprecisa_e_antiga()
    {
        Assert.Contains("LOCALIZACAO_DESCONHECIDA", Codigos(Caso(new(), comLocal: false)));
        Assert.Contains("LOCALIZACAO_IMPRECISA", Codigos(Caso(new(), precisao: 2000)));
        Assert.Contains("LOCALIZACAO_ANTIGA", Codigos(Caso(new(), lidaEm: Agora.AddHours(-2))));
    }

    [Fact]
    public void Relato_incompleto()
    {
        Assert.Contains("RELATO_INCOMPLETO", Codigos(Caso(new())));
    }

    [Fact]
    public void Informacoes_contraditorias()
    {
        var s = MotorRegrasUrgencia.Avaliar(Caso(new() { Sozinho = true, QuantidadePessoas = 4, PessoasFeridas = 6 }), null, Agora);
        var x = Assert.Single(s, i => i.Codigo == "INFORMACOES_CONTRADITORIAS");
        Assert.Equal(2, x.Evidencias.Count);
    }

    [Fact]
    public void Dados_meteorologicos_antigos_e_contexto_territorial()
    {
        var ctx = new ContextoTerritorialResumo(true, "Aviso X", true, "Área de risco nível 3", 40, Agora.AddHours(-6), true, 2);
        var c = Codigos(Caso(new() { QuantidadePessoas = 1, Descricao = "chuva forte" }), ctx);
        Assert.Contains("DADOS_METEOROLOGICOS_DESATUALIZADOS", c);
        Assert.Contains("AVISO_OFICIAL_VIGENTE", c);
        Assert.Contains("SUSCETIBILIDADE_CONHECIDA", c);
        Assert.Contains("OCORRENCIAS_PROXIMAS", c);
    }

    [Fact]
    public void Normalizacao_ignora_acentos_e_caixa() =>
        Assert.Equal("agua esta subindo", DetectorIndicios.Normalizar("ÁGUA Está Subindo"));

    [Fact]
    public void Verbo_beber_nao_e_confundido_com_bebe()
    {
        var ind = DetectorIndicios.Detectar(new ComplementoOcorrencia { Descricao = "ele bebe água da chuva" });
        Assert.DoesNotContain(ind, i => i.Tipo == Indicio.Vulnerabilidade);
    }

    [Fact]
    public void Toda_sinalizacao_de_ameaca_tem_evidencia()
    {
        var s = MotorRegrasUrgencia.Avaliar(Caso(new() { Ilhado = true, Correnteza = true, PessoasFeridas = 1, Desmoronamento = true }), null, Agora);
        Assert.All(s.Where(x => x.Nivel == NivelSinalizacao.IndicioAmeacaImediata), x => Assert.NotEmpty(x.Evidencias));
    }
}
