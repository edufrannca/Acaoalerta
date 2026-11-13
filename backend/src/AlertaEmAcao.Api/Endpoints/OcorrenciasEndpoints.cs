using System.Security.Claims;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record SocorroRequest(
    Guid Id,
    DateTimeOffset CriadaEm,
    double? Latitude,
    double? Longitude,
    double? PrecisaoMetros,
    DateTimeOffset? LocalizacaoLidaEm,
    ComplementoOcorrencia? Complemento,
    Guid? AvisoId,
    bool? IncluirFamilia);

public sealed record LocalizacaoRequest(double Latitude, double Longitude, double? PrecisaoMetros, DateTimeOffset LidaEm);

public sealed record SituacaoRequest(Guid Id, StatusSituacao Status, DateTimeOffset DeclaradaEm, double? Latitude, double? Longitude, Guid? AvisoId, bool? DesejaAjudar, string? Mensagem);

public static class OcorrenciasEndpoints
{
    public const string CabecalhoDispositivo = "X-Dispositivo-Id";

    public static string Dispositivo(HttpContext ctx)
    {
        var d = ctx.Request.Headers[CabecalhoDispositivo].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(d) || d.Length is < 16 or > 100)
            throw new ErroDominio("dispositivo_obrigatorio", $"Cabeçalho {CabecalhoDispositivo} obrigatório (identificador aleatório do aparelho).");
        return d;
    }

    public static void MapOcorrencias(this WebApplication app)
    {
        var g = app.MapGroup("/api").WithTags("Ocorrências (cidadão)");

        // Pedido de socorro: NÃO exige cadastro nem dados completos.
        g.MapPost("/ocorrencias", async (SocorroRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoOcorrencias s, CancellationToken ct) =>
        {
            var res = await s.RegistrarSocorroAsync(new ComandoRegistrarSocorro(r.Id, Dispositivo(ctx), r.CriadaEm, r.Latitude, r.Longitude,
                r.PrecisaoMetros, r.LocalizacaoLidaEm, r.Complemento, r.AvisoId, r.IncluirFamilia ?? true), p.Id(), ct);
            var corpo = VisaoSolicitante(res.Ocorrencia, res.Encaminhamentos);
            return res.JaExistia ? Results.Ok(corpo) : Results.Created($"/api/ocorrencias/{res.Ocorrencia.Id}", corpo);
        }).RequireRateLimiting("socorro");

        g.MapPatch("/ocorrencias/{id:guid}/complemento", async (Guid id, ComplementoOcorrencia c, HttpContext ctx, ClaimsPrincipal p,
            ServicoOcorrencias s, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var o = await s.ComplementarAsync(id, Dispositivo(ctx), p.Id(), c, ct);
            return Results.Ok(VisaoSolicitante(o, await Encaminhamentos(uow, id, ct)));
        }).RequireRateLimiting("socorro");

        g.MapPost("/ocorrencias/{id:guid}/localizacao", async (Guid id, LocalizacaoRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoOcorrencias s, CancellationToken ct) =>
        {
            var (o, aceita) = await s.AtualizarLocalizacaoAsync(id, Dispositivo(ctx), p.Id(), r.Latitude, r.Longitude, r.PrecisaoMetros, r.LidaEm, ct);
            return Results.Ok(new { aceita, motivo = aceita ? null : "Atualização muito frequente ou sem deslocamento relevante (proteção de bateria).",
                o.Latitude, o.Longitude, o.LocalizacaoLidaEm, o.MunicipioCodigo });
        }).RequireRateLimiting("socorro");

        g.MapGet("/ocorrencias/{id:guid}", async (Guid id, HttpContext ctx, ClaimsPrincipal p, ServicoOcorrencias s, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var o = await s.ObterDoSolicitanteAsync(id, ctx.Request.Headers[CabecalhoDispositivo].FirstOrDefault(), p.Id(), ct);
            return Results.Ok(VisaoSolicitante(o, await Encaminhamentos(uow, id, ct)));
        });

        g.MapGet("/ocorrencias/do-dispositivo", async (HttpContext ctx, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var d = Dispositivo(ctx);
            var os = await uow.ListarAsync(uow.Consulta<Ocorrencia>().Where(o => o.DispositivoId == d).OrderByDescending(o => o.RecebidaNoServidorEm).Take(30), ct);
            return Results.Ok(os.Select(o => new { o.Id, status = o.Status.ToString(), o.RecebidaNoServidorEm, o.CriadaNoDispositivoEm, o.MunicipioCodigo }));
        });

        // Visão comunitária: somente agregados aproximados (grade de ~1,1 km), sem dados pessoais.
        g.MapGet("/ocorrencias/comunitarias", async (IUnidadeTrabalho uow, IRelogio relogio, string? municipio, CancellationToken ct) =>
        {
            var desde = relogio.Agora.AddHours(-6);
            var q = uow.Consulta<Ocorrencia>().Where(o => o.RecebidaNoServidorEm >= desde && o.Latitude != null
                && o.Verificacao != VerificacaoOcorrencia.DescartadaPorOperador && o.Status != StatusOcorrencia.Encerrada);
            if (municipio is not null) q = q.Where(o => o.MunicipioCodigo == municipio);
            var pontos = await uow.ListarAsync(q.Select(o => new { o.Latitude, o.Longitude, o.MunicipioCodigo, o.Verificacao }), ct);
            const double passo = 0.01;
            var celulas = pontos.GroupBy(x => (Math.Round(x.Latitude!.Value / passo) * passo, Math.Round(x.Longitude!.Value / passo) * passo, x.MunicipioCodigo))
                .Select(c => new
                {
                    latitude = Math.Round(c.Key.Item1, 3), longitude = Math.Round(c.Key.Item2, 3), municipio = c.Key.MunicipioCodigo,
                    relatos = c.Count(), verificadas = c.Count(x => x.Verificacao == VerificacaoOcorrencia.VerificadaPorOperador),
                });
            return Results.Ok(new
            {
                observacao = "Localização aproximada (células de ~1 km). Relatos não verificados podem conter erros.",
                janelaHoras = 6, atualizadoEm = relogio.Agora, celulas,
            });
        });

        g.MapPost("/situacoes", async (SituacaoRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoOcorrencias s, CancellationToken ct) =>
        {
            var (sit, ja) = await s.DeclararSituacaoAsync(r.Id, Dispositivo(ctx), p.Id(), r.Status, r.DeclaradaEm, r.Latitude, r.Longitude, r.AvisoId,
                r.DesejaAjudar ?? false, r.Mensagem, ct);
            var corpo = new
            {
                sit.Id, status = sit.Status.ToString(), sit.RecebidaNoServidorEm, sit.MunicipioCodigo, sit.DesejaAjudar,
                transmissao = "recebida_pelo_servidor",
                observacao = "Autodeclaração registrada. Não certifica a segurança da residência ou da região.",
            };
            return ja ? Results.Ok(corpo) : Results.Created($"/api/situacoes/{sit.Id}", corpo);
        }).RequireRateLimiting("socorro");
    }

    public static Task<List<EncaminhamentoInstitucional>> Encaminhamentos(IUnidadeTrabalho uow, Guid id, CancellationToken ct) =>
        uow.ListarAsync(uow.Consulta<EncaminhamentoInstitucional>().Where(e => e.ReferenciaId == id).OrderBy(e => e.CriadoEm), ct);

    /// <summary>O que o próprio solicitante vê: estado real de transmissão e confirmações.</summary>
    public static object VisaoSolicitante(Ocorrencia o, IEnumerable<EncaminhamentoInstitucional> encs) => new
    {
        o.Id,
        status = o.Status.ToString(),
        statusDescricao = DescreverStatus(o.Status),
        o.RecebidaNoServidorEm,
        o.CriadaNoDispositivoEm,
        municipio = o.MunicipioCodigo is null ? null : new { codigo = o.MunicipioCodigo, nome = MunicipiosRmr.Nome(o.MunicipioCodigo) },
        localizacao = o.Latitude is null ? null : new { o.Latitude, o.Longitude, o.PrecisaoMetros, lidaEm = o.LocalizacaoLidaEm },
        encaminhamentos = encs.Where(e => e.TipoMensagem == TipoMensagem.Ocorrencia).Select(e => new
        {
            e.Id, central = e.InstituicaoCodigo, status = e.Status.ToString(), e.Protocolo, e.ConfirmadoEm, e.Tentativas, e.UltimoErro, e.EstadoNaCentral,
            simulada = true,
        }),
        historico = o.Historico.OrderBy(h => h.Em).Select(h => new { h.Em, h.Tipo, h.Descricao }),
        aviso = "Ambiente de demonstração: as centrais que confirmam são SIMULADAS. Em emergência real, ligue 193 ou 199.",
    };

    public static string DescreverStatus(StatusOcorrencia s) => s switch
    {
        StatusOcorrencia.RecebidaPeloServidor => "Recebido pelo servidor do Alerta em Ação. Aguardando confirmação das centrais.",
        StatusOcorrencia.ConfirmadaParcialmente => "Parte das centrais (simuladas) confirmou o recebimento. Outras ainda pendentes.",
        StatusOcorrencia.ConfirmadaPorTodas => "Todas as centrais (simuladas) pertinentes confirmaram o recebimento técnico.",
        StatusOcorrencia.FalhaNoEncaminhamento => "Falha ao entregar às centrais. Novas tentativas automáticas em andamento.",
        StatusOcorrencia.SemDestinoConfigurado => "Recebido, mas sem central configurada para este local. Ligue 193 ou 199.",
        StatusOcorrencia.EmAtendimentoSimulado => "Atendimento FICTÍCIO registrado pela central simulada.",
        StatusOcorrencia.Encerrada => "Pedido encerrado.",
        _ => s.ToString(),
    };
}
