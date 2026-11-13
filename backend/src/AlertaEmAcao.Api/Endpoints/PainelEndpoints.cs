using System.Security.Claims;
using System.Text.Json.Nodes;
using AlertaEmAcao.CentraisSimuladas;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record VerificacaoRequest(VerificacaoOcorrencia Verificacao, string? Nota);

public static class PainelEndpoints
{
    public static void MapPainel(this WebApplication app)
    {
        var g = app.MapGroup("/api/painel").WithTags("Painel de coordenação (demonstração)").RequireAuthorization("Operador");

        g.MapGet("/ocorrencias", async (ClaimsPrincipal u, AlertaDbContext db, string? municipio, CancellationToken ct) =>
        {
            var ids = await IdsVisiveis(u, db, ct);
            var q = db.Set<Ocorrencia>().AsNoTracking().Where(o => ids == null || ids.Contains(o.Id));
            if (municipio is not null) q = q.Where(o => o.MunicipioCodigo == municipio);
            var lista = await q.OrderByDescending(o => o.RecebidaNoServidorEm).Take(200).ToListAsync(ct);
            var oids = lista.Select(o => o.Id).ToList();
            var analises = await db.Set<AnaliseOcorrencia>().AsNoTracking().Where(a => oids.Contains(a.OcorrenciaId)).ToDictionaryAsync(a => a.OcorrenciaId, ct);
            var encs = (await db.Set<EncaminhamentoInstitucional>().AsNoTracking().Where(e => oids.Contains(e.ReferenciaId) && e.TipoMensagem == TipoMensagem.Ocorrencia).ToListAsync(ct))
                .GroupBy(e => e.ReferenciaId).ToDictionary(x => x.Key, x => x.ToList());
            var precisa = !u.Admin();
            return Results.Ok(new
            {
                aviso = ExtensoesCentraisSimuladas.Aviso,
                escopo = u.Admin() ? "Administrador: localização aproximada, sem relato (minimização)." : $"Central {u.Central()}: ocorrências encaminhadas a esta central.",
                ocorrencias = lista.Select(o =>
                {
                    var a = analises.GetValueOrDefault(o.Id);
                    var p = o.Ponto;
                    var pt = p is null ? null : precisa ? p : p.Value.Aproximar();
                    return new
                    {
                        o.Id, status = o.Status.ToString(), o.RecebidaNoServidorEm, o.CriadaNoDispositivoEm, o.MunicipioCodigo,
                        municipioNome = MunicipiosRmr.Nome(o.MunicipioCodigo), latitude = pt?.Latitude, longitude = pt?.Longitude,
                        localizacaoAproximada = !precisa, canal = o.Canal.ToString(), verificacao = o.Verificacao.ToString(),
                        pessoas = o.Complemento.QuantidadePessoas ?? o.Complemento.Pessoas?.Count,
                        nivel = a?.NivelMaximo?.ToString(), sinalizacoes = a?.Sinalizacoes.Select(s => new { s.Codigo, nivel = s.Nivel.ToString(), s.Titulo }),
                        statusIA = a?.StatusIA.ToString(),
                        encaminhamentos = encs.GetValueOrDefault(o.Id)?.Select(e => new { central = e.InstituicaoCodigo, status = e.Status.ToString(), e.Protocolo }),
                    };
                }),
            });
        });

        g.MapGet("/ocorrencias/{id:guid}", async (Guid id, ClaimsPrincipal u, AlertaDbContext db, IRelogio relogio, CancellationToken ct) =>
        {
            var ids = await IdsVisiveis(u, db, ct);
            if (ids is not null && !ids.Contains(id)) return Results.Forbid();
            var o = await db.Set<Ocorrencia>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return Results.NotFound();
            var a = await db.Set<AnaliseOcorrencia>().AsNoTracking().FirstOrDefaultAsync(x => x.OcorrenciaId == id, ct);
            var encs = await db.Set<EncaminhamentoInstitucional>().AsNoTracking().Where(e => e.ReferenciaId == id).OrderBy(e => e.CriadoEm).ToListAsync(ct);
            var insts = await db.Set<Instituicao>().AsNoTracking().ToDictionaryAsync(i => i.Id, ct);
            var precisa = !u.Admin();
            // Auditoria de acesso a dados pessoais/localização.
            db.Add(new RegistroAuditoria { Em = relogio.Agora, UsuarioId = u.Id()?.ToString(), Papel = u.Central() ?? "admin", Acao = precisa ? "ver_ocorrencia_detalhada" : "ver_ocorrencia_minimizada", Recurso = $"ocorrencia:{id}" });
            await db.SaveChangesAsync(ct);
            var p = o.Ponto;
            var pt = p is null ? null : precisa ? p : p.Value.Aproximar();
            return Results.Ok(new
            {
                aviso = ExtensoesCentraisSimuladas.Aviso,
                ocorrencia = new
                {
                    o.Id, status = o.Status.ToString(), statusDescricao = OcorrenciasEndpoints.DescreverStatus(o.Status), o.RecebidaNoServidorEm, o.CriadaNoDispositivoEm,
                    o.MunicipioCodigo, municipioNome = MunicipiosRmr.Nome(o.MunicipioCodigo), canal = o.Canal.ToString(), o.RetransmitidaPor,
                    verificacao = o.Verificacao.ToString(), o.Demonstracao, autenticado = o.UsuarioId is not null,
                    localizacao = pt is null ? null : new { latitude = pt.Value.Latitude, longitude = pt.Value.Longitude, o.PrecisaoMetros, lidaEm = o.LocalizacaoLidaEm, aproximada = !precisa },
                    trilha = precisa ? o.TrilhaLocalizacao.TakeLast(20) : null,
                    complemento = precisa ? o.Complemento : null,
                    historico = o.Historico.OrderBy(h => h.Em),
                },
                analise = a is null ? null : new
                {
                    a.VersaoRegras, nivelMaximo = a.NivelMaximo?.ToString(), a.Sinalizacoes, a.Contexto, a.Fontes, a.RegrasAvaliadasEm,
                    ia = new { status = a.StatusIA.ToString(), modelo = a.ModeloIA, resultado = precisa ? a.ResultadoIA : null, problemas = a.ProblemasValidacaoIA, a.IAConcluidaEm, a.DuracaoIAMs },
                    observacao = "Sinalizações para avaliação humana. Não constituem decisão de prioridade nem ordem de atendimento.",
                },
                encaminhamentos = encs.Select(e => new
                {
                    e.Id, tipo = e.TipoMensagem.ToString(), central = e.InstituicaoCodigo, centralNome = insts.GetValueOrDefault(e.InstituicaoId)?.Nome,
                    e.Justificativa, status = e.Status.ToString(), e.Tentativas, e.Protocolo, e.ConfirmadoEm, e.UltimoErro, e.ProximaTentativaEm,
                    e.EstadoNaCentral, historicoTentativas = e.HistoricoTentativas,
                }),
            });
        });

        g.MapPost("/ocorrencias/{id:guid}/verificacao", async (Guid id, VerificacaoRequest r, ClaimsPrincipal u, AlertaDbContext db, IUnidadeTrabalho uow, IRelogio relogio,
            Modulos.Comunicacao.INotificadorTempoReal rt, CancellationToken ct) =>
        {
            var ids = await IdsVisiveis(u, db, ct);
            if (ids is not null && !ids.Contains(id)) return Results.Forbid();
            var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == id), ct) ?? throw new ErroNaoEncontrado("Ocorrência não encontrada.");
            o.Verificar(r.Verificacao, u.Central() ?? "admin", r.Nota, relogio.Agora);
            await uow.SalvarAsync(ct);
            await rt.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, [], "verificacao");
            return Results.Ok(new { o.Id, verificacao = o.Verificacao.ToString() });
        });

        g.MapPost("/encaminhamentos/{id:guid}/reenviar", async (Guid id, ClaimsPrincipal u, AlertaDbContext db, ServicoDespacho s, CancellationToken ct) =>
        {
            var e = await db.Set<EncaminhamentoInstitucional>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            if (!u.Admin() && u.Central() != e.InstituicaoCodigo) return Results.Forbid();
            await s.ReenviarAsync(id, ct);
            return Results.Accepted();
        });

        g.MapGet("/resumo", async (ClaimsPrincipal u, AlertaDbContext db, SimuladorDbContext sim, IRelogio relogio, CancellationToken ct) =>
        {
            var ids = await IdsVisiveis(u, db, ct);
            var desde = relogio.Agora.AddHours(-24);
            var q = db.Set<Ocorrencia>().AsNoTracking().Where(o => o.RecebidaNoServidorEm >= desde && (ids == null || ids.Contains(o.Id)));
            var porStatus = await q.GroupBy(o => o.Status).Select(x => new { status = x.Key.ToString(), total = x.Count() }).ToListAsync(ct);
            var encs = await db.Set<EncaminhamentoInstitucional>().AsNoTracking().Where(e => e.CriadoEm >= desde && (u.Admin() || e.InstituicaoCodigo == u.Central()))
                .GroupBy(e => e.Status).Select(x => new { status = x.Key.ToString(), total = x.Count() }).ToListAsync(ct);
            var centrais = await sim.Centrais.AsNoTracking().Select(c => new { c.Codigo, c.Nome, modo = c.Modo.ToString(), c.MunicipioCodigo, c.AtrasoMs }).ToListAsync(ct);
            return Results.Ok(new { janelaHoras = 24, porStatus, encaminhamentos = encs, centrais = u.Admin() ? centrais : centrais.Where(c => c.Codigo == u.Central()).ToList() });
        });

        // --- Modo demonstração (administrador) ---
        var demo = app.MapGroup("/api/demo").WithTags("Modo demonstração").RequireAuthorization("Administrador");

        demo.MapPost("/avisos", async (ComandoAvisoDemonstrativo c, ServicoAlertas s, CancellationToken ct) =>
        {
            var a = await s.PublicarDemonstrativoAsync(c, ct);
            return Results.Created($"/api/alertas/{a.Id}", new { a.Id, a.Titulo, a.InicioEm, a.FimEm, oficial = false });
        });

        demo.MapPost("/avisos/processar", async (ServicoAlertas s, CancellationToken ct) => Results.Ok(new { notificacoes = await s.ProcessarNovosAsync(ct) }));

        demo.MapGet("/notificacoes", async (AlertaDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Set<Notificacao>().AsNoTracking().OrderByDescending(n => n.CriadaEm).Take(100)
                .Select(n => new { n.Id, n.AvisoId, n.UsuarioId, n.CriadaEm, n.LidaEm, resposta = n.Resposta.HasValue ? n.Resposta.Value.ToString() : null, n.Entregas }).ToListAsync(ct)));

        // Chave do gateway SIMULADO/Nearby para um receptor de teste (usada na PoC Android).
        demo.MapGet("/gateway-chave", (string receptor, IConfiguration cfg) =>
            Results.Ok(new { receptor, chave = AlertaEmAcao.Contratos.ChavesCentrais.Derivar(cfg["Centrais:SegredoMestre"]!, "gateway:" + receptor) }));

        demo.MapGet("/auditoria", async (AlertaDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Auditoria.AsNoTracking().OrderByDescending(a => a.Em).Take(200).ToListAsync(ct)));
    }

    /// <summary>null = todas (administrador, com minimização); senão, ocorrências encaminhadas à central do operador.</summary>
    public static async Task<List<Guid>?> IdsVisiveis(ClaimsPrincipal u, AlertaDbContext db, CancellationToken ct)
    {
        if (u.Admin()) return null;
        var central = u.Central();
        if (central is null) return [];
        return await db.Set<EncaminhamentoInstitucional>().AsNoTracking()
            .Where(e => e.InstituicaoCodigo == central && (e.TipoMensagem == TipoMensagem.Ocorrencia || e.TipoMensagem == TipoMensagem.AtualizacaoOcorrencia))
            .Select(e => e.ReferenciaId).Distinct().ToListAsync(ct);
    }
}
