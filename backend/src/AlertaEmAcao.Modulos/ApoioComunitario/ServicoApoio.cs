using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Encaminhamento;

namespace AlertaEmAcao.Modulos.ApoioComunitario;

public sealed record ComandoOferta(Guid Id, string DispositivoId, string MunicipioCodigo, string? Regiao, TipoAjuda Tipo, string Descricao,
    string? Disponibilidade, string? OrganizacaoReferencia, string? ContatoAutorizado);

public sealed class ServicoApoio(IUnidadeTrabalho uow, INotificadorTempoReal tempoReal, IRelogio relogio)
{
    public async Task<(OfertaAjuda Oferta, bool JaExistia)> RegistrarOfertaAsync(ComandoOferta c, Guid? usuarioId, CancellationToken ct)
    {
        if (c.Id != Guid.Empty && await uow.PrimeiroAsync(uow.Consulta<OfertaAjuda>().Where(o => o.Id == c.Id), ct) is { } existente)
            return (existente, true);
        var o = OfertaAjuda.Registrar(c.Id, usuarioId, c.DispositivoId, c.MunicipioCodigo, c.Regiao, c.Tipo, c.Descricao, c.Disponibilidade,
            c.OrganizacaoReferencia, c.ContatoAutorizado, relogio.Agora);
        uow.Adicionar(o);
        await uow.SalvarAsync(ct);
        return (o, false);
    }

    public async Task<OfertaAjuda> SolicitarParticipacaoAsync(Guid ofertaId, Guid necessidadeId, string dispositivoId, Guid? usuarioId, CancellationToken ct)
    {
        var o = await uow.PrimeiroAsync(uow.Consulta<OfertaAjuda>().Where(x => x.Id == ofertaId), ct) ?? throw new ErroNaoEncontrado("Oferta não encontrada.");
        if (o.DispositivoId != dispositivoId && (usuarioId is null || o.UsuarioId != usuarioId)) throw new ErroAcessoNegado("Oferta de outro usuário.");
        var n = await uow.PrimeiroAsync(uow.Consulta<NecessidadePublicada>().Where(x => x.Id == necessidadeId), ct) ?? throw new ErroNaoEncontrado("Necessidade não encontrada.");
        var org = await uow.PrimeiroAsync(uow.Consulta<Instituicao>().Where(i => i.Id == n.OrganizacaoId), ct) ?? throw new ErroNaoEncontrado("Organização não encontrada.");
        var agora = relogio.Agora;
        o.SolicitarParticipacao(n, agora);
        uow.Adicionar(EncaminhamentoInstitucional.Criar(TipoMensagem.OfertaApoio, o.Id, org, $"Oferta para necessidade publicada \"{n.Titulo}\"", agora));
        await uow.SalvarAsync(ct);
        await tempoReal.OfertaAtualizadaAsync(o.Id, org.Codigo);
        return o;
    }
}
