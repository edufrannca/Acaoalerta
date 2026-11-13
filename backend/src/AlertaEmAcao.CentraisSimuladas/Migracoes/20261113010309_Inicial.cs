using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlertaEmAcao.CentraisSimuladas.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "simulador");

            migrationBuilder.CreateTable(
                name: "centrais",
                schema: "simulador",
                columns: table => new
                {
                    Codigo = table.Column<string>(type: "text", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: true),
                    Modo = table.Column<string>(type: "text", nullable: false),
                    AtrasoMs = table.Column<int>(type: "integer", nullable: false),
                    Sequencial = table.Column<long>(type: "bigint", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModoAlteradoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_centrais", x => x.Codigo);
                });

            migrationBuilder.CreateTable(
                name: "recebimentos",
                schema: "simulador",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CentralCodigo = table.Column<string>(type: "text", nullable: false),
                    ChaveIdempotencia = table.Column<string>(type: "text", nullable: false),
                    Protocolo = table.Column<string>(type: "text", nullable: false),
                    TipoMensagem = table.Column<string>(type: "text", nullable: false),
                    IdentificadorReferencia = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentificadorCorrelacao = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    RecebidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Duplicatas = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    HistoricoJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recebimentos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recebimentos_CentralCodigo_ChaveIdempotencia",
                schema: "simulador",
                table: "recebimentos",
                columns: new[] { "CentralCodigo", "ChaveIdempotencia" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recebimentos_Protocolo",
                schema: "simulador",
                table: "recebimentos",
                column: "Protocolo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "centrais",
                schema: "simulador");

            migrationBuilder.DropTable(
                name: "recebimentos",
                schema: "simulador");
        }
    }
}
