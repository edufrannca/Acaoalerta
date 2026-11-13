using System.Reflection;

namespace AlertaEmAcao.Testes.Arquitetura;

/// <summary>Garante as regras de dependência da Clean Architecture/Hexagonal entre os projetos.</summary>
public class DependenciasTestes
{
    private static readonly Assembly Contratos = typeof(AlertaEmAcao.Contratos.MensagemCentralV1).Assembly;
    private static readonly Assembly Modulos = typeof(AlertaEmAcao.Modulos.Comum.IUnidadeTrabalho).Assembly;
    private static readonly Assembly Infra = typeof(AlertaEmAcao.Infraestrutura.InjecaoDependencias).Assembly;
    private static readonly Assembly Centrais = typeof(AlertaEmAcao.CentraisSimuladas.CentralSimulada).Assembly;
    private static readonly Assembly Api = typeof(AlertaEmAcao.Api.Configuracao).Assembly;

    private static HashSet<string> Referencias(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!).ToHashSet();

    [Fact]
    public void Contratos_nao_dependem_de_nenhum_projeto_interno_nem_de_frameworks()
    {
        var r = Referencias(Contratos);
        Assert.DoesNotContain(r, n => n.StartsWith("AlertaEmAcao."));
        Assert.DoesNotContain(r, n => n.StartsWith("Microsoft.EntityFrameworkCore") || n.StartsWith("Microsoft.AspNetCore"));
    }

    [Fact]
    public void Modulos_de_dominio_nao_dependem_de_infraestrutura_ef_ou_aspnet()
    {
        var r = Referencias(Modulos);
        Assert.DoesNotContain("AlertaEmAcao.Infraestrutura", r);
        Assert.DoesNotContain("AlertaEmAcao.Api", r);
        Assert.DoesNotContain("AlertaEmAcao.CentraisSimuladas", r);
        Assert.DoesNotContain(r, n => n.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(r, n => n.StartsWith("Microsoft.AspNetCore"));
        Assert.DoesNotContain(r, n => n.StartsWith("Npgsql"));
    }

    [Fact]
    public void Centrais_simuladas_sao_isoladas_do_dominio_e_da_infraestrutura()
    {
        var r = Referencias(Centrais);
        Assert.Contains("AlertaEmAcao.Contratos", r);
        Assert.DoesNotContain("AlertaEmAcao.Modulos", r);
        Assert.DoesNotContain("AlertaEmAcao.Infraestrutura", r);
    }

    [Fact]
    public void Infraestrutura_nao_depende_da_api_nem_das_centrais_simuladas()
    {
        var r = Referencias(Infra);
        Assert.DoesNotContain("AlertaEmAcao.Api", r);
        Assert.DoesNotContain("AlertaEmAcao.CentraisSimuladas", r);
    }

    [Fact]
    public void Portas_do_dominio_sao_interfaces_implementadas_na_infraestrutura()
    {
        var portas = Modulos.GetTypes().Where(t => t.IsInterface && t.IsPublic && t.Name.StartsWith('I')).ToList();
        Assert.NotEmpty(portas);
        var implementacoes = Infra.GetTypes().Concat(Api.GetTypes()).Concat(Modulos.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false }).ToList();
        var semImplementacao = portas.Where(p => !implementacoes.Any(p.IsAssignableFrom)).Select(p => p.Name).ToList();
        Assert.True(semImplementacao.Count == 0, "Portas sem implementação: " + string.Join(", ", semImplementacao));
    }

    [Fact]
    public void Entidades_de_dominio_nao_expoem_setters_publicos()
    {
        var entidades = new[]
        {
            typeof(AlertaEmAcao.Modulos.Ocorrencias.Ocorrencia), typeof(AlertaEmAcao.Modulos.Encaminhamento.EncaminhamentoInstitucional),
            typeof(AlertaEmAcao.Modulos.Alertas.Aviso), typeof(AlertaEmAcao.Modulos.Identidade.Usuario), typeof(AlertaEmAcao.Modulos.ApoioComunitario.OfertaAjuda),
        };
        foreach (var e in entidades)
        {
            var publicos = e.GetProperties().Where(p => p.SetMethod?.IsPublic == true).Select(p => p.Name).ToList();
            Assert.True(publicos.Count == 0, $"{e.Name} expõe setters públicos: {string.Join(", ", publicos)}");
        }
    }

    [Fact]
    public void Cada_modulo_esta_em_seu_proprio_namespace()
    {
        var esperados = new[] { "Identidade", "Municipios", "Familias", "Localizacao", "Monitoramento", "Alertas", "Notificacoes", "Ocorrencias",
            "InteligenciaTerritorial", "AnaliseSituacional", "Comunicacao", "Encaminhamento", "Abrigos", "ApoioComunitario" };
        var ns = Modulos.GetTypes().Select(t => t.Namespace).Where(n => n is not null).Distinct().ToHashSet();
        Assert.All(esperados, m => Assert.Contains($"AlertaEmAcao.Modulos.{m}", ns));
    }
}
