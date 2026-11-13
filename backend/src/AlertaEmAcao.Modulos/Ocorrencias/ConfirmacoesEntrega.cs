namespace AlertaEmAcao.Modulos.Ocorrencias;

/// <summary>
/// Níveis de entrega comprovada. Cada nível só é registrado com evidência do participante que persistiu.
/// O nível 6 (recebimento institucional real) não existe neste sistema: depende de integração oficial homologada.
/// </summary>
public enum NivelEntrega
{
    /// <summary>1 — o aparelho emissor salvou o pedido (informado pelo próprio aparelho).</summary>
    PersistenciaLocal = 1,
    /// <summary>2 — um aparelho próximo recebeu e persistiu a mensagem.</summary>
    RecebidoPorDispositivoProximo = 2,
    /// <summary>3 — o intermediário responsável pelo encaminhamento confirmou persistência.</summary>
    RecebidoPorGateway = 3,
    /// <summary>4 — a API do Alerta em Ação persistiu o registro.</summary>
    RecebidoPeloBackend = 4,
    /// <summary>5 — a central SIMULADA persistiu e devolveu protocolo de demonstração.</summary>
    RecebidoPelaCentralDemonstrativa = 5,
}

public sealed class ConfirmacaoEntrega
{
    public Guid Id { get; private set; }
    public Guid OcorrenciaId { get; private set; }
    public string? IdentificadorMensagem { get; private set; }
    public NivelEntrega Nivel { get; private set; }
    public string Origem { get; private set; } = default!;
    public string Destino { get; private set; } = default!;
    /// <summary>Momento em que o participante declarou a persistência (relógio dele).</summary>
    public DateTimeOffset Horario { get; private set; }
    public string Canal { get; private set; } = default!;
    public string Resultado { get; private set; } = default!;
    public string? Comprovante { get; private set; }
    public DateTimeOffset RegistradoEm { get; private set; }

    private ConfirmacaoEntrega() { }

    public static ConfirmacaoEntrega Criar(Guid ocorrenciaId, string? mensagemId, NivelEntrega nivel, string origem, string destino,
        DateTimeOffset horario, string canal, string resultado, string? comprovante, DateTimeOffset agora) => new()
    {
        Id = Guid.NewGuid(), OcorrenciaId = ocorrenciaId, IdentificadorMensagem = mensagemId, Nivel = nivel,
        Origem = Limitar(origem, 120), Destino = Limitar(destino, 120), Horario = horario, Canal = Limitar(canal, 40),
        Resultado = Limitar(resultado, 200), Comprovante = comprovante is null ? null : Limitar(comprovante, 200), RegistradoEm = agora,
    };

    private static string Limitar(string s, int n) => s.Length <= n ? s : s[..n];

    public static string Descrever(NivelEntrega n) => n switch
    {
        NivelEntrega.PersistenciaLocal => "Salvo no celular de quem pediu ajuda",
        NivelEntrega.RecebidoPorDispositivoProximo => "Recebido e guardado por um celular próximo",
        NivelEntrega.RecebidoPorGateway => "Recebido por um ponto de apoio para envio",
        NivelEntrega.RecebidoPeloBackend => "Pedido de ajuda registrado",
        NivelEntrega.RecebidoPelaCentralDemonstrativa => "A equipe de teste (simulada) recebeu o pedido",
        _ => n.ToString(),
    };
}
