using AlertaEmAcao.Infraestrutura.Adaptadores;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Infraestrutura.Seguranca;
using AlertaEmAcao.Infraestrutura.Trabalhos;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Abrigos;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlertaEmAcao.Infraestrutura;

public static class InjecaoDependencias
{
    public const string UserAgent = "AlertaEmAcao/1.0 (+https://github.com/edufrannca/Acaoalerta)";

    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection s, IConfiguration cfg, string connectionString)
    {
        CifradorCampos.Configurar(cfg["Dados:ChaveCifragem"]);

        s.AddDbContext<AlertaDbContext>(o => o.UseNpgsql(connectionString, n =>
        {
            n.MigrationsHistoryTable("__historico_migracoes", "alerta");
            n.EnableRetryOnFailure(3);
        }));
        s.AddScoped<IUnidadeTrabalho, UnidadeTrabalhoEf>();
        s.AddScoped<IConsultaEspacial, ConsultaEspacialPostgis>();
        s.TryAddSingleton<IRelogio, RelogioSistema>();
        s.AddSingleton<IRegistroFontes, RegistroFontes>();
        s.AddSingleton<ICatalogoMunicipios, CatalogoMunicipios>();
        s.AddSingleton<IHasherSenha, HasherSenhaPbkdf2>();

        s.Configure<OpcoesIntegracaoCentrais>(cfg.GetSection("Centrais"));
        s.Configure<OpcoesIA>(cfg.GetSection("IA"));
        s.Configure<OpcoesWebPush>(cfg.GetSection("WebPush"));

        void Http<TInterface, TImpl>(TimeSpan timeout) where TInterface : class where TImpl : class, TInterface =>
            s.AddHttpClient<TInterface, TImpl>(c => { c.Timeout = timeout; c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent); });

        // Fontes públicas reais
        s.AddHttpClient("nominatim", c => { c.Timeout = TimeSpan.FromSeconds(8); c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent); });
        s.AddHttpClient("viacep", c => { c.Timeout = TimeSpan.FromSeconds(8); c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent); });
        s.AddMemoryCache();
        s.TryAddSingleton<Modulos.Localizacao.IGeocodificador, GeocodificadorPublico>();
        s.AddHttpClient(nameof(FonteChuvaApac), c => { c.Timeout = TimeSpan.FromSeconds(15); c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent); });
        s.AddSingleton<IFonteChuva>(sp => sp.GetRequiredService<FonteChuvaApacSingleton>().Instancia);
        s.AddSingleton<FonteChuvaApacSingleton>();
        Http<IFonteLimitesMunicipais, FonteLimitesApac>(TimeSpan.FromSeconds(25));
        Http<IFonteAvisosOficiais, FonteAvisosInmet>(TimeSpan.FromSeconds(25));
        Http<IFonteAreasRisco, FonteAreasRiscoRecife>(TimeSpan.FromSeconds(30));
        Http<IFonteAbrigos, FonteAbrigosCopRecife>(TimeSpan.FromSeconds(50));

        // Integrações
        Http<IClienteCentral, ClienteCentralHttp>(TimeSpan.FromSeconds(40));
        Http<IEnviadorPush, EnviadorWebPush>(TimeSpan.FromSeconds(15));
        var provedor = (cfg["IA:Provedor"] ?? "").Trim().ToLowerInvariant();
        if (provedor == "anthropic") Http<IProvedorIA, ProvedorIAAnthropic>(TimeSpan.FromSeconds(50));
        else if (provedor is "openai" or "openai-compat" or "groq")
        {
            Http<IProvedorIA, ProvedorIACompativelOpenAI>(TimeSpan.FromSeconds(50));
            Http<Modulos.InteligenciaTerritorial.IExplicadorIA, ProvedorIACompativelOpenAI>(TimeSpan.FromSeconds(15));
        }
        else s.TryAddSingleton<IProvedorIA, ProvedorIANulo>();
        s.TryAddSingleton<Modulos.InteligenciaTerritorial.IExplicadorIA, Modulos.InteligenciaTerritorial.ExplicadorIANulo>();

        // Casos de uso
        s.AddScoped<ServicoTerritorio>();
        s.AddScoped<ServicoAnalise>();
        s.AddScoped<ServicoOcorrencias>();
        s.AddScoped<ServicoDespacho>();
        s.AddScoped<ServicoAlertas>();
        s.AddScoped<ServicoNotificacoes>();
        s.AddScoped<ServicoApoio>();
        s.AddScoped<ServicoIdentidade>();
        s.AddScoped<ServicoConsentimentos>();
        s.AddScoped<ServicoVinculos>();
        s.AddScoped<ServicoNotificacaoFamiliar>();
        s.AddScoped<Modulos.InteligenciaTerritorial.ServicoIndicadorAlagamento>();
        s.AddScoped<Modulos.Notificacoes.ServicoAlertaPreventivo>();
        s.AddScoped<ServicoFontesReferencia>();
        s.TryAddSingleton<INotificadorTempoReal, NotificadorNulo>();

        if (cfg.GetValue("Trabalhos:Habilitados", true))
        {
            s.AddHostedService<TrabalhoDespacho>();
            s.AddHostedService<TrabalhoAnaliseIA>();
            s.AddHostedService<TrabalhoAlertas>();
            if (cfg.GetValue("Trabalhos:FontesExternas", true)) s.AddHostedService<TrabalhoFontesReferencia>();
            if (cfg.GetValue("Trabalhos:IndicadorAlagamento", true)) s.AddHostedService<TrabalhoIndicadorAlagamento>();
        }
        return s;
    }
}

/// <summary>Mantém uma única instância (com cache) da fonte de chuva, criada via IHttpClientFactory.</summary>
public sealed class FonteChuvaApacSingleton(IHttpClientFactory f, IRegistroFontes r, IRelogio rel, Microsoft.Extensions.Logging.ILogger<FonteChuvaApac> l)
{
    public FonteChuvaApac Instancia { get; } = new(CriarCliente(f), r, rel, l);

    private static HttpClient CriarCliente(IHttpClientFactory f)
    {
        var c = f.CreateClient(nameof(FonteChuvaApac));
        return c;
    }
}
