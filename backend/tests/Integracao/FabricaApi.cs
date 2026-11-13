using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.Infraestrutura.Adaptadores;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Monitoramento;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AlertaEmAcao.Testes.Integracao;

public sealed class RelogioControlado : IRelogio
{
    private TimeSpan _deslocamento = TimeSpan.Zero;
    public DateTimeOffset Agora => DateTimeOffset.UtcNow + _deslocamento;
    public void Avancar(TimeSpan t) => _deslocamento += t;
}

public sealed class ProvedorIAFalso : IProvedorIA
{
    public Func<EntradaAnaliseIA, string>? Resposta { get; set; }
    public bool Falhar { get; set; }
    public int Chamadas { get; private set; }
    public bool Configurado => true;
    public string Descricao => "IA falsa para testes";

    public Task<RespostaBrutaIA> AnalisarAsync(EntradaAnaliseIA entrada, CancellationToken ct)
    {
        Chamadas++;
        if (Falhar) throw new HttpRequestException("Provedor de IA fora do ar (simulado no teste)");
        return Task.FromResult(new RespostaBrutaIA(Resposta?.Invoke(entrada) ?? "{}", "modelo-falso", "teste", 5));
    }
}

public sealed class FonteChuvaFalsa(IRelogio relogio) : IFonteChuva
{
    public Task<ResultadoFonteChuva> ObterRmrAsync(CancellationToken ct) => Task.FromResult(new ResultadoFonteChuva(
    [
        new LeituraChuva("t1", "[TESTE] Estação Várzea", -8.036, -34.98, "Recife", "2611606", relogio.Agora.AddMinutes(-15), 2, 5, 8, 12, 30, 45, 60, "APAC/SIRH (amostra de teste)"),
    ], relogio.Agora, true, null, "APAC (teste)", "http://teste"));
}

public sealed class FonteAvisosFalsa : IFonteAvisosOficiais
{
    public Task<(List<Aviso> Avisos, bool Disponivel, string? Erro)> ObterAsync(CancellationToken ct) => Task.FromResult((new List<Aviso>(), false, (string?)"desativada em teste"));
}

/// <summary>
/// Sobe a API completa (incluindo centrais simuladas) contra um PostgreSQL+PostGIS real, num banco exclusivo.
/// As chamadas às centrais passam por HTTP real (pipeline ASP.NET via TestServer).
/// </summary>
public sealed class FabricaApi : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly string Base = Environment.GetEnvironmentVariable("TESTES_POSTGRES") ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres";
    private readonly string _banco = $"alerta_teste_{Guid.NewGuid():N}"[..28];
    public RelogioControlado Relogio { get; } = new();
    public ProvedorIAFalso IA { get; } = new();
    public string Conexao => $"{Base};Database={_banco}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:Postgres", Conexao);
        builder.UseSetting("Trabalhos:Habilitados", "false");
        builder.UseSetting("Centrais:SegredoMestre", "segredo-de-teste");
        builder.UseSetting("Centrais:UrlBaseCentrais", "http://localhost");
        builder.UseSetting("Centrais:UrlRetorno", "http://localhost");
        builder.UseSetting("IA:Provedor", "teste");
        builder.UseSetting("LimiteRequisicoes:Autenticacao", "100000");
        builder.UseSetting("LimiteRequisicoes:Socorro", "100000");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IRelogio>(Relogio);
            s.AddSingleton<IProvedorIA>(IA);
            s.AddSingleton<IFonteChuva, FonteChuvaFalsa>();
            s.AddSingleton<IFonteAvisosOficiais, FonteAvisosFalsa>();
            s.AddHttpClient<IClienteCentral, ClienteCentralHttp>().ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
            s.AddHttpClient("retorno-centrais").ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
        });
    }

    public async Task InitializeAsync()
    {
        await using var c = new NpgsqlConnection(Base + ";Database=postgres");
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {_banco}", c);
        await cmd.ExecuteNonQueryAsync();
        _ = Server; // inicia o host (migrações + dados iniciais)
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(Base + ";Database=postgres");
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_banco} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<HttpClient> ClienteDemoAsync(string perfil)
    {
        var c = CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/demo", new { perfil });
        r.EnsureSuccessStatusCode();
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public HttpClient ClienteDispositivo(string dispositivo)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add("X-Dispositivo-Id", dispositivo);
        return c;
    }

    public async Task<int> DespacharAsync()
    {
        var total = 0;
        for (var i = 0; i < 5; i++)
        {
            using var s = Services.CreateScope();
            var n = await s.ServiceProvider.GetRequiredService<ServicoDespacho>().ProcessarPendentesAsync(50, CancellationToken.None);
            total += n;
            if (n == 0) break;
        }
        return total;
    }

    public async Task<T> NoEscopoAsync<T>(Func<IServiceProvider, Task<T>> f)
    {
        using var s = Services.CreateScope();
        return await f(s.ServiceProvider);
    }
}

[CollectionDefinition("api")]
public sealed class ColecaoApi : ICollectionFixture<FabricaApi>;
