using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlertaEmAcao.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class IndicadorAlagamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "estados_indicador_local",
                schema: "alerta",
                columns: table => new
                {
                    LocalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nivel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Episodio = table.Column<Guid>(type: "uuid", nullable: false),
                    AvaliadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_estados_indicador_local", x => x.LocalId);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes_preventivas",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nivel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Chave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Mensagem = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Entregas = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes_preventivas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_preventivas_UsuarioId_Chave",
                schema: "alerta",
                table: "notificacoes_preventivas",
                columns: new[] { "UsuarioId", "Chave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "estados_indicador_local",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "notificacoes_preventivas",
                schema: "alerta");
        }
    }
}
