using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlertaEmAcao.Contratos;

/// <summary>
/// Protocolo único de comunicação por proximidade (Nearby Connections) entre Android, iOS e o gateway.
/// Especificação: docs/PROTOCOLO_PROXIMIDADE.md. Vetores de teste compartilhados: docs/protocolo/vetores-v2.json
/// (os mesmos vetores são verificados em C#, Kotlin e Swift).
/// Codificação: JSON UTF-8, nomes em camelCase, campos opcionais AUSENTES ou null (equivalentes),
/// horários ISO-8601 UTC com sufixo "Z", coordenadas com no máximo 6 casas decimais.
/// </summary>
public static class ProtocoloProximidade
{
    public const string Versao = "alerta-em-acao/proximidade/v2";
    /// <summary>Identificador de serviço Nearby — idêntico no Android e no iOS.</summary>
    public const string IdentificadorServico = "br.alertaemacao.nearby";
    /// <summary>Estratégia Nearby — P2P_CLUSTER no Android, .cluster no iOS.</summary>
    public const string Estrategia = "P2P_CLUSTER";
    public const int TamanhoMaximoBytes = 32 * 1024;
    private const string RotuloChave = "alerta-em-acao/proximidade/v2";

    public static class Tipos
    {
        public const string PedidoSocorro = "PEDIDO_SOCORRO";
        public const string Atualizacao = "ATUALIZACAO";
        public const string AvisoDemonstrativo = "AVISO_DEMONSTRATIVO";
        public const string Confirmacao = "CONFIRMACAO";
        public static readonly string[] Todos = [PedidoSocorro, Atualizacao, AvisoDemonstrativo, Confirmacao];
    }

    public static class Estados
    {
        public const string SalvoNoAparelho = "SALVO_NO_APARELHO";                  // nível 1
        public const string RecebidoPorProximidade = "RECEBIDO_POR_PROXIMIDADE";    // nível 2
        public const string RecebidoPeloGateway = "RECEBIDO_PELO_GATEWAY";          // nível 3
        public const string RecebidoPeloServidor = "RECEBIDO_PELO_SERVIDOR";        // nível 4
        public const string RecebidoPelaCentralSimulada = "RECEBIDO_PELA_CENTRAL_SIMULADA"; // nível 5
        public const string Falhou = "FALHOU";
        public static readonly string[] Todos = [SalvoNoAparelho, RecebidoPorProximidade, RecebidoPeloGateway, RecebidoPeloServidor, RecebidoPelaCentralSimulada, Falhou];
        public static int Nivel(string? estado) => estado switch
        {
            SalvoNoAparelho => 1, RecebidoPorProximidade => 2, RecebidoPeloGateway => 3,
            RecebidoPeloServidor => 4, RecebidoPelaCentralSimulada => 5, _ => 0,
        };
    }

    /// <summary>Chave do grupo de demonstração: HMAC-SHA256(rótulo, códigoDoGrupo). Todos os aparelhos do grupo digitam o mesmo código.</summary>
    public static byte[] ChaveGrupo(string codigoGrupo) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(RotuloChave), Encoding.UTF8.GetBytes(codigoGrupo.Trim().ToUpperInvariant()));

    /// <summary>Remetente pseudonimizado: "ap-" + 16 hex de SHA-256 do identificador de instalação. Não identifica a pessoa.</summary>
    public static string Pseudonimizar(string identificadorInstalacao) =>
        "ap-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identificadorInstalacao)))[..16].ToLowerInvariant();

    /// <summary>
    /// Forma canônica usada na integridade. Campos mutáveis em trânsito (numeroTentativas, estadoTransmissao)
    /// ficam de fora. Linhas separadas por "\n"; ausente = vazio; coordenadas com 6 casas; precisão inteira;
    /// dadosEstruturados como "chave=valor" ordenado por chave (ordinal), omitindo nulos.
    /// </summary>
    public static string Canonica(MensagemProximidadeV2 m)
    {
        var sb = new StringBuilder();
        void L(string? v) => sb.Append(v ?? "").Append('\n');
        L(m.VersaoProtocolo); L(m.IdentificadorMensagem); L(m.IdentificadorOcorrencia); L(m.TipoMensagem); L(m.Municipio);
        L(m.CoordenadasAutorizadas is { } c ? c.Latitude.ToString("F6", CultureInfo.InvariantCulture) : null);
        L(m.CoordenadasAutorizadas is { } c2 ? c2.Longitude.ToString("F6", CultureInfo.InvariantCulture) : null);
        L(m.CoordenadasAutorizadas?.PrecisaoMetros is { } p ? ((long)Math.Round(p, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) : null);
        L(m.HorarioCriacao); L(m.HorarioUltimaAtualizacao); L(m.RemetentePseudonimizado); L(m.IdentificadorCorrelacao);
        foreach (var (k, v) in Achatar(m.DadosEstruturados).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append(k).Append('=').Append(v).Append('\n');
        return sb.ToString();
    }

    public static string CalcularIntegridade(MensagemProximidadeV2 m, byte[] chaveGrupo) =>
        "hmac-sha256:" + B64Url(HMACSHA256.HashData(chaveGrupo, Encoding.UTF8.GetBytes(Canonica(m))));

    public static bool VerificarIntegridade(MensagemProximidadeV2 m, byte[] chaveGrupo) =>
        m.IntegridadeMensagem is { } i && ChavesCentrais.Comparar(i, CalcularIntegridade(m, chaveGrupo));

    /// <summary>Validação estrutural (independente da chave).</summary>
    public static IReadOnlyList<string> Validar(MensagemProximidadeV2 m)
    {
        var e = new List<string>();
        if (m.VersaoProtocolo != Versao) e.Add("versaoProtocolo não suportada");
        if (!Guid.TryParse(m.IdentificadorMensagem, out _)) e.Add("identificadorMensagem deve ser UUID");
        if (!Guid.TryParse(m.IdentificadorOcorrencia, out _)) e.Add("identificadorOcorrencia deve ser UUID");
        if (!Tipos.Todos.Contains(m.TipoMensagem)) e.Add("tipoMensagem inválido");
        if (m.EstadoTransmissao is { } est && !Estados.Todos.Contains(est)) e.Add("estadoTransmissao inválido");
        if (!HorarioValido(m.HorarioCriacao)) e.Add("horarioCriacao deve ser ISO-8601 UTC terminado em Z");
        if (!HorarioValido(m.HorarioUltimaAtualizacao)) e.Add("horarioUltimaAtualizacao deve ser ISO-8601 UTC terminado em Z");
        if (string.IsNullOrWhiteSpace(m.RemetentePseudonimizado) || m.RemetentePseudonimizado.Length > 64) e.Add("remetentePseudonimizado inválido");
        if (m.NumeroTentativas is < 0 or > 1000) e.Add("numeroTentativas inválido");
        if (m.CoordenadasAutorizadas is { } c && (c.Latitude is < -90 or > 90 || c.Longitude is < -180 or > 180)) e.Add("coordenadas inválidas");
        if (m.TipoMensagem == Tipos.Confirmacao && !Guid.TryParse(m.IdentificadorCorrelacao, out _)) e.Add("CONFIRMACAO exige identificadorCorrelacao");
        if (string.IsNullOrWhiteSpace(m.IntegridadeMensagem)) e.Add("integridadeMensagem ausente");
        return e;
    }

    private static bool HorarioValido(string? h) =>
        h is { Length: >= 20 } && h.EndsWith('Z') && DateTimeOffset.TryParse(h, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

    private static IEnumerable<KeyValuePair<string, string>> Achatar(JsonElement? dados)
    {
        if (dados is not { ValueKind: JsonValueKind.Object } d) yield break;
        foreach (var p in d.EnumerateObject())
        {
            var v = p.Value;
            string? s = v.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => v.TryGetInt64(out var n) ? n.ToString(CultureInfo.InvariantCulture) : v.GetDouble().ToString("F6", CultureInfo.InvariantCulture),
                JsonValueKind.String => Escapar(v.GetString()!),
                JsonValueKind.Array => string.Join(",", v.EnumerateArray().Select(x => x.ValueKind switch
                {
                    JsonValueKind.Number when x.TryGetInt64(out var xi) => xi.ToString(CultureInfo.InvariantCulture),
                    JsonValueKind.String => Escapar(x.GetString()!).Replace(",", "\\,"),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => "",
                })),
                _ => null, // objetos aninhados não fazem parte do protocolo v2
            };
            if (s is not null) yield return new(p.Name, s);
        }
    }

    private static string Escapar(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record CoordenadasAutorizadasV2(double Latitude, double Longitude, double? PrecisaoMetros);

/// <summary>Envelope do protocolo v2. Nomes exigidos pela especificação do hackathon.</summary>
public sealed record MensagemProximidadeV2
{
    public required string VersaoProtocolo { get; init; }
    public required string IdentificadorMensagem { get; init; }
    public required string IdentificadorOcorrencia { get; init; }
    public required string TipoMensagem { get; init; }
    public string? Municipio { get; init; }
    public CoordenadasAutorizadasV2? CoordenadasAutorizadas { get; init; }
    public required string HorarioCriacao { get; init; }
    public required string HorarioUltimaAtualizacao { get; init; }
    public required string RemetentePseudonimizado { get; init; }
    public JsonElement? DadosEstruturados { get; init; }
    public int NumeroTentativas { get; init; }
    public string? IdentificadorCorrelacao { get; init; }
    public string? EstadoTransmissao { get; init; }
    public string? IntegridadeMensagem { get; init; }
}

/// <summary>Corpo do POST do aparelho-gateway ao backend: a mensagem original intacta + o registro do recebimento local.</summary>
public sealed record EntregaGatewayV2(
    MensagemProximidadeV2 Mensagem,
    string ReceptorPseudonimizado,
    string RecebidoLocalmenteEm,
    string Canal,
    string? ModeloAparelhoReceptor = null,
    string? PlataformaEmissor = null,
    string? PlataformaReceptor = null,
    // Posição do APARELHO RECEPTOR (gateway), opcional. Nunca substitui coordenadasAutorizadas do emissor.
    CoordenadasAutorizadasV2? PosicaoReceptor = null);
