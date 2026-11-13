using System.Security.Cryptography;
using System.Text;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Localizacao;

namespace AlertaEmAcao.Modulos.Familias;

public enum StatusVinculo { AguardandoAprovacao, Ativo, Recusado, Revogado }

/// <summary>O que o familiar pode ver sobre pedidos de socorro associados à residência. Decidido pelo morador.</summary>
public sealed record PermissoesVinculo(bool ReceberNotificacoes = true, bool VerNecessidades = true, bool VerMoradores = false, bool VerPosicaoDoPedido = false)
{
    public PermissoesVinculo() : this(true, true, false, false) { }
}

/// <summary>
/// Vínculo explícito entre a residência de um morador (LocalMonitorado "moro aqui") e um familiar com conta.
/// Nunca é criado por endereço, CEP ou proximidade: só por convite usado pelo familiar e aprovação do morador.
/// </summary>
public sealed class VinculoFamiliar
{
    public Guid Id { get; private set; }
    public Guid ResidenciaId { get; private set; }
    public Guid MoradorId { get; private set; }
    public Guid FamiliarId { get; private set; }
    /// <summary>Como o familiar chama essa residência ("Casa da minha mãe").</summary>
    public string ApelidoParaFamiliar { get; private set; } = default!;
    public StatusVinculo Status { get; private set; }
    public PermissoesVinculo Permissoes { get; private set; } = new();
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset? DecididoEm { get; private set; }
    public DateTimeOffset? RevogadoEm { get; private set; }
    public string? RevogadoPor { get; private set; }

    private VinculoFamiliar() { }

    internal static VinculoFamiliar Solicitar(Guid residenciaId, Guid moradorId, Guid familiarId, string apelido, DateTimeOffset agora)
    {
        apelido = (apelido ?? "").Trim();
        if (apelido.Length is < 1 or > 60) throw new ErroDominio("apelido_invalido", "Dê um nome curto à residência (até 60 caracteres).");
        return new VinculoFamiliar
        {
            Id = Guid.CreateVersion7(), ResidenciaId = residenciaId, MoradorId = moradorId, FamiliarId = familiarId,
            ApelidoParaFamiliar = apelido, Status = StatusVinculo.AguardandoAprovacao, CriadoEm = agora,
        };
    }

    public void Decidir(bool aprovar, PermissoesVinculo? permissoes, DateTimeOffset agora)
    {
        if (Status != StatusVinculo.AguardandoAprovacao) throw new ErroDominio("estado_invalido", "Este vínculo não está aguardando aprovação.");
        Status = aprovar ? StatusVinculo.Ativo : StatusVinculo.Recusado;
        if (permissoes is not null) Permissoes = permissoes;
        DecididoEm = agora;
    }

    public void AlterarPermissoes(PermissoesVinculo p)
    {
        if (Status != StatusVinculo.Ativo) throw new ErroDominio("estado_invalido", "Só vínculos ativos têm permissões.");
        Permissoes = p;
    }

    public void Revogar(string por, DateTimeOffset agora)
    {
        if (Status is StatusVinculo.Revogado or StatusVinculo.Recusado) return;
        Status = StatusVinculo.Revogado;
        RevogadoEm = agora;
        RevogadoPor = por;
    }

    public bool Ativo => Status == StatusVinculo.Ativo;
}

/// <summary>Convite temporário e de uso único. Só o hash do código é guardado.</summary>
public sealed class ConviteVinculo
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);
    // Sem 0/O/1/I/L para ditar por telefone sem confusão.
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public Guid Id { get; private set; }
    public Guid ResidenciaId { get; private set; }
    public Guid MoradorId { get; private set; }
    public string CodigoHash { get; private set; } = default!;
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? UsadoEm { get; private set; }
    public Guid? UsadoPor { get; private set; }
    public bool Cancelado { get; private set; }

    private ConviteVinculo() { }

    public static (ConviteVinculo Convite, string Codigo) Gerar(Guid residenciaId, Guid moradorId, DateTimeOffset agora)
    {
        var bytes = RandomNumberGenerator.GetBytes(10);
        var codigo = new string(bytes.Select(b => Alfabeto[b % Alfabeto.Length]).ToArray());
        return (new ConviteVinculo
        {
            Id = Guid.CreateVersion7(), ResidenciaId = residenciaId, MoradorId = moradorId, CodigoHash = Hash(codigo),
            CriadoEm = agora, ExpiraEm = agora + Validade,
        }, $"{codigo[..5]}-{codigo[5..]}");
    }

    public static string Normalizar(string codigo) => new((codigo ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    public static string Hash(string codigo) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("alerta-em-acao/convite/v1:" + Normalizar(codigo))));

    public bool Valido(DateTimeOffset agora) => !Cancelado && UsadoEm is null && agora < ExpiraEm;

    public void Usar(Guid familiarId, DateTimeOffset agora) { UsadoEm = agora; UsadoPor = familiarId; }
    public void Cancelar() => Cancelado = true;
}

public sealed class ServicoVinculos(IUnidadeTrabalho uow, IRelogio relogio)
{
    public const int ConvitesAtivosPorResidencia = 5;
    public const int VinculosPorResidencia = 10;
    /// <summary>Mensagem única para código inexistente, expirado ou usado: não revela se existe convite ou conta.</summary>
    public const string MensagemCodigoInvalido = "Código inválido ou expirado. Peça um novo código ao morador.";

    public async Task<LocalMonitorado> ResidenciaDoMoradorAsync(Guid residenciaId, Guid moradorId, CancellationToken ct)
    {
        var l = await uow.PrimeiroAsync(uow.Consulta<LocalMonitorado>().Where(x => x.Id == residenciaId && x.UsuarioId == moradorId && x.Ativo), ct)
            ?? throw new ErroNaoEncontrado("Residência não encontrada.");
        if (l.Tipo != TipoLocal.Residencia || !l.MoroAqui)
            throw new ErroDominio("nao_moradia", "Convites só podem ser criados para a residência onde você mora (marque \"Eu moro aqui\").");
        return l;
    }

    public async Task<(ConviteVinculo Convite, string Codigo)> GerarConviteAsync(Guid residenciaId, Guid moradorId, CancellationToken ct)
    {
        await ResidenciaDoMoradorAsync(residenciaId, moradorId, ct);
        var agora = relogio.Agora;
        var ativos = await uow.ContarAsync(uow.Consulta<ConviteVinculo>().Where(c => c.ResidenciaId == residenciaId && !c.Cancelado && c.UsadoEm == null && c.ExpiraEm > agora), ct);
        if (ativos >= ConvitesAtivosPorResidencia) throw new ErroDominio("convites_demais", "Há muitos convites em aberto. Cancele algum ou aguarde expirarem.");
        var (c, codigo) = ConviteVinculo.Gerar(residenciaId, moradorId, agora);
        uow.Adicionar(c);
        await uow.SalvarAsync(ct);
        return (c, codigo);
    }

    /// <summary>Familiar usa o código: cria vínculo AGUARDANDO aprovação do morador. Nada é liberado antes disso.</summary>
    public async Task<VinculoFamiliar> UsarConviteAsync(string codigo, Guid familiarId, string apelido, CancellationToken ct)
    {
        var agora = relogio.Agora;
        var hash = ConviteVinculo.Hash(codigo);
        var c = await uow.PrimeiroAsync(uow.Consulta<ConviteVinculo>().Where(x => x.CodigoHash == hash), ct);
        if (c is null || !c.Valido(agora)) throw new ErroDominio("convite_invalido", MensagemCodigoInvalido);
        if (c.MoradorId == familiarId) throw new ErroDominio("proprio_convite", "Este código é da sua própria residência. Envie-o ao familiar.");
        var res = await uow.PrimeiroAsync(uow.Consulta<LocalMonitorado>().Where(x => x.Id == c.ResidenciaId && x.Ativo && x.MoroAqui), ct);
        if (res is null) throw new ErroDominio("convite_invalido", MensagemCodigoInvalido);
        var existentes = await uow.ListarAsync(uow.Consulta<VinculoFamiliar>().Where(v => v.ResidenciaId == c.ResidenciaId), ct);
        if (existentes.Any(v => v.FamiliarId == familiarId && v.Status is StatusVinculo.Ativo or StatusVinculo.AguardandoAprovacao))
            throw new ErroDominio("vinculo_existente", "Você já tem um vínculo com esta residência.");
        if (existentes.Count(v => v.Status is StatusVinculo.Ativo or StatusVinculo.AguardandoAprovacao) >= VinculosPorResidencia)
            throw new ErroDominio("vinculos_demais", "Esta residência atingiu o limite de familiares vinculados.");
        c.Usar(familiarId, agora);
        var v = VinculoFamiliar.Solicitar(c.ResidenciaId, c.MoradorId, familiarId, apelido, agora);
        uow.Adicionar(v);
        await uow.SalvarAsync(ct);
        return v;
    }

    public async Task<VinculoFamiliar> DoMoradorAsync(Guid vinculoId, Guid moradorId, CancellationToken ct) =>
        await uow.PrimeiroAsync(uow.Consulta<VinculoFamiliar>().Where(v => v.Id == vinculoId && v.MoradorId == moradorId), ct)
        ?? throw new ErroNaoEncontrado("Vínculo não encontrado.");

    public async Task<VinculoFamiliar> DecidirAsync(Guid vinculoId, Guid moradorId, bool aprovar, PermissoesVinculo? p, CancellationToken ct)
    {
        var v = await DoMoradorAsync(vinculoId, moradorId, ct);
        v.Decidir(aprovar, p, relogio.Agora);
        await uow.SalvarAsync(ct);
        return v;
    }

    public async Task<VinculoFamiliar> AlterarPermissoesAsync(Guid vinculoId, Guid moradorId, PermissoesVinculo p, CancellationToken ct)
    {
        var v = await DoMoradorAsync(vinculoId, moradorId, ct);
        v.AlterarPermissoes(p);
        await uow.SalvarAsync(ct);
        return v;
    }

    /// <summary>Morador ou o próprio familiar podem encerrar o vínculo a qualquer momento.</summary>
    public async Task RevogarAsync(Guid vinculoId, Guid usuarioId, CancellationToken ct)
    {
        var v = await uow.PrimeiroAsync(uow.Consulta<VinculoFamiliar>().Where(x => x.Id == vinculoId && (x.MoradorId == usuarioId || x.FamiliarId == usuarioId)), ct)
            ?? throw new ErroNaoEncontrado("Vínculo não encontrado.");
        v.Revogar(v.MoradorId == usuarioId ? "morador" : "familiar", relogio.Agora);
        await uow.SalvarAsync(ct);
    }

    /// <summary>Ao remover a residência ou deixar de morar nela, todos os vínculos e convites caem.</summary>
    public async Task EncerrarDaResidenciaAsync(Guid residenciaId, CancellationToken ct)
    {
        var agora = relogio.Agora;
        foreach (var v in await uow.ListarAsync(uow.Consulta<VinculoFamiliar>().Where(x => x.ResidenciaId == residenciaId && (x.Status == StatusVinculo.Ativo || x.Status == StatusVinculo.AguardandoAprovacao)), ct))
            v.Revogar("residencia_removida", agora);
        foreach (var c in await uow.ListarAsync(uow.Consulta<ConviteVinculo>().Where(x => x.ResidenciaId == residenciaId && x.UsadoEm == null && !x.Cancelado), ct))
            c.Cancelar();
    }
}
