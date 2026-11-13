using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlertaEmAcao.CentraisSimuladas;

public sealed class FabricaDesignTime : IDesignTimeDbContextFactory<SimuladorDbContext>
{
    public SimuladorDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SimuladorDbContext>()
            .UseNpgsql("Host=localhost;Database=alerta_design;Username=postgres;Password=postgres", n => n.MigrationsHistoryTable("__historico_migracoes", "simulador"))
            .Options);
}
