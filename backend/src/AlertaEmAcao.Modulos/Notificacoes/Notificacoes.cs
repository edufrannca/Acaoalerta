using AlertaEmAcao.Modulos.Comum;

namespace AlertaEmAcao.Modulos.Notificacoes;

public enum RespostaNotificacao { EstouSeguro, PrecisoDeSocorro, VerOrientacoes }

public sealed record EntregaCanal(string Canal, string Resultado, DateTimeOffset Em, string? Detalhe);

/// <summary>Notificação proativa gerada a partir de um aviso aplicável a um local monitorado.</summary>
public sealed class Notificacao
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid AvisoId { get; private set; }
    public Guid? LocalMonitoradoId { get; private set; }
    public string Titulo { get; private set; } = default!;
    public string Mensagem { get; private set; } = default!;
    public string Pergunta { get; private set; } = default!;
    public bool AvisoOficial { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public List<EntregaCanal> Entregas { get; private set; } = [];
    public DateTimeOffset? LidaEm { get; private set; }
    public RespostaNotificacao? Resposta { get; private set; }
    public DateTimeOffset? RespondidaEm { get; private set; }

    private Notificacao() { }

    public static Notificacao Criar(Guid usuarioId, Guid avisoId, Guid? localId, string apelidoLocal, string tituloAviso, bool oficial, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(), UsuarioId = usuarioId, AvisoId = avisoId, LocalMonitoradoId = localId, AvisoOficial = oficial, CriadaEm = agora,
        Titulo = oficial ? "Atenção à sua região" : "[DEMONSTRAÇÃO] Atenção à sua região",
        Mensagem = oficial
            ? $"Existe um aviso oficial ({tituloAviso}) aplicável à sua localização monitorada \"{apelidoLocal}\"."
            : $"Aviso DEMONSTRATIVO (não oficial) aplicável à localização monitorada \"{apelidoLocal}\": {tituloAviso}.",
        Pergunta = "Você e sua família estão bem?",
    };

    public void RegistrarEntrega(string canal, string resultado, DateTimeOffset em, string? detalhe = null) =>
        Entregas.Add(new EntregaCanal(canal, resultado, em, detalhe));

    public void MarcarLida(DateTimeOffset agora) => LidaEm ??= agora;

    public void Responder(RespostaNotificacao r, DateTimeOffset agora)
    {
        Resposta = r;
        RespondidaEm = agora;
        LidaEm ??= agora;
    }
}

public sealed class AssinaturaPush
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Endpoint { get; private set; } = default!;
    public string P256dh { get; private set; } = default!;
    public string Auth { get; private set; } = default!;
    public DateTimeOffset CriadaEm { get; private set; }
    public bool Ativa { get; private set; }

    private AssinaturaPush() { }

    public static AssinaturaPush Criar(Guid usuarioId, string endpoint, string p256dh, string auth, DateTimeOffset agora)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var u) || u.Scheme != "https")
            throw new ErroDominio("endpoint_invalido", "Endpoint de push inválido.");
        return new AssinaturaPush { Id = Guid.CreateVersion7(), UsuarioId = usuarioId, Endpoint = endpoint, P256dh = p256dh, Auth = auth, CriadaEm = agora, Ativa = true };
    }

    public void Desativar() => Ativa = false;
}

public sealed record ConteudoPush(string Titulo, string Corpo, string Url, string Tag);

public interface IEnviadorPush
{
    bool Configurado { get; }
    string? ChavePublicaVapid { get; }
    /// <summary>Retorna (sucesso, assinaturaExpirada, detalhe).</summary>
    Task<(bool Sucesso, bool Expirada, string Detalhe)> EnviarAsync(AssinaturaPush assinatura, ConteudoPush conteudo, CancellationToken ct);
}
