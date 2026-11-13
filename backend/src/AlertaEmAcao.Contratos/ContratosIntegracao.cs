using System.Security.Cryptography;
using System.Text;

namespace AlertaEmAcao.Contratos;

/// <summary>Derivação da chave de autenticação de cada central a partir de um segredo mestre (HMAC-SHA256).</summary>
public static class ChavesCentrais
{
    public static string Derivar(string segredo, string codigoCentral)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes("central:" + codigoCentral));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool Comparar(string? a, string b) =>
        a is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}


/// <summary>
/// Contratos estáveis entre o Alerta em Ação e sistemas receptores (centrais).
/// Versionados: mudanças incompatíveis exigem nova versão (v2), nunca alteração in-place.
/// </summary>
public static class VersoesContrato
{
    public const string MensagemCentral = "alerta-em-acao/central/v1";
    public const string RetornoCentral = "alerta-em-acao/retorno-central/v1";
    public const string PacoteGateway = "alerta-em-acao/gateway/v1";
    public const string AmbienteDemonstracao = "DEMONSTRACAO";
}

public static class CabecalhosIntegracao
{
    public const string ChaveCentral = "X-Central-Chave";
    public const string ChaveIdempotencia = "Idempotency-Key";
    public const string Correlacao = "X-Correlation-Id";
    public const string ChaveRetorno = "X-Retorno-Chave";
}

public sealed record PessoasEnvolvidasV1(
    int? Quantidade,
    int? IdadeSolicitante,
    IReadOnlyList<int?> Idades,
    int? Feridos,
    int? Criancas,
    int? Idosos,
    bool? MobilidadeReduzida,
    bool? EquipamentoMedicoEssencial,
    bool? Sozinho);

public sealed record SinalizacaoV1(string Codigo, string Nivel, string Titulo, string Explicacao);

public sealed record MensagemCentralV1
{
    public required string Versao { get; init; }
    public required string TipoMensagem { get; init; }
    public required Guid IdentificadorOcorrencia { get; init; }
    public required Guid IdentificadorCorrelacao { get; init; }
    public required string MunicipioCodigo { get; init; }
    public required string MunicipioNome { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? PrecisaoMetros { get; init; }
    public required DateTimeOffset HorarioOcorrencia { get; init; }
    public DateTimeOffset? HorarioLocalizacao { get; init; }
    public required IReadOnlyList<string> TiposRisco { get; init; }
    public required PessoasEnvolvidasV1 PessoasEnvolvidas { get; init; }
    public required IReadOnlyList<string> Necessidades { get; init; }
    public string? Relato { get; init; }
    public string? ReferenciaLocal { get; init; }
    public IReadOnlyDictionary<string, string>? InformacoesComplementares { get; init; }
    public required IReadOnlyList<SinalizacaoV1> Sinalizacoes { get; init; }
    public string? CanalOrigem { get; init; }
    public required string Ambiente { get; init; }
}

public sealed record MensagemOfertaApoioV1
{
    public required string Versao { get; init; }
    public required string TipoMensagem { get; init; }
    public required Guid IdentificadorOferta { get; init; }
    public required Guid IdentificadorNecessidade { get; init; }
    public required Guid IdentificadorCorrelacao { get; init; }
    public required string MunicipioCodigo { get; init; }
    public required string TipoAjuda { get; init; }
    public required string Descricao { get; init; }
    public string? Disponibilidade { get; init; }
    public string? RegiaoDisponibilidade { get; init; }
    public string? ContatoAutorizado { get; init; }
    public required DateTimeOffset CriadaEm { get; init; }
    public required string Ambiente { get; init; }
}

/// <summary>Resposta da central APÓS persistir a mensagem.</summary>
public sealed record RespostaRecebimentoV1(
    string Protocolo,
    string CentralCodigo,
    string CentralNome,
    DateTimeOffset RecebidoEm,
    bool Duplicado,
    string Ambiente,
    string Aviso);

public sealed record ErroCentralV1(string Codigo, string Mensagem, IReadOnlyList<string>? Detalhes = null);

/// <summary>Retorno assíncrono da central ao Alerta em Ação (mudança de estado do atendimento demonstrativo).</summary>
public sealed record RetornoEstadoCentralV1(
    string Versao,
    string CentralCodigo,
    string Protocolo,
    Guid IdentificadorReferencia,
    string TipoMensagem,
    string Estado,
    string? Nota,
    DateTimeOffset Em,
    string Ambiente);

/// <summary>Pacote compacto retransmitido por proximidade (Nearby) ou gateway de rádio simulado.</summary>
public sealed record PacoteGatewayV1(
    string Versao,
    Guid Id,
    string DispositivoOrigem,
    string Receptor,
    int Saltos,
    DateTimeOffset CriadoEm,
    double? Latitude,
    double? Longitude,
    double? PrecisaoMetros,
    int? Pessoas,
    bool? AguaSubindo,
    bool? Feridos,
    string? Texto,
    string Canal);

public static class EventosTempoReal
{
    public const string Hub = "/hubs/operacao";
    public const string OcorrenciaAtualizada = "ocorrenciaAtualizada";
    public const string AvisoPublicado = "avisoPublicado";
    public const string Notificacao = "notificacao";
    public const string OfertaAtualizada = "ofertaAtualizada";
    public const string RecebimentoCentral = "recebimentoCentral";
}
