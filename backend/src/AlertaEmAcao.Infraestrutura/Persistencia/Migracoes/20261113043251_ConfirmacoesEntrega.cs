using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlertaEmAcao.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ConfirmacoesEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "confirmacoes_entrega",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OcorrenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentificadorMensagem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Nivel = table.Column<int>(type: "integer", nullable: false),
                    Origem = table.Column<string>(type: "text", nullable: false),
                    Destino = table.Column<string>(type: "text", nullable: false),
                    Horario = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Canal = table.Column<string>(type: "text", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: false),
                    Comprovante = table.Column<string>(type: "text", nullable: true),
                    RegistradoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmacoes_entrega", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_confirmacoes_entrega_OcorrenciaId",
                schema: "alerta",
                table: "confirmacoes_entrega",
                column: "OcorrenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_confirmacoes_entrega_OcorrenciaId_Nivel_IdentificadorMensag~",
                schema: "alerta",
                table: "confirmacoes_entrega",
                columns: new[] { "OcorrenciaId", "Nivel", "IdentificadorMensagem", "Destino" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "confirmacoes_entrega",
                schema: "alerta");
        }
    }
}
