using System.Security.Claims;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Notificacoes;

namespace AlertaEmAcao.Api.Endpoints;

public static class AlagamentoEndpoints
{
    public static object Visao(IndicadorAlagamento i, ExplicacaoIndicador e) => new
    {
        regiao = i.Regiao,
        nivel = i.Nivel.ToString(),
        rotulo = i.Rotulo,
        explicacao = e.Texto,
        explicacaoOrigem = e.Origem,
        explicacaoObservacao = e.Motivo,
        justificativa = i.Justificativa,
        estacao = i.Estacao,
        intensidade = i.IntensidadeDescricao,
        tendencia = i.TendenciaDescricao,
        avisos = i.Avisos,
        ocorrenciasVerificadas = i.OcorrenciasVerificadas,
        relatosNaoVerificados = i.RelatosNaoVerificados,
        suscetibilidadeDocumentada = i.SuscetibilidadeDocumentada,
        estacoesDivergentes = i.EstacoesDivergentes,
        estimativaTemporal = i.Estimativa,
        separacao = i.Separacao,
        confiabilidade = i.Confiabilidade,
        limitacoes = i.Limitacoes,
        orientacoes = i.Orientacoes,
        avaliadoEm = i.AvaliadoEm,
        validoAte = i.ValidoAte,
        criterios = i.VersaoCriterios,
        fontes = new[] { "APAC/SIRH — monitoramento de chuvas (leituras de estações)", "INMET — avisos meteorológicos", "Alerta em Ação — ocorrências verificadas por operador (ambiente demonstrativo)", "Prefeitura do Recife — áreas de risco (quando aplicável)" },
    };

    public static void MapAlagamento(this WebApplication app)
    {
        // Público: a posição vai só na consulta (não é armazenada). Também usado para locais monitorados (coordenadas do local).
        app.MapGet("/api/territorio/alagamento", async (double lat, double lon, string? regiao, ServicoIndicadorAlagamento s, CancellationToken ct) =>
        {
            if (!PontoGeo.TentarCriar(lat, lon, out var p)) return Results.BadRequest(new { codigo = "coordenadas_invalidas", erro = "Coordenadas inválidas." });
            var i = await s.AvaliarAsync(p, string.IsNullOrWhiteSpace(regiao) ? null : regiao.Trim()[..Math.Min(80, regiao.Trim().Length)], ct);
            return Results.Ok(Visao(i, await s.ExplicarAsync(i, ct)));
        }).WithTags("Território").RequireRateLimiting("voz");

        var eu = app.MapGroup("/api/eu").RequireAuthorization().WithTags("Notificações preventivas");
        eu.MapGet("/notificacoes-preventivas", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var l = await uow.ListarAsync(uow.Consulta<NotificacaoPreventiva>().Where(n => n.UsuarioId == uid).OrderByDescending(n => n.CriadaEm).Take(50), ct);
            return Results.Ok(l.Select(n => new { n.Id, n.LocalId, nivel = n.Nivel.ToString(), n.Titulo, n.Mensagem, n.Url, n.CriadaEm, n.LidaEm, n.Entregas }));
        });
        eu.MapPost("/notificacoes-preventivas/{id:guid}/lida", async (Guid id, ClaimsPrincipal p, IUnidadeTrabalho uow, IRelogio relogio, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var n = await uow.PrimeiroAsync(uow.Consulta<NotificacaoPreventiva>().Where(x => x.Id == id && x.UsuarioId == uid), ct) ?? throw new ErroNaoEncontrado("Notificação não encontrada.");
            n.MarcarLida(relogio.Agora);
            await uow.SalvarAsync(ct);
            return Results.NoContent();
        });
    }
}
