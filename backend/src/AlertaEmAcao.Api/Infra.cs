using System.Security.Claims;
using AlertaEmAcao.CentraisSimuladas;
using AlertaEmAcao.Contratos;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AlertaEmAcao.Api;

public sealed record ChaveRetornoCentrais(string Valor);

public static class Configuracao
{
    /// <summary>Aceita ConnectionStrings:Postgres ou DATABASE_URL (formato postgres://usuario:senha@host:porta/banco).</summary>
    public static string ConnectionString(IConfiguration cfg)
    {
        var cs = cfg.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(cs)) return cs;
        var url = cfg["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(url)) return "Host=localhost;Port=5432;Database=alerta;Username=postgres;Password=postgres";
        var u = new Uri(url);
        var partes = u.UserInfo.Split(':', 2);
        var ssl = u.Host is "localhost" or "127.0.0.1" ? "Disable" : "Prefer";
        return $"Host={u.Host};Port={(u.Port > 0 ? u.Port : 5432)};Database={u.AbsolutePath.TrimStart('/')};Username={Uri.UnescapeDataString(partes[0])};" +
               $"Password={Uri.UnescapeDataString(partes.ElementAtOrDefault(1) ?? "")};SSL Mode={ssl}";
    }
}

public static class Usuarios
{
    public static Guid? Id(this ClaimsPrincipal u) => Guid.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;
    public static string? Central(this ClaimsPrincipal u) => u.FindFirstValue("central");
    public static bool Admin(this ClaimsPrincipal u) => u.IsInRole(nameof(Papel.Administrador));

    public static ClaimsPrincipal Principal(Usuario u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.Nome),
            new(ClaimTypes.Email, u.Email),
            new(ClaimTypes.Role, u.Papel.ToString()),
        };
        if (u.CentralCodigo is not null) claims.Add(new("central", u.CentralCodigo));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "bearer"));
    }
}

public sealed class TratadorErros(ILogger<TratadorErros> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception e, CancellationToken ct)
    {
        var (status, codigo) = e switch
        {
            ErroNaoEncontrado => (404, "nao_encontrado"),
            ErroAcessoNegado => (403, "acesso_negado"),
            ErroDominio d when d.Codigo is "registro_concorrente" or "email_em_uso" or "id_conflito" => (409, d.Codigo),
            ErroDominio d when d.Codigo == "credenciais_invalidas" => (401, d.Codigo),
            ErroDominio d => (400, d.Codigo),
            BadHttpRequestException => (400, "requisicao_invalida"),
            System.Text.Json.JsonException => (400, "json_invalido"),
            _ => (500, "erro_interno"),
        };
        if (status == 500) log.LogError(e, "Erro não tratado em {Caminho}", ctx.Request.Path);
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = $"https://alerta-em-acao/erros/{codigo}",
            title = status == 500 ? "Erro interno. O pedido não foi perdido se estiver salvo no aparelho; tente novamente." : e.Message,
            status,
            codigo,
            traceId = ctx.TraceIdentifier,
        }, ct);
        return true;
    }
}

public sealed class CabecalhosSeguranca(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=()";
        h.XFrameOptions = "DENY";
        h.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data: blob: https:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                                  "font-src 'self' data: https://fonts.gstatic.com; connect-src 'self' https: wss: ws:; worker-src 'self' blob:; script-src 'self'; frame-ancestors 'none'";
        return next(ctx);
    }
}

public static class Inicializacao
{
    public static async Task ExecutarAsync(IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        using var s = sp.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AlertaDbContext>();
        var sim = s.ServiceProvider.GetRequiredService<SimuladorDbContext>();
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                await sim.Database.MigrateAsync(ct);
                break;
            }
            catch (Exception e) when (tentativa < 10 && !ct.IsCancellationRequested)
            {
                log.LogWarning("Banco indisponível ({Msg}); nova tentativa em {S}s", e.Message, tentativa * 2);
                await Task.Delay(TimeSpan.FromSeconds(tentativa * 2), ct);
            }
        }
        await DadosIniciais.AplicarAsync(db, s.ServiceProvider.GetRequiredService<IHasherSenha>(), s.ServiceProvider.GetRequiredService<IRelogio>(), ct);
        await ExtensoesCentraisSimuladas.GarantirCentraisAsync(sim, DadosIniciais.Centrais().Select(c => (c.Codigo, c.Nome, c.Tipo.ToString(), c.Municipio)), ct);
        await s.ServiceProvider.GetRequiredService<ICatalogoMunicipios>().RecarregarAsync(ct);
        if (Infraestrutura.Persistencia.CifradorCampos.ChaveDeDesenvolvimento)
            log.LogWarning("Dados:ChaveCifragem não configurada — usando chave de DESENVOLVIMENTO para cifrar campos sensíveis.");
        log.LogInformation("Alerta em Ação inicializado.");
    }
}

/// <summary>Hub SignalR. Grupos: admin, central:{código}, usuario:{id}, ocorrencia:{id}.</summary>
public sealed class HubOperacao(IServiceScopeFactory escopos) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var u = Context.User;
        if (u?.Identity?.IsAuthenticated == true)
        {
            if (u.Admin()) await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
            if (u.Central() is { } c) await Groups.AddToGroupAsync(Context.ConnectionId, $"central:{c}");
            if (u.Id() is { } id) await Groups.AddToGroupAsync(Context.ConnectionId, $"usuario:{id}");
        }
        await base.OnConnectedAsync();
    }

    /// <summary>O solicitante acompanha o próprio pedido (prova de posse: identificador do dispositivo).</summary>
    public async Task<bool> AcompanharOcorrencia(Guid ocorrenciaId, string dispositivoId)
    {
        using var s = escopos.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AlertaDbContext>();
        var uid = Context.User?.Id();
        var ok = await db.Set<Ocorrencia>().AnyAsync(o => o.Id == ocorrenciaId && (o.DispositivoId == dispositivoId || (uid != null && o.UsuarioId == uid)));
        if (ok) await Groups.AddToGroupAsync(Context.ConnectionId, $"ocorrencia:{ocorrenciaId}");
        return ok;
    }

    public Task AcompanharMapaComunitario() => Groups.AddToGroupAsync(Context.ConnectionId, "comunidade");
}

public sealed class NotificadorSignalR(IHubContext<HubOperacao> hub) : INotificadorTempoReal
{
    public async Task OcorrenciaAtualizadaAsync(Guid id, string? municipio, IReadOnlyCollection<string> centrais, string motivo)
    {
        var evento = new { id, municipio, motivo, em = DateTimeOffset.UtcNow };
        var grupos = new List<string> { "admin", $"ocorrencia:{id}" };
        grupos.AddRange(centrais.Select(c => $"central:{c}"));
        await hub.Clients.Groups(grupos).SendAsync(EventosTempoReal.OcorrenciaAtualizada, evento);
        // Mapa comunitário recebe apenas o aviso de que algo mudou (sem localização nem dados pessoais).
        await hub.Clients.Group("comunidade").SendAsync(EventosTempoReal.OcorrenciaAtualizada, new { municipio, em = evento.em });
    }

    public Task AvisoPublicadoAsync(Guid avisoId, bool oficial, IReadOnlyCollection<string> municipios) =>
        hub.Clients.All.SendAsync(EventosTempoReal.AvisoPublicado, new { avisoId, oficial, municipios });

    public Task NotificacaoUsuarioAsync(Guid usuarioId, object conteudo) =>
        hub.Clients.Group($"usuario:{usuarioId}").SendAsync(EventosTempoReal.Notificacao, conteudo);

    public Task OfertaAtualizadaAsync(Guid ofertaId, string organizacaoCodigo) =>
        hub.Clients.Groups("admin", $"central:{organizacaoCodigo}").SendAsync(EventosTempoReal.OfertaAtualizada, new { ofertaId });
}
