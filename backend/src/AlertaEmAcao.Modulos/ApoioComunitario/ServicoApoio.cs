using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Modulos.ApoioComunitario;

public sealed record ComandoOferta(Guid Id, string DispositivoId, string MunicipioCodigo, string? Regiao, TipoAjuda Tipo, string Descricao,
    string? Disponibilidade, string? OrganizacaoReferencia, string? ContatoAutorizado);

public sealed record ComandoOfertaPedido(Guid Id, string DispositivoId, Guid OcorrenciaId, TipoAjuda Tipo, string Descricao, string? Disponibilidade,
    string? ContatoAutorizado, bool CienteSeguranca);

public sealed class ServicoApoio(IUnidadeTrabalho uow, INotificadorTempoReal tempoReal, IRelogio relogio)
{
    public async Task<(OfertaAjuda Oferta, bool JaExistia)> OfertarParaPedidoAsync(ComandoOfertaPedido c, Guid? usuarioId, CancellationToken ct)
    {
        if (c.Id != Guid.Empty && await uow.PrimeiroAsync(uow.Consulta<OfertaAjuda>().Where(o => o.Id == c.Id), ct) is { } existente)
            return (existente, true);
        var oc = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(o => o.Id == c.OcorrenciaId), ct) ?? throw new ErroNaoEncontrado("Pedido não encontrado.");
        // Só pedidos que o solicitante autorizou mostrar à comunidade podem receber ofertas de desconhecidos.
        if (!oc.Compartilhamento.Autorizado || oc.Status == StatusOcorrencia.Encerrada || oc.Verificacao == VerificacaoOcorrencia.DescartadaPorOperador)
            throw new ErroDominio("pedido_indisponivel", "Este pedido não está aberto para ofertas da comunidade.");
        if (oc.PertenceAoDispositivo(c.DispositivoId)) throw new ErroDominio("proprio_pedido", "Você não pode oferecer ajuda ao seu próprio pedido.");
        var abertas = await uow.ListarAsync(uow.Consulta<OfertaAjuda>().Where(o => o.OcorrenciaId == c.OcorrenciaId && o.DispositivoId == c.DispositivoId
            && (o.Status == StatusOferta.AguardandoAvaliacao || o.Status == StatusOferta.AceitaPelaEquipe || o.Status == StatusOferta.Encaminhada)), ct);
        if (abertas.Count > 0) throw new ErroDominio("oferta_existente", "Você já tem uma oferta em andamento para este pedido.");
        var agora = relogio.Agora;
        var o = OfertaAjuda.RegistrarParaPedido(c.Id, usuarioId, c.DispositivoId, oc.Id, oc.MunicipioCodigo, c.Tipo, c.Descricao, c.Disponibilidade,
            c.ContatoAutorizado, c.CienteSeguranca, agora);
        oc.Registrar(agora, "oferta_comunitaria", $"Oferta de ajuda comunitária recebida ({o.Tipo}); aguardando avaliação da equipe.");
        uow.Adicionar(o);
        await uow.SalvarAsync(ct);
        await tempoReal.OcorrenciaAtualizadaAsync(oc.Id, oc.MunicipioCodigo, [], "oferta_comunitaria");
        return (o, false);
    }

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
