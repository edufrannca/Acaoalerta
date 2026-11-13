using AlertaEmAcao.Modulos.Comum;

namespace AlertaEmAcao.Modulos.Identidade;

/// <summary>Finalidades separadas (LGPD art. 8º §4º: autorizações genéricas são nulas). Todas opcionais.</summary>
public enum FinalidadeConsentimento
{
    /// <summary>A — alertas e informações dos locais monitorados.</summary>
    AlertasPreventivos,
    /// <summary>B — dados de atendimento e localização durante SOS ativo com equipes autorizadas (simuladas nesta versão).</summary>
    CompartilhamentoOperacional,
    /// <summary>C — resumo mínimo de novos pedidos para a comunidade.</summary>
    CompartilhamentoComunitario,
    /// <summary>D — familiares vinculados e aprovados recebem notificações sobre pedidos associados à residência.</summary>
    NotificacaoFamiliares,
}

public static class TextosConsentimento
{
    public const string Versao = "2026-11-13.1";

    public static readonly IReadOnlyDictionary<FinalidadeConsentimento, string> Textos = new Dictionary<FinalidadeConsentimento, string>
    {
        [FinalidadeConsentimento.AlertasPreventivos] = "Quero receber alertas e informações relacionadas aos locais que escolhi monitorar.",
        [FinalidadeConsentimento.CompartilhamentoOperacional] = "Autorizo o compartilhamento dos dados necessários ao atendimento e das atualizações de localização durante um pedido de socorro ativo com equipes autorizadas. Nesta versão do projeto, as equipes e centrais são simuladas.",
        [FinalidadeConsentimento.CompartilhamentoComunitario] = "Autorizo que novos pedidos de ajuda mostrem um resumo das necessidades e da região aproximada para que outras pessoas possam oferecer apoio.",
        [FinalidadeConsentimento.NotificacaoFamiliares] = "Autorizo que familiares vinculados e aprovados recebam notificações e informações permitidas sobre pedidos de socorro associados à minha residência.",
    };

    public const string NotaProtecaoDaVida =
        "Pedir socorro nunca depende destas autorizações. O tratamento estritamente necessário para proteger a vida durante um pedido " +
        "tem base legal própria (LGPD, art. 7º, VII, e art. 11, II, \"e\"), avaliada separadamente; a autorização B amplia o envio contínuo de localização e dados de atendimento.";
}

/// <summary>Registro imutável: cada concessão ou revogação é uma linha nova (data, finalidade, versão do texto, origem).</summary>
public sealed class RegistroConsentimento
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public FinalidadeConsentimento Finalidade { get; private set; }
    public bool Concedido { get; private set; }
    public string VersaoTexto { get; private set; } = default!;
    public string Origem { get; private set; } = default!;
    public DateTimeOffset Em { get; private set; }

    private RegistroConsentimento() { }

    public static RegistroConsentimento Novo(Guid usuarioId, FinalidadeConsentimento f, bool concedido, string origem, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(), UsuarioId = usuarioId, Finalidade = f, Concedido = concedido,
        VersaoTexto = TextosConsentimento.Versao, Origem = origem is "cadastro" or "perfil" ? origem : "perfil", Em = agora,
    };
}

public sealed record EstadoConsentimentos(bool AlertasPreventivos, bool CompartilhamentoOperacional, bool CompartilhamentoComunitario, bool NotificacaoFamiliares)
{
    public static EstadoConsentimentos Nenhum { get; } = new(false, false, false, false);
    public bool Tem(FinalidadeConsentimento f) => f switch
    {
        FinalidadeConsentimento.AlertasPreventivos => AlertasPreventivos,
        FinalidadeConsentimento.CompartilhamentoOperacional => CompartilhamentoOperacional,
        FinalidadeConsentimento.CompartilhamentoComunitario => CompartilhamentoComunitario,
        _ => NotificacaoFamiliares,
    };
}

public sealed class ServicoConsentimentos(IUnidadeTrabalho uow, IRelogio relogio)
{
    /// <summary>
    /// Estado atual = último registro de cada finalidade. Sem registro: não concedido.
    /// Exceção de compatibilidade: alertas preventivos seguem o campo legado do perfil (escolha real do usuário antes desta versão).
    /// </summary>
    public async Task<EstadoConsentimentos> AtualAsync(Guid usuarioId, CancellationToken ct)
    {
        var regs = await uow.ListarAsync(uow.Consulta<RegistroConsentimento>().Where(r => r.UsuarioId == usuarioId).OrderBy(r => r.Em), ct);
        bool? Ultimo(FinalidadeConsentimento f) => regs.LastOrDefault(r => r.Finalidade == f)?.Concedido;
        var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Id == usuarioId), ct);
        return new(Ultimo(FinalidadeConsentimento.AlertasPreventivos) ?? u?.ConsenteAlertas ?? false,
            Ultimo(FinalidadeConsentimento.CompartilhamentoOperacional) ?? false,
            Ultimo(FinalidadeConsentimento.CompartilhamentoComunitario) ?? false,
            Ultimo(FinalidadeConsentimento.NotificacaoFamiliares) ?? false);
    }

    /// <summary>Registra só mudanças reais. Mantém o campo legado de alertas em sincronia (usado na consulta espacial de avisos).</summary>
    public async Task<bool> DefinirAsync(Guid usuarioId, FinalidadeConsentimento f, bool concedido, string origem, CancellationToken ct, bool salvar = true)
    {
        var atual = await AtualAsync(usuarioId, ct);
        if (atual.Tem(f) == concedido && (origem != "cadastro" || !concedido)) return false;
        uow.Adicionar(RegistroConsentimento.Novo(usuarioId, f, concedido, origem, relogio.Agora));
        if (f == FinalidadeConsentimento.AlertasPreventivos &&
            await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Id == usuarioId), ct) is { } u)
            u.DefinirConsentimentoAlertas(concedido);
        if (salvar) await uow.SalvarAsync(ct);
        return true;
    }

    public Task<List<RegistroConsentimento>> HistoricoAsync(Guid usuarioId, CancellationToken ct) =>
        uow.ListarAsync(uow.Consulta<RegistroConsentimento>().Where(r => r.UsuarioId == usuarioId).OrderByDescending(r => r.Em), ct);
}
