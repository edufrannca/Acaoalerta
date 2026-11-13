using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Modulos.Encaminhamento;

public enum TipoInstituicao { DefesaCivilMunicipal, CorpoBombeiros, Samu, PoliciaMilitar, CentroIntegradoOperacoes, OrganizacaoApoio }

public enum CanalIntegracao
{
    /// <summary>HTTP para central receptora SIMULADA (ambiente de demonstração).</summary>
    HttpCentralSimulada,
    /// <summary>Reservado para integração institucional homologada (não implementado sem autorização).</summary>
    HttpInstitucionalHomologado,
}

public enum EstadoIntegracao { Simulada, AguardandoHomologacao, Desativada }

public sealed class Instituicao
{
    public Guid Id { get; private set; }
    public string Codigo { get; private set; } = default!;
    public string Nome { get; private set; } = default!;
    public TipoInstituicao Tipo { get; private set; }
    /// <summary>null = jurisdição estadual/metropolitana (atende todos os municípios da RMR).</summary>
    public string? MunicipioCodigo { get; private set; }
    public string Jurisdicao { get; private set; } = default!;
    public CanalIntegracao Canal { get; private set; }
    public EstadoIntegracao Estado { get; private set; }
    public string EndpointUrl { get; private set; } = default!;
    public bool Habilitada { get; private set; }
    public int TimeoutSegundos { get; private set; }

    private Instituicao() { }

    public static Instituicao Criar(string codigo, string nome, TipoInstituicao tipo, string? municipio, string jurisdicao,
        CanalIntegracao canal, EstadoIntegracao estado, string endpointUrl, int timeoutSegundos = 10)
    {
        if (municipio is not null && !MunicipiosRmr.Pertence(municipio)) throw new ErroDominio("municipio_invalido", "Município fora da RMR.");
        if (canal == CanalIntegracao.HttpCentralSimulada && !nome.Contains("SIMULAD", StringComparison.OrdinalIgnoreCase))
            throw new ErroDominio("nome_simulado", "Centrais simuladas devem ter 'SIMULADA' no nome para não serem confundidas com órgãos reais.");
        return new Instituicao
        {
            Id = Guid.CreateVersion7(), Codigo = codigo, Nome = nome, Tipo = tipo, MunicipioCodigo = municipio, Jurisdicao = jurisdicao,
            Canal = canal, Estado = estado, EndpointUrl = endpointUrl, Habilitada = true, TimeoutSegundos = timeoutSegundos,
        };
    }

    public void AlterarEndpoint(string url) => EndpointUrl = url;
    public void Habilitar(bool h) => Habilitada = h;
}

public enum Gatilho { Sempre, RiscoHidrologico, RiscoGeologico, PessoaFerida, NecessidadeMedica, SegurancaPublica, PessoaIsolada }

public sealed class RegraEncaminhamento
{
    public Guid Id { get; private set; }
    /// <summary>null = vale para todos os municípios da RMR.</summary>
    public string? MunicipioCodigo { get; private set; }
    public TipoInstituicao TipoInstituicao { get; private set; }
    public Gatilho Gatilho { get; private set; }
    public bool Ativa { get; private set; }
    public string Descricao { get; private set; } = default!;

    private RegraEncaminhamento() { }

    public static RegraEncaminhamento Criar(string? municipio, TipoInstituicao tipo, Gatilho gatilho, string descricao) =>
        new() { Id = Guid.CreateVersion7(), MunicipioCodigo = municipio, TipoInstituicao = tipo, Gatilho = gatilho, Ativa = true, Descricao = descricao };

    public void Ativar(bool a) => Ativa = a;
}

public sealed record DestinoDeterminado(Instituicao Instituicao, string Justificativa);

/// <summary>
/// Determina destinos de uma ocorrência a partir do município e dos indícios detectados,
/// consultando regras configuráveis. Não envia tudo para todos: cada destino tem justificativa.
/// </summary>
public static class RoteadorEncaminhamento
{
    public static IReadOnlyList<DestinoDeterminado> Determinar(string? municipioCodigo, IReadOnlyCollection<Indicio> indicios,
        IEnumerable<RegraEncaminhamento> regras, IEnumerable<Instituicao> instituicoes)
    {
        if (municipioCodigo is null || !MunicipiosRmr.Pertence(municipioCodigo)) return [];
        var insts = instituicoes.Where(i => i.Habilitada && i.Estado != EstadoIntegracao.Desativada && i.Tipo != TipoInstituicao.OrganizacaoApoio).ToList();
        var resultado = new Dictionary<Guid, DestinoDeterminado>();

        foreach (var regra in regras.Where(r => r.Ativa && (r.MunicipioCodigo is null || r.MunicipioCodigo == municipioCodigo)))
        {
            if (!Dispara(regra.Gatilho, indicios)) continue;
            // Preferência: instituição do próprio município; senão, de jurisdição metropolitana/estadual.
            var candidata = insts.FirstOrDefault(i => i.Tipo == regra.TipoInstituicao && i.MunicipioCodigo == municipioCodigo)
                            ?? insts.FirstOrDefault(i => i.Tipo == regra.TipoInstituicao && i.MunicipioCodigo is null);
            if (candidata is null || resultado.ContainsKey(candidata.Id)) continue;
            resultado[candidata.Id] = new DestinoDeterminado(candidata, $"Regra: {regra.Descricao} (gatilho {regra.Gatilho})");
        }
        return resultado.Values.ToList();
    }

    private static bool Dispara(Gatilho g, IReadOnlyCollection<Indicio> i) => g switch
    {
        Gatilho.Sempre => true,
        Gatilho.RiscoHidrologico => i.Contains(Indicio.RiscoHidrologico),
        Gatilho.RiscoGeologico => i.Contains(Indicio.RiscoGeologico),
        Gatilho.PessoaFerida => i.Contains(Indicio.PessoaFerida),
        Gatilho.NecessidadeMedica => i.Contains(Indicio.NecessidadeMedica),
        Gatilho.SegurancaPublica => i.Contains(Indicio.SegurancaPublica),
        Gatilho.PessoaIsolada => i.Contains(Indicio.PessoaIsolada),
        _ => false,
    };
}

public enum TipoMensagem { Ocorrencia, AtualizacaoOcorrencia, OfertaApoio }

public enum StatusEncaminhamento
{
    Pendente,
    /// <summary>Requisição HTTP em andamento.</summary>
    Transmitindo,
    /// <summary>A central respondeu 2xx com protocolo após persistir. Único estado de "recebido".</summary>
    Recebido,
    /// <summary>Falha temporária (timeout, 5xx, rede). Nova tentativa agendada.</summary>
    FalhaTemporaria,
    /// <summary>Central rejeitou (4xx). Requer intervenção; reenvio manual possível.</summary>
    Rejeitado,
    /// <summary>Limite de tentativas atingido.</summary>
    Esgotado,
}

public sealed record TentativaEnvio(int Numero, DateTimeOffset IniciadaEm, long DuracaoMs, int? HttpStatus, string Resultado, string? Detalhe);

public sealed class EncaminhamentoInstitucional
{
    public Guid Id { get; private set; }
    public TipoMensagem TipoMensagem { get; private set; }
    public Guid ReferenciaId { get; private set; }
    public Guid InstituicaoId { get; private set; }
    public string InstituicaoCodigo { get; private set; } = default!;
    public string Justificativa { get; private set; } = default!;
    public StatusEncaminhamento Status { get; private set; }
    public int Tentativas { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset ProximaTentativaEm { get; private set; }
    public string? Protocolo { get; private set; }
    public DateTimeOffset? ConfirmadoEm { get; private set; }
    public string? UltimoErro { get; private set; }
    public string? EstadoNaCentral { get; private set; }
    public List<TentativaEnvio> HistoricoTentativas { get; private set; } = [];
    public uint Versao { get; private set; }

    public const int MaximoTentativas = 12;

    private EncaminhamentoInstitucional() { }

    public static EncaminhamentoInstitucional Criar(TipoMensagem tipo, Guid referencia, Instituicao inst, string justificativa, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(), TipoMensagem = tipo, ReferenciaId = referencia, InstituicaoId = inst.Id, InstituicaoCodigo = inst.Codigo,
        Justificativa = justificativa, Status = StatusEncaminhamento.Pendente, CriadoEm = agora, ProximaTentativaEm = agora,
    };

    public bool ProntoParaEnvio(DateTimeOffset agora) =>
        Status is StatusEncaminhamento.Pendente or StatusEncaminhamento.FalhaTemporaria && ProximaTentativaEm <= agora;

    public void IniciarTransmissao()
    {
        if (Status is not (StatusEncaminhamento.Pendente or StatusEncaminhamento.FalhaTemporaria))
            throw new ErroDominio("estado_invalido", $"Não é possível transmitir no estado {Status}.");
        Status = StatusEncaminhamento.Transmitindo;
        Tentativas++;
    }

    /// <summary>Confirmação só é aceita com protocolo emitido pela central.</summary>
    public void ConfirmarRecebimento(string protocolo, DateTimeOffset confirmadoEm, int httpStatus, long duracaoMs, DateTimeOffset iniciadaEm, bool duplicado)
    {
        if (string.IsNullOrWhiteSpace(protocolo)) throw new ErroDominio("protocolo_ausente", "Confirmação sem protocolo não é aceita.");
        if (Status != StatusEncaminhamento.Transmitindo) throw new ErroDominio("estado_invalido", "Confirmação recebida fora de uma transmissão.");
        Status = StatusEncaminhamento.Recebido;
        Protocolo = protocolo;
        ConfirmadoEm = confirmadoEm;
        UltimoErro = null;
        EstadoNaCentral = "Recebido";
        HistoricoTentativas.Add(new(Tentativas, iniciadaEm, duracaoMs, httpStatus, duplicado ? "recebido_duplicado_reconhecido" : "recebido", $"Protocolo {protocolo}"));
    }

    public void RegistrarFalha(bool temporaria, int? httpStatus, string erro, long duracaoMs, DateTimeOffset iniciadaEm, DateTimeOffset agora)
    {
        UltimoErro = erro.Length > 500 ? erro[..500] : erro;
        HistoricoTentativas.Add(new(Tentativas, iniciadaEm, duracaoMs, httpStatus, temporaria ? "falha_temporaria" : "rejeitado", UltimoErro));
        if (HistoricoTentativas.Count > 50) HistoricoTentativas.RemoveAt(0);
        if (!temporaria) { Status = StatusEncaminhamento.Rejeitado; return; }
        if (Tentativas >= MaximoTentativas) { Status = StatusEncaminhamento.Esgotado; return; }
        Status = StatusEncaminhamento.FalhaTemporaria;
        ProximaTentativaEm = agora + PoliticaRetentativa.Atraso(Tentativas);
    }

    /// <summary>Reenvio manual (ex.: central reativada). Mantém a mesma chave de idempotência.</summary>
    public void SolicitarReenvio(DateTimeOffset agora)
    {
        if (Status == StatusEncaminhamento.Recebido) throw new ErroDominio("ja_recebido", "Encaminhamento já confirmado.");
        if (Status == StatusEncaminhamento.Transmitindo) return;
        Status = StatusEncaminhamento.Pendente;
        ProximaTentativaEm = agora;
        if (Tentativas >= MaximoTentativas) Tentativas = MaximoTentativas - 3;
    }

    /// <summary>Recupera encaminhamentos presos em "Transmitindo" (ex.: processo reiniciado).</summary>
    public void RecuperarTransmissaoInterrompida(DateTimeOffset agora)
    {
        if (Status != StatusEncaminhamento.Transmitindo) return;
        Status = StatusEncaminhamento.FalhaTemporaria;
        UltimoErro = "Transmissão interrompida (processo reiniciado). Reenvio com a mesma chave de idempotência.";
        ProximaTentativaEm = agora;
    }

    public void AtualizarEstadoNaCentral(string estado) => EstadoNaCentral = estado;
}

public static class PoliticaRetentativa
{
    /// <summary>Backoff exponencial com teto: 2s, 4s, 8s, 16s, 30s, 60s, 60s…</summary>
    public static TimeSpan Atraso(int tentativa) =>
        TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, Math.Clamp(tentativa, 1, 6))));
}

public sealed record ResultadoEnvioCentral(bool Sucesso, bool FalhaTemporaria, int? HttpStatus, string? Protocolo, DateTimeOffset? RecebidoEm, bool Duplicado, string? Erro, long DuracaoMs);

public interface IClienteCentral
{
    Task<ResultadoEnvioCentral> EnviarAsync(Instituicao destino, object mensagem, string chaveIdempotencia, CancellationToken ct);
}
