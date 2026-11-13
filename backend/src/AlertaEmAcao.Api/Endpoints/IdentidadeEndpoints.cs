using System.Security.Claims;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.AspNetCore.Authentication.BearerToken;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record RegistroRequest(string Nome, string Email, string? Telefone, string Senha, string? MunicipioCodigo);
public sealed record EntrarRequest(string Email, string Senha);
public sealed record DemoRequest(string Perfil);
public sealed record PerfilRequest(string? Nome, string? Telefone, string? MunicipioCodigo, string? EnderecoReferencia, bool ConsenteAlertas, bool ConsenteLocalizacao, bool ReceberPush);
public sealed record FamiliaRequest(List<IntegranteFamilia> Integrantes, List<ContatoReferencia>? Contatos);
public sealed record LocalRequest(string Apelido, double Latitude, double Longitude);
public sealed record RespostaNotificacaoRequest(RespostaNotificacao Resposta);
public sealed record AssinaturaPushRequest(string Endpoint, string P256dh, string Auth);

public static class IdentidadeEndpoints
{
    public static readonly Dictionary<string, string> PerfisDemo = new()
    {
        ["cidadao"] = "cidada", ["cidadao-olinda"] = "cidadao.olinda", ["voluntario"] = "voluntaria",
        ["defesa-civil-recife"] = "operador.dc.recife", ["defesa-civil-olinda"] = "operador.dc.olinda",
        ["defesa-civil-ipojuca"] = "operador.dc.ipojuca", ["defesa-civil-jaboatao"] = "operador.dc.jaboatao",
        ["bombeiros"] = "operador.bombeiros", ["samu"] = "operador.samu", ["organizacao"] = "org.maos", ["admin"] = "admin",
    };

    public static void MapIdentidade(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Identidade").RequireRateLimiting("autenticacao");

        auth.MapPost("/registrar", async (RegistroRequest r, ServicoIdentidade s, CancellationToken ct) =>
        {
            var u = await s.RegistrarAsync(r.Nome, r.Email, r.Telefone, r.Senha, r.MunicipioCodigo, ct);
            return Results.SignIn(Usuarios.Principal(u), authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
        });

        auth.MapPost("/entrar", async (EntrarRequest r, ServicoIdentidade s, CancellationToken ct) =>
        {
            var u = await s.AutenticarAsync(r.Email, r.Senha, ct);
            return Results.SignIn(Usuarios.Principal(u), authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
        });

        // Atalho do modo demonstração: entra numa conta FICTÍCIA pré-cadastrada.
        auth.MapPost("/demo", async (DemoRequest r, IUnidadeTrabalho uow, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!cfg.GetValue("Demo:Habilitado", true)) return Results.NotFound();
            if (!PerfisDemo.TryGetValue(r.Perfil, out var login)) return Results.BadRequest(new { erro = "Perfil de demonstração inexistente.", perfis = PerfisDemo.Keys });
            var email = $"{login}@{DadosIniciais.DominioDemo}";
            var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Email == email && x.ContaDemonstracao), ct);
            return u is null ? Results.NotFound() : Results.SignIn(Usuarios.Principal(u), authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
        });

        auth.MapGet("/perfis-demo", () => Results.Ok(PerfisDemo.Keys));

        var eu = app.MapGroup("/api/eu").WithTags("Identidade").RequireAuthorization();

        eu.MapGet("/", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var id = p.Id()!.Value;
            var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Id == id), ct) ?? throw new ErroNaoEncontrado("Usuário não encontrado.");
            return Results.Ok(new
            {
                u.Id, u.Nome, u.Email, u.Telefone, papel = u.Papel.ToString(), u.CentralCodigo, u.MunicipioCodigo, u.EnderecoReferencia,
                u.ConsenteAlertas, u.ConsenteLocalizacao, u.ReceberPush, u.ContaDemonstracao,
            });
        });

        eu.MapPut("/perfil", async (PerfilRequest r, ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var id = p.Id()!.Value;
            var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Id == id), ct) ?? throw new ErroNaoEncontrado("Usuário não encontrado.");
            u.AtualizarPerfil(r.Nome, r.Telefone, r.MunicipioCodigo, r.EnderecoReferencia, r.ConsenteAlertas, r.ConsenteLocalizacao, r.ReceberPush);
            await uow.SalvarAsync(ct);
            return Results.NoContent();
        });

        eu.MapGet("/familia", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var id = p.Id()!.Value;
            var g = await uow.PrimeiroAsync(uow.Consulta<GrupoFamiliar>().Where(x => x.UsuarioId == id), ct);
            return Results.Ok(new { integrantes = g?.Integrantes ?? [], contatos = g?.Contatos ?? [], atualizadoEm = g?.AtualizadoEm });
        });

        eu.MapPut("/familia", async (FamiliaRequest r, ClaimsPrincipal p, ServicoIdentidade s, CancellationToken ct) =>
        {
            var g = await s.DefinirFamiliaAsync(p.Id()!.Value, r.Integrantes ?? [], r.Contatos ?? [], ct);
            return Results.Ok(new { g.Quantidade, g.AtualizadoEm });
        });

        eu.MapGet("/locais", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var id = p.Id()!.Value;
            var l = await uow.ListarAsync(uow.Consulta<LocalMonitorado>().Where(x => x.UsuarioId == id && x.Ativo).OrderBy(x => x.CriadoEm), ct);
            return Results.Ok(l.Select(x => new { x.Id, x.Apelido, x.Latitude, x.Longitude, x.MunicipioCodigo, x.CriadoEm }));
        });

        eu.MapPost("/locais", async (LocalRequest r, ClaimsPrincipal p, ServicoIdentidade s, CancellationToken ct) =>
        {
            var l = await s.AdicionarLocalAsync(p.Id()!.Value, r.Apelido, r.Latitude, r.Longitude, ct);
            return Results.Created($"/api/eu/locais/{l.Id}", new { l.Id, l.Apelido, l.MunicipioCodigo });
        });

        eu.MapDelete("/locais/{id:guid}", async (Guid id, ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var l = await uow.PrimeiroAsync(uow.Consulta<LocalMonitorado>().Where(x => x.Id == id && x.UsuarioId == uid), ct) ?? throw new ErroNaoEncontrado("Local não encontrado.");
            l.Desativar();
            await uow.SalvarAsync(ct);
            return Results.NoContent();
        });

        eu.MapGet("/notificacoes", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var ns = await uow.ListarAsync(uow.Consulta<Notificacao>().Where(n => n.UsuarioId == uid).OrderByDescending(n => n.CriadaEm).Take(50), ct);
            var avisoIds = ns.Select(n => n.AvisoId).ToList();
            var avisos = (await uow.ListarAsync(uow.Consulta<Aviso>().Where(a => avisoIds.Contains(a.Id)), ct)).ToDictionary(a => a.Id);
            return Results.Ok(ns.Select(n => new
            {
                n.Id, n.Titulo, n.Mensagem, n.Pergunta, n.AvisoOficial, n.CriadaEm, n.LidaEm, resposta = n.Resposta?.ToString(), n.RespondidaEm, n.Entregas,
                aviso = avisos.TryGetValue(n.AvisoId, out var a) ? new { a.Id, a.Titulo, a.Severidade, a.FimEm, a.Instrucoes, a.FonteNome, oficial = a.Oficial } : null,
            }));
        });

        eu.MapPost("/notificacoes/{id:guid}/resposta", async (Guid id, RespostaNotificacaoRequest r, ClaimsPrincipal p, ServicoNotificacoes s, CancellationToken ct) =>
        {
            var n = await s.ResponderAsync(id, p.Id()!.Value, r.Resposta, ct);
            return Results.Ok(new { n.Id, resposta = n.Resposta?.ToString(), n.RespondidaEm });
        });

        eu.MapPost("/push/assinatura", async (AssinaturaPushRequest r, ClaimsPrincipal p, IUnidadeTrabalho uow, IRelogio relogio, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var existente = await uow.PrimeiroAsync(uow.Consulta<AssinaturaPush>().Where(a => a.Endpoint == r.Endpoint), ct);
            if (existente is not null) uow.Remover(existente);
            uow.Adicionar(AssinaturaPush.Criar(uid, r.Endpoint, r.P256dh, r.Auth, relogio.Agora));
            await uow.SalvarAsync(ct);
            return Results.NoContent();
        });

        eu.MapGet("/pedidos", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var os = await uow.ListarAsync(uow.Consulta<Ocorrencia>().Where(o => o.UsuarioId == uid).OrderByDescending(o => o.RecebidaNoServidorEm).Take(30), ct);
            return Results.Ok(os.Select(o => new { o.Id, status = o.Status.ToString(), o.RecebidaNoServidorEm, o.MunicipioCodigo }));
        });

        // LGPD, art. 18: cópia dos dados pessoais do titular em formato estruturado.
        eu.MapGet("/exportar", async (ClaimsPrincipal p, IUnidadeTrabalho uow, IRelogio relogio, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Id == uid), ct) ?? throw new ErroNaoEncontrado("Usuário não encontrado.");
            var familia = await uow.PrimeiroAsync(uow.Consulta<GrupoFamiliar>().Where(x => x.UsuarioId == uid), ct);
            var locais = await uow.ListarAsync(uow.Consulta<LocalMonitorado>().Where(x => x.UsuarioId == uid), ct);
            var pedidos = await uow.ListarAsync(uow.Consulta<Ocorrencia>().Where(o => o.UsuarioId == uid), ct);
            var notificacoes = await uow.ListarAsync(uow.Consulta<Notificacao>().Where(n => n.UsuarioId == uid), ct);
            return Results.Json(new
            {
                geradoEm = relogio.Agora,
                usuario = new { u.Id, u.Nome, u.Email, u.Telefone, papel = u.Papel.ToString(), u.MunicipioCodigo, u.EnderecoReferencia, u.ConsenteAlertas, u.ConsenteLocalizacao, u.ReceberPush },
                familia = familia is null ? null : new { familia.Integrantes, familia.Contatos, familia.AtualizadoEm },
                locais = locais.Select(x => new { x.Id, x.Apelido, x.Latitude, x.Longitude, x.MunicipioCodigo, x.Ativo, x.CriadoEm }),
                pedidos = pedidos.Select(o => new { o.Id, status = o.Status.ToString(), o.RecebidaNoServidorEm, o.MunicipioCodigo, o.Complemento, o.Historico }),
                notificacoes = notificacoes.Select(n => new { n.Id, n.Titulo, n.CriadaEm, resposta = n.Resposta?.ToString(), n.RespondidaEm }),
            });
        });
    }
}
