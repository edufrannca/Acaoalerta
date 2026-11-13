using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Localizacao;

namespace AlertaEmAcao.Modulos.Ocorrencias;

public enum CanalRecebimento
{
    Internet,
    /// <summary>Retransmitida por outro aparelho via Nearby Connections (prova de conceito).</summary>
    ProximidadeNearby,
    /// <summary>Recebida de um gateway de rádio SIMULADO (demonstração LoRa).</summary>
    GatewaySimulado,
    /// <summary>Lida por câmera (código QR) num aparelho próximo com internet, que a encaminhou ao servidor.</summary>
    ProximidadeQr,
}

/// <summary>
/// O que o solicitante autorizou mostrar a cidadãos desconhecidos. Padrão: nada.
/// Nunca inclui nome, telefone, dados médicos individuais, texto livre ou coordenadas precisas.
/// </summary>
public sealed record CompartilhamentoComunitario(bool Autorizado = false, bool IncluirPessoas = false, bool IncluirNecessidades = false, DateTimeOffset? AtualizadoEm = null)
{
    public CompartilhamentoComunitario() : this(false, false, false, null) { }
    public static readonly CompartilhamentoComunitario Nenhum = new();
}

public static class NecessidadesDeclaradas
{
    public static readonly IReadOnlyDictionary<string, string> Rotulos = new Dictionary<string, string>
    {
        ["agua"] = "Água potável",
        ["alimentos"] = "Alimentos",
        ["abrigo"] = "Abrigo",
        ["transporte_assistido"] = "Transporte assistido",
        ["itens_higiene"] = "Itens de higiene",
        ["roupas"] = "Roupas e cobertas",
        ["outros"] = "Outros recursos",
    };
}

/// <summary>Estado de transmissão/atendimento. Nunca avança sem evento comprovado.</summary>
public enum StatusOcorrencia
{
    /// <summary>Persistida no servidor; encaminhamentos ainda não confirmados.</summary>
    RecebidaPeloServidor,
    /// <summary>Ao menos uma central confirmou o recebimento técnico; outras pendentes.</summary>
    ConfirmadaParcialmente,
    /// <summary>Todas as centrais pertinentes confirmaram o recebimento técnico.</summary>
    ConfirmadaPorTodas,
    /// <summary>Nenhuma central confirmou e há falhas registradas (retentativas em curso).</summary>
    FalhaNoEncaminhamento,
    /// <summary>Sem destino configurado para o município/tipo.</summary>
    SemDestinoConfigurado,
    EmAtendimentoSimulado,
    Encerrada,
}

public enum VerificacaoOcorrencia { NaoVerificada, VerificadaPorOperador, DescartadaPorOperador }

public sealed record PessoaEnvolvida(int? Idade, bool Ferida, bool MobilidadeReduzida, bool DependeAssistencia);

/// <summary>Dados complementares — todos opcionais. Nenhum deles impede o pedido inicial.</summary>
public sealed record ComplementoOcorrencia
{
    public int? IdadeSolicitante { get; init; }
    public int? QuantidadePessoas { get; init; }
    public List<PessoaEnvolvida>? Pessoas { get; init; }
    public int? PessoasFeridas { get; init; }
    public int? Criancas { get; init; }
    public int? Idosos { get; init; }
    public bool? MobilidadeReduzida { get; init; }
    public bool? EquipamentoMedicoEssencial { get; init; }
    public bool? Sozinho { get; init; }
    public bool? Ilhado { get; init; }
    public bool? AguaSubindo { get; init; }
    public bool? Correnteza { get; init; }
    public bool? RiscoDeslizamento { get; init; }
    public bool? Desmoronamento { get; init; }
    public bool? BarreiraComSinaisInstabilidade { get; init; }
    public bool? AcessoBloqueado { get; init; }
    public bool? RiscoSegurancaPublica { get; init; }
    public string? OutrosRiscos { get; init; }
    public string? Descricao { get; init; }
    public string? ReferenciaLocal { get; init; }
    /// <summary>Necessidades declaradas (códigos de <see cref="NecessidadesDeclaradas"/>).</summary>
    public List<string>? Necessidades { get; init; }

    public ComplementoOcorrencia Mesclar(ComplementoOcorrencia novo) => this with
    {
        IdadeSolicitante = novo.IdadeSolicitante ?? IdadeSolicitante,
        QuantidadePessoas = novo.QuantidadePessoas ?? QuantidadePessoas,
        Pessoas = novo.Pessoas ?? Pessoas,
        PessoasFeridas = novo.PessoasFeridas ?? PessoasFeridas,
        Criancas = novo.Criancas ?? Criancas,
        Idosos = novo.Idosos ?? Idosos,
        MobilidadeReduzida = novo.MobilidadeReduzida ?? MobilidadeReduzida,
        EquipamentoMedicoEssencial = novo.EquipamentoMedicoEssencial ?? EquipamentoMedicoEssencial,
        Sozinho = novo.Sozinho ?? Sozinho,
        Ilhado = novo.Ilhado ?? Ilhado,
        AguaSubindo = novo.AguaSubindo ?? AguaSubindo,
        Correnteza = novo.Correnteza ?? Correnteza,
        RiscoDeslizamento = novo.RiscoDeslizamento ?? RiscoDeslizamento,
        Desmoronamento = novo.Desmoronamento ?? Desmoronamento,
        BarreiraComSinaisInstabilidade = novo.BarreiraComSinaisInstabilidade ?? BarreiraComSinaisInstabilidade,
        AcessoBloqueado = novo.AcessoBloqueado ?? AcessoBloqueado,
        RiscoSegurancaPublica = novo.RiscoSegurancaPublica ?? RiscoSegurancaPublica,
        OutrosRiscos = Limitar(novo.OutrosRiscos ?? OutrosRiscos, 500),
        Descricao = Limitar(novo.Descricao ?? Descricao, 2000),
        ReferenciaLocal = Limitar(novo.ReferenciaLocal ?? ReferenciaLocal, 300),
        Necessidades = novo.Necessidades ?? Necessidades,
    };

    public void Validar()
    {
        if (IdadeSolicitante is < 0 or > 125) throw new ErroDominio("idade_invalida", "Idade fora do intervalo.");
        if (QuantidadePessoas is < 1 or > 500) throw new ErroDominio("pessoas_invalido", "Quantidade de pessoas fora do intervalo (1–500).");
        foreach (var n in new[] { PessoasFeridas, Criancas, Idosos })
            if (n is < 0 or > 500) throw new ErroDominio("contagem_invalida", "Contagens devem estar entre 0 e 500.");
        if (Pessoas?.Count > 100) throw new ErroDominio("pessoas_demais", "Máximo de 100 pessoas detalhadas.");
        if (Pessoas?.Any(p => p.Idade is < 0 or > 125) == true) throw new ErroDominio("idade_invalida", "Idade fora do intervalo.");
        if (Necessidades is { } ns && (ns.Count > 10 || ns.Any(n => !NecessidadesDeclaradas.Rotulos.ContainsKey(n))))
            throw new ErroDominio("necessidade_invalida", "Necessidade não reconhecida.");
    }

    private static string? Limitar(string? s, int max) => s is null ? null : s.Trim()[..Math.Min(max, s.Trim().Length)];
}

public sealed record EventoHistorico(DateTimeOffset Em, string Tipo, string Descricao, string? Ator = null);

/// <summary>Pedido de socorro. Id gerado no dispositivo para idempotência offline.</summary>
public sealed class Ocorrencia
{
    public Guid Id { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string DispositivoId { get; private set; } = default!;
    public DateTimeOffset CriadaNoDispositivoEm { get; private set; }
    public DateTimeOffset RecebidaNoServidorEm { get; private set; }
    public CanalRecebimento Canal { get; private set; }
    public string? RetransmitidaPor { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? PrecisaoMetros { get; private set; }
    public DateTimeOffset? LocalizacaoLidaEm { get; private set; }
    public string? MunicipioCodigo { get; private set; }
    public ComplementoOcorrencia Complemento { get; private set; } = new();
    public StatusOcorrencia Status { get; private set; }
    public VerificacaoOcorrencia Verificacao { get; private set; }
    public Guid? AvisoRelacionadoId { get; private set; }
    public List<EventoHistorico> Historico { get; private set; } = [];
    public List<PosicaoDispositivo> TrilhaLocalizacao { get; private set; } = [];
    public bool Demonstracao { get; private set; }
    public CompartilhamentoComunitario Compartilhamento { get; private set; } = CompartilhamentoComunitario.Nenhum;
    /// <summary>Posição do aparelho que retransmitiu (gateway). NUNCA substitui a posição do emissor.</summary>
    public List<PosicaoDispositivo> PosicoesRetransmissor { get; private set; } = [];
    public PosicaoDispositivo? PosicaoRetransmissor => PosicoesRetransmissor.LastOrDefault();
    public DateTimeOffset AtualizadaEm { get; private set; }
    public uint Versao { get; private set; }

    private Ocorrencia() { }

    public static Ocorrencia Registrar(Guid id, Guid? usuarioId, string dispositivoId, DateTimeOffset criadaNoDispositivo,
        PosicaoDispositivo? posicao, string? municipioCodigo, ComplementoOcorrencia? complemento, CanalRecebimento canal,
        string? retransmitidaPor, Guid? avisoId, bool demonstracao, DateTimeOffset agora)
    {
        if (id == Guid.Empty) throw new ErroDominio("id_invalido", "Identificador da ocorrência é obrigatório.");
        if (string.IsNullOrWhiteSpace(dispositivoId) || dispositivoId.Length > 100)
            throw new ErroDominio("dispositivo_invalido", "Identificador de dispositivo inválido.");
        // Relógio do aparelho pode estar errado; não aceitamos datas no futuro distante.
        if (criadaNoDispositivo > agora.AddMinutes(10)) criadaNoDispositivo = agora;
        complemento ??= new ComplementoOcorrencia();
        complemento.Validar();

        var o = new Ocorrencia
        {
            Id = id, UsuarioId = usuarioId, DispositivoId = dispositivoId.Trim(), CriadaNoDispositivoEm = criadaNoDispositivo,
            RecebidaNoServidorEm = agora, Canal = canal, RetransmitidaPor = retransmitidaPor, MunicipioCodigo = municipioCodigo,
            Complemento = complemento, Status = StatusOcorrencia.RecebidaPeloServidor, AvisoRelacionadoId = avisoId,
            Demonstracao = demonstracao, AtualizadaEm = agora,
        };
        if (posicao is not null) o.AplicarPosicao(posicao);
        o.Registrar(agora, "recebida", canal switch
        {
            CanalRecebimento.Internet => "Pedido recebido e armazenado pelo servidor.",
            CanalRecebimento.ProximidadeNearby => $"Pedido recebido via retransmissão por proximidade (aparelho {retransmitidaPor}).",
            _ => $"Pedido recebido via gateway SIMULADO {retransmitidaPor}.",
        });
        if (posicao is null) o.Registrar(agora, "localizacao", "Pedido sem localização. Solicitar referência ao cidadão.");
        return o;
    }

    /// <summary>
    /// Pseudônimo estável do aparelho emissor usado quando o pedido chega por outro aparelho (QR ou Nearby importado da PWA):
    /// quem retransmite não recebe o identificador real, mas o próprio emissor continua reconhecido quando sincronizar.
    /// </summary>
    public static string PseudonimoEmissor(string dispositivoId)
    {
        var h = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("alerta-em-acao/emissor/v1:" + dispositivoId.Trim()));
        return "p:" + Convert.ToBase64String(h, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public bool PertenceAoDispositivo(string? dispositivoId) =>
        !string.IsNullOrWhiteSpace(dispositivoId) && (DispositivoId == dispositivoId || DispositivoId == PseudonimoEmissor(dispositivoId));

    public void DefinirCompartilhamento(CompartilhamentoComunitario c, DateTimeOffset agora)
    {
        var novo = c.Autorizado ? c with { AtualizadoEm = agora } : CompartilhamentoComunitario.Nenhum with { AtualizadoEm = agora };
        if (novo with { AtualizadoEm = null } == Compartilhamento with { AtualizadoEm = null }) return;
        Compartilhamento = novo;
        Registrar(agora, "compartilhamento", novo.Autorizado
            ? $"Solicitante autorizou resumo comunitário (pessoas: {(novo.IncluirPessoas ? "sim" : "não")}, necessidades: {(novo.IncluirNecessidades ? "sim" : "não")})."
            : "Solicitante não compartilha o pedido com a comunidade.");
    }

    public void RegistrarPosicaoRetransmissor(PosicaoDispositivo p, string receptor, DateTimeOffset agora)
    {
        PosicoesRetransmissor.Add(p);
        if (PosicoesRetransmissor.Count > 10) PosicoesRetransmissor.RemoveAt(0);
        Registrar(agora, "posicao_retransmissor", $"Posição aproximada do aparelho retransmissor {receptor} registrada separadamente (não é a posição do solicitante).");
    }

    public PontoGeo? Ponto => Latitude is not null && Longitude is not null ? new PontoGeo(Latitude.Value, Longitude.Value) : null;

    public void Complementar(ComplementoOcorrencia novo, DateTimeOffset agora)
    {
        var mesclado = Complemento.Mesclar(novo);
        mesclado.Validar();
        Complemento = mesclado;
        Registrar(agora, "complemento", "Informações complementares recebidas.");
    }

    /// <summary>Retorna true se a posição foi aceita (respeitando a política de frequência).</summary>
    public bool AtualizarLocalizacao(PosicaoDispositivo nova, string? municipioCodigo, DateTimeOffset agora)
    {
        var ultima = TrilhaLocalizacao.LastOrDefault();
        if (!PoliticaAtualizacaoLocalizacao.DeveAceitar(ultima, nova)) return false;
        AplicarPosicao(nova);
        if (municipioCodigo is not null && municipioCodigo != MunicipioCodigo)
        {
            Registrar(agora, "municipio", $"Município da ocorrência alterado de {MunicipioCodigo ?? "desconhecido"} para {municipioCodigo}.");
            MunicipioCodigo = municipioCodigo;
        }
        AtualizadaEm = agora;
        return true;
    }

    private void AplicarPosicao(PosicaoDispositivo p)
    {
        Latitude = p.Latitude; Longitude = p.Longitude; PrecisaoMetros = p.PrecisaoMetros; LocalizacaoLidaEm = p.LidaEm;
        TrilhaLocalizacao.Add(p);
        if (TrilhaLocalizacao.Count > 200) TrilhaLocalizacao.RemoveAt(0);
    }

    public void DefinirMunicipio(string? codigo) => MunicipioCodigo = codigo;

    /// <summary>Status é sempre recalculado a partir dos encaminhamentos efetivamente confirmados.</summary>
    public void RecalcularStatus(IReadOnlyCollection<Encaminhamento.StatusEncaminhamento> encaminhamentos, DateTimeOffset agora)
    {
        if (Status is StatusOcorrencia.EmAtendimentoSimulado or StatusOcorrencia.Encerrada) return;
        StatusOcorrencia novo;
        if (encaminhamentos.Count == 0) novo = StatusOcorrencia.SemDestinoConfigurado;
        else if (encaminhamentos.All(s => s == Encaminhamento.StatusEncaminhamento.Recebido)) novo = StatusOcorrencia.ConfirmadaPorTodas;
        else if (encaminhamentos.Any(s => s == Encaminhamento.StatusEncaminhamento.Recebido)) novo = StatusOcorrencia.ConfirmadaParcialmente;
        else if (encaminhamentos.Any(s => s is Encaminhamento.StatusEncaminhamento.FalhaTemporaria or Encaminhamento.StatusEncaminhamento.Rejeitado or Encaminhamento.StatusEncaminhamento.Esgotado))
            novo = StatusOcorrencia.FalhaNoEncaminhamento;
        else novo = StatusOcorrencia.RecebidaPeloServidor;
        if (novo != Status) { Status = novo; AtualizadaEm = agora; }
    }

    public void MarcarEmAtendimentoSimulado(string ator, string descricao, DateTimeOffset agora)
    {
        Status = StatusOcorrencia.EmAtendimentoSimulado;
        Registrar(agora, "atendimento_simulado", descricao, ator);
    }

    public void Encerrar(string ator, string descricao, DateTimeOffset agora)
    {
        Status = StatusOcorrencia.Encerrada;
        Registrar(agora, "encerrada", descricao, ator);
    }

    public void Verificar(VerificacaoOcorrencia v, string ator, string? nota, DateTimeOffset agora)
    {
        Verificacao = v;
        Registrar(agora, "verificacao", $"{v}{(nota is null ? "" : ": " + nota)}", ator);
    }

    public void Registrar(DateTimeOffset em, string tipo, string descricao, string? ator = null)
    {
        Historico.Add(new EventoHistorico(em, tipo, descricao, ator));
        AtualizadaEm = em;
    }
}

public enum StatusSituacao { Seguro, PrecisaAjuda }

/// <summary>Autodeclaração "Estou seguro". NÃO certifica a segurança da residência ou da região.</summary>
public sealed class SituacaoDeclarada
{
    public Guid Id { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string DispositivoId { get; private set; } = default!;
    public StatusSituacao Status { get; private set; }
    public DateTimeOffset DeclaradaNoDispositivoEm { get; private set; }
    public DateTimeOffset RecebidaNoServidorEm { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public string? MunicipioCodigo { get; private set; }
    public Guid? AvisoId { get; private set; }
    public bool DesejaAjudar { get; private set; }
    public string? Mensagem { get; private set; }

    private SituacaoDeclarada() { }

    public static SituacaoDeclarada Declarar(Guid id, Guid? usuarioId, string dispositivoId, StatusSituacao status,
        DateTimeOffset declaradaEm, PontoGeo? ponto, string? municipio, Guid? avisoId, bool desejaAjudar, string? mensagem, DateTimeOffset agora)
    {
        if (id == Guid.Empty) throw new ErroDominio("id_invalido", "Identificador obrigatório.");
        if (string.IsNullOrWhiteSpace(dispositivoId)) throw new ErroDominio("dispositivo_invalido", "Dispositivo obrigatório.");
        return new SituacaoDeclarada
        {
            Id = id, UsuarioId = usuarioId, DispositivoId = dispositivoId, Status = status,
            DeclaradaNoDispositivoEm = declaradaEm > agora.AddMinutes(10) ? agora : declaradaEm, RecebidaNoServidorEm = agora,
            Latitude = ponto?.Latitude, Longitude = ponto?.Longitude, MunicipioCodigo = municipio, AvisoId = avisoId,
            DesejaAjudar = desejaAjudar, Mensagem = mensagem?.Trim()[..Math.Min(300, mensagem.Trim().Length)],
        };
    }
}
