using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlertaEmAcao.Infraestrutura.Persistencia;

/// <summary>Usada apenas pelo `dotnet ef` para gerar migrações.</summary>
public sealed class FabricaDesignTime : IDesignTimeDbContextFactory<AlertaDbContext>
{
    public AlertaDbContext CreateDbContext(string[] args)
    {
        CifradorCampos.Configurar(null);
        return new AlertaDbContext(new DbContextOptionsBuilder<AlertaDbContext>()
            .UseNpgsql("Host=localhost;Database=alerta_design;Username=postgres;Password=postgres", n => n.MigrationsHistoryTable("__historico_migracoes", "alerta"))
            .Options);
    }
}
