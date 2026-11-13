using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlertaEmAcao.Infraestrutura.Adaptadores;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Notificacoes;
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

/// <summary>CEP/endereço sem internet: respostas fixas com o formato real das fontes.</summary>
public sealed class GeocodificadorFalso : IGeocodificador
{
    public bool Indisponivel { get; set; }

    public Task<EnderecoCep?> BuscarCepAsync(string cep, CancellationToken ct)
    {
        if (Indisponivel) throw new HttpRequestException("indisponível");
        return Task.FromResult<EnderecoCep?>(cep switch
        {
            "50030230" => new EnderecoCep(cep, "Rua do Bom Jesus", "Recife", "Recife", "PE"),        // geocodificável
            "53130000" => new EnderecoCep(cep, null, null, "Olinda", "PE"),                          // CEP geral do município, sem rua
            "01310100" => new EnderecoCep(cep, "Avenida Paulista", "Bela Vista", "São Paulo", "SP"),  // fora da RMR
            _ => null,
        });
    }

    public Task<IReadOnlyList<ResultadoGeocodificacao>> BuscarEnderecoAsync(string? logradouro, string? numero, string? bairro, string municipio, string uf, string? cep, CancellationToken ct)
    {
        if (Indisponivel) throw new HttpRequestException("Serviço de endereços indisponível agora.");
        IReadOnlyList<ResultadoGeocodificacao> r = (logradouro ?? "").ToLowerInvariant() switch
        {
            var l when l.Contains("bom jesus") => [new(-8.0631, -34.8711, "Rua do Bom Jesus, Recife", "Só a rua: confirme o ponto exato no mapa", true)],
            var l when l.Contains("rua da aurora") => [new(-8.0600, -34.8800, "Rua da Aurora, Boa Vista, Recife", "Só a rua: confirme o ponto exato no mapa", true),
                                                      new(-8.0010, -34.8500, "Rua da Aurora, Olinda", "Só a rua: confirme o ponto exato no mapa", true)],
            _ => [],
        };
        return Task.FromResult(r);
    }
}

/// <summary>Push configurável: por padrão "não configurado" (como produção sem VAPID); pode simular falha.</summary>
public sealed class EnviadorPushFalso : IEnviadorPush
{
    public bool Configurado { get; set; }
    public bool Falhar { get; set; }
    public int Tentativas;
    public string? ChavePublicaVapid => Configurado ? "chave-de-teste" : null;
    public Task<(bool Sucesso, bool Expirada, string Detalhe)> EnviarAsync(AssinaturaPush assinatura, ConteudoPush conteudo, CancellationToken ct)
    {
        Interlocked.Increment(ref Tentativas);
        if (Falhar) throw new HttpRequestException("serviço de push fora do ar (simulado)");
        return Task.FromResult((true, false, "aceito (simulado)"));
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
    public GeocodificadorFalso Geo { get; } = new();
    public EnviadorPushFalso Push { get; } = new();
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
        builder.UseSetting("LimiteRequisicoes:Geocodificacao", "100000");
        builder.UseSetting("LimiteRequisicoes:Convite", "100000");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IRelogio>(Relogio);
            s.AddSingleton<IProvedorIA>(IA);
            s.AddSingleton<IFonteChuva, FonteChuvaFalsa>();
            s.AddSingleton<IFonteAvisosOficiais, FonteAvisosFalsa>();
            s.AddSingleton<IGeocodificador>(Geo);
            s.AddSingleton<IEnviadorPush>(Push);
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

    /// <summary>Conta real nova (não demonstração), com consentimentos opcionais.</summary>
    public async Task<(HttpClient Cliente, Guid Id)> NovaContaAsync(string nome, object? consentimentos = null)
    {
        var c = CreateClient();
        var r = await c.PostAsJsonAsync("/api/auth/registrar", new { nome, email = $"{Guid.NewGuid():N}@teste.alerta.br", senha = "senha-segura-123", consentimentos });
        r.EnsureSuccessStatusCode();
        var token = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        c.DefaultRequestHeaders.Add("X-Dispositivo-Id", "conta-" + Guid.NewGuid().ToString("N"));
        var eu = await c.GetFromJsonAsync<JsonElement>("/api/eu");
        return (c, eu.GetProperty("id").GetGuid());
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
