using System.Security.Claims;
using System.Threading.RateLimiting;
using AlertaEmAcao.Api;
using AlertaEmAcao.Api.Endpoints;
using AlertaEmAcao.CentraisSimuladas;
using AlertaEmAcao.Contratos;
using AlertaEmAcao.Infraestrutura;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Comunicacao;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

var conexao = Configuracao.ConnectionString(cfg);
var segredoCentrais = cfg["Centrais:SegredoMestre"];
if (string.IsNullOrWhiteSpace(segredoCentrais))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testes"))
        throw new InvalidOperationException("Configure Centrais__SegredoMestre (segredo das centrais simuladas).");
    segredoCentrais = "segredo-de-desenvolvimento-centrais";
    cfg["Centrais:SegredoMestre"] = segredoCentrais;
}
var chaveRetorno = cfg["Centrais:ChaveRetorno"] ?? ChavesCentrais.Derivar(segredoCentrais, "retorno");

builder.Services.AdicionarInfraestrutura(cfg, conexao);
builder.Services.AddCentraisSimuladas(conexao, o =>
{
    o.SegredoMestre = segredoCentrais;
    o.UrlRetorno = cfg["Centrais:UrlRetorno"] ?? cfg["Centrais:UrlBaseCentrais"] ?? "http://localhost:8080";
    o.ChaveRetorno = chaveRetorno;
});
builder.Services.AddSingleton(new ChaveRetornoCentrais(chaveRetorno));

builder.Services.AddDataProtection().SetApplicationName("AlertaEmAcao");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
    new ConfigureOptions<KeyManagementOptions>(o => o.XmlRepository = new RepositorioChavesProtecao(sp.GetRequiredService<IServiceScopeFactory>())));

builder.Services.AddAuthentication(BearerTokenDefaults.AuthenticationScheme)
    .AddBearerToken(o =>
    {
        o.BearerTokenExpiration = TimeSpan.FromHours(12);
        o.RefreshTokenExpiration = TimeSpan.FromDays(7);
        o.Events.OnMessageReceived = ctx =>
        {
            // SignalR (WebSocket) envia o token pela query string.
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(EventosTempoReal.Hub)) ctx.Token = token;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Operador", p => p.RequireRole("OperadorCentral", "Administrador"))
    .AddPolicy("Organizacao", p => p.RequireRole("Organizacao", "Administrador"))
    .AddPolicy("Administrador", p => p.RequireRole("Administrador"));

builder.Services.AddSignalR();
builder.Services.AddSingleton<INotificadorTempoReal, NotificadorSignalR>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorErros>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
});
builder.Services.AddCors(o => o.AddPolicy("dev", p => p
    .WithOrigins((cfg["Cors:Origens"] ?? "http://localhost:5173").Split(',', StringSplitOptions.RemoveEmptyEntries))
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    RateLimitPartition<string> PorIp(HttpContext c, int limite, TimeSpan janela) => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "desconhecido", _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = janela });
    o.AddPolicy("autenticacao", c => PorIp(c, cfg.GetValue("LimiteRequisicoes:Autenticacao", 30), TimeSpan.FromMinutes(1)));
    // Pedido de socorro: limite generoso (sincronização offline envia vários de uma vez).
    o.AddPolicy("voz", c => PorIp(c, cfg.GetValue("LimiteRequisicoes:Voz", 40), TimeSpan.FromMinutes(1)));
    // CEP/endereço: consulta explícita (sem autocompletar), protegendo ViaCEP e Nominatim.
    o.AddPolicy("geocodificacao", c => PorIp(c, cfg.GetValue("LimiteRequisicoes:Geocodificacao", 20), TimeSpan.FromMinutes(1)));
    // Convites: limita tentativas de adivinhar códigos.
    o.AddPolicy("convite", c => PorIp(c, cfg.GetValue("LimiteRequisicoes:Convite", 10), TimeSpan.FromMinutes(10)));
    o.AddPolicy("socorro", c => PorIp(c, cfg.GetValue("LimiteRequisicoes:Socorro", 120), TimeSpan.FromMinutes(1)));
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<CabecalhosSeguranca>();
if (app.Environment.IsDevelopment()) app.UseCors("dev");
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Service worker e manifest nunca em cache longo; assets com hash podem ser cacheados.
        var nome = ctx.File.Name;
        ctx.Context.Response.Headers.CacheControl = nome is "sw.js" or "index.html" or "manifest.webmanifest" or "registerSW.js"
            ? "no-cache" : nome.Contains('-') ? "public, max-age=31536000, immutable" : "public, max-age=3600";
    },
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapHub<HubOperacao>(EventosTempoReal.Hub);
app.MapCentraisSimuladas();
app.MapearEndpoints();
app.MapFallbackToFile("index.html");

await Inicializacao.ExecutarAsync(app.Services, app.Logger, app.Lifetime.ApplicationStopping);
app.Run();

public partial class Program;
