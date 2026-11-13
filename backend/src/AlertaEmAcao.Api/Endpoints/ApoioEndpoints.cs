using System.Security.Claims;
using AlertaEmAcao.Contratos;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record OfertaRequest(Guid Id, string MunicipioCodigo, string? Regiao, TipoAjuda Tipo, string Descricao, string? Disponibilidade,
    string? OrganizacaoReferencia, string? ContatoAutorizado);
public sealed record ParticipacaoRequest(Guid NecessidadeId);

public static class ApoioEndpoints
{
    public static void MapApoio(this WebApplication app)
    {
        var g = app.MapGroup("/api/apoio").WithTags("Apoio comunitário");

        g.MapGet("/necessidades", async (IUnidadeTrabalho uow, string? municipio, CancellationToken ct) =>
        {
            var q = uow.Consulta<NecessidadePublicada>().Where(n => n.Status == StatusNecessidade.Aberta);
            if (municipio is not null) q = q.Where(n => n.MunicipioCodigo == municipio);
            var l = await uow.ListarAsync(q.OrderByDescending(n => n.PublicadaEm), ct);
            return Results.Ok(new
            {
                observacao = "Necessidades publicadas por organizações verificadas. Na demonstração, organizações e necessidades são FICTÍCIAS. Nunca se desloque para áreas de risco.",
                necessidades = l.Select(n => new { n.Id, n.OrganizacaoCodigo, n.OrganizacaoNome, n.MunicipioCodigo, tipo = n.Tipo.ToString(), n.Titulo, n.Descricao, n.PontoEntregaVerificado, n.Ficticia, n.PublicadaEm }),
            });
        });

        g.MapPost("/ofertas", async (OfertaRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoApoio s, CancellationToken ct) =>
        {
            var (o, ja) = await s.RegistrarOfertaAsync(new ComandoOferta(r.Id, OcorrenciasEndpoints.Dispositivo(ctx), r.MunicipioCodigo, r.Regiao, r.Tipo,
                r.Descricao, r.Disponibilidade, r.OrganizacaoReferencia, r.ContatoAutorizado), p.Id(), ct);
            return ja ? Results.Ok(Visao(o, null)) : Results.Created($"/api/apoio/ofertas/{o.Id}", Visao(o, null));
        });

        g.MapPost("/ofertas/{id:guid}/participacao", async (Guid id, ParticipacaoRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoApoio s, CancellationToken ct) =>
        {
            var o = await s.SolicitarParticipacaoAsync(id, r.NecessidadeId, OcorrenciasEndpoints.Dispositivo(ctx), p.Id(), ct);
            return Results.Ok(Visao(o, null));
        });

        g.MapGet("/ofertas/minhas", async (HttpContext ctx, ClaimsPrincipal p, AlertaDbContext db, CancellationToken ct) =>
        {
            var d = OcorrenciasEndpoints.Dispositivo(ctx);
            var uid = p.Id();
            var os = await db.Set<OfertaAjuda>().AsNoTracking().Where(o => o.DispositivoId == d || (uid != null && o.UsuarioId == uid))
                .OrderByDescending(o => o.CriadaEm).Take(30).ToListAsync(ct);
            var ids = os.Select(o => o.Id).ToList();
            var encs = (await db.Set<EncaminhamentoInstitucional>().AsNoTracking().Where(e => ids.Contains(e.ReferenciaId)).ToListAsync(ct))
                .GroupBy(e => e.ReferenciaId).ToDictionary(x => x.Key, x => x.OrderByDescending(e => e.CriadoEm).First());
            return Results.Ok(os.Select(o => Visao(o, encs.GetValueOrDefault(o.Id))));
        });

        // Painel da organização (simulada)
        app.MapGet("/api/painel/organizacao/ofertas", async (ClaimsPrincipal u, AlertaDbContext db, CancellationToken ct) =>
        {
            var central = u.Central();
            var encs = await db.Set<EncaminhamentoInstitucional>().AsNoTracking()
                .Where(e => e.TipoMensagem == TipoMensagem.OfertaApoio && (u.Admin() || e.InstituicaoCodigo == central)).ToListAsync(ct);
            var ids = encs.Select(e => e.ReferenciaId).ToList();
            var os = await db.Set<OfertaAjuda>().AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(ct);
            return Results.Ok(new { aviso = CentraisSimuladas.ExtensoesCentraisSimuladas.Aviso, ofertas = os.Select(o => Visao(o, encs.FirstOrDefault(e => e.ReferenciaId == o.Id), incluirContato: true)) });
        }).RequireAuthorization("Organizacao").WithTags("Apoio comunitário");
    }

    private static object Visao(OfertaAjuda o, EncaminhamentoInstitucional? e, bool incluirContato = false) => new
    {
        o.Id, o.MunicipioCodigo, o.RegiaoDisponibilidade, tipo = o.Tipo.ToString(), o.Descricao, o.Disponibilidade, o.NecessidadeId,
        status = o.Status.ToString(), o.ProtocoloOrganizacao, o.Historico, o.CriadaEm,
        contatoAutorizado = incluirContato ? o.ContatoAutorizado : null,
        encaminhamento = e is null ? null : new { e.Id, organizacao = e.InstituicaoCodigo, status = e.Status.ToString(), e.Protocolo, e.ConfirmadoEm, e.Tentativas, e.UltimoErro },
    };
}

public static class IntegracoesEndpoints
{
    public const string CabecalhoGateway = "X-Gateway-Chave";

    public static void MapIntegracoes(this WebApplication app)
    {
        var g = app.MapGroup("/api").WithTags("Integrações");

        // Retorno assíncrono das centrais (simuladas) — autenticado por chave compartilhada.
        g.MapPost("/integracoes/retorno-central", async (RetornoEstadoCentralV1 r, HttpRequest req, ChaveRetornoCentrais chave, ServicoDespacho s, CancellationToken ct) =>
        {
            if (!ChavesCentrais.Comparar(req.Headers[CabecalhosIntegracao.ChaveRetorno].FirstOrDefault(), chave.Valor)) return Results.Unauthorized();
            if (r.Versao != VersoesContrato.RetornoCentral) return Results.BadRequest(new { erro = "Versão de contrato não suportada." });
            await s.RegistrarRetornoAsync(r, ct);
            return Results.Ok(new { recebido = true });
        });

        // Gateway SIMULADO (LoRa) / retransmissão Nearby: recebe pacote compacto e registra a ocorrência.
        g.MapPost("/gateway/pacotes", async (PacoteGatewayV1 p, HttpRequest req, IConfiguration cfg, ServicoOcorrencias s, CancellationToken ct) =>
        {
            var segredo = cfg["Centrais:SegredoMestre"]!;
            if (!ChavesCentrais.Comparar(req.Headers[CabecalhoGateway].FirstOrDefault(), ChavesCentrais.Derivar(segredo, "gateway:" + p.Receptor)))
                return Results.Unauthorized();
            if (p.Versao != VersoesContrato.PacoteGateway) return Results.BadRequest(new { erro = "Versão de pacote não suportada." });
            if (p.Saltos is < 0 or > 8) return Results.BadRequest(new { erro = "Número de saltos inválido." });
            var canal = p.Canal == "nearby" ? CanalRecebimento.ProximidadeNearby : CanalRecebimento.GatewaySimulado;
            var res = await s.RegistrarSocorroAsync(new ComandoRegistrarSocorro(p.Id, p.DispositivoOrigem, p.CriadoEm, p.Latitude, p.Longitude, p.PrecisaoMetros, p.CriadoEm,
                new ComplementoOcorrencia { QuantidadePessoas = p.Pessoas, AguaSubindo = p.AguaSubindo, PessoasFeridas = p.Feridos == true ? 1 : null, Descricao = p.Texto },
                null, false, canal, $"{p.Receptor} ({p.Saltos} salto(s))"), null, ct);
            return Results.Ok(new { id = res.Ocorrencia.Id, recebidoEm = res.Ocorrencia.RecebidaNoServidorEm, duplicado = res.JaExistia, status = res.Ocorrencia.Status.ToString() });
        });
    }
}
