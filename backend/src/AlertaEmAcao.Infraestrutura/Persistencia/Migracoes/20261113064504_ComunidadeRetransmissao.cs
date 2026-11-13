using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlertaEmAcao.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ComunidadeRetransmissao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstrucaoEquipe",
                schema: "alerta",
                table: "ofertas_ajuda",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OcorrenciaId",
                schema: "alerta",
                table: "ofertas_ajuda",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Compartilhamento",
                schema: "alerta",
                table: "ocorrencias",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "PosicoesRetransmissor",
                schema: "alerta",
                table: "ocorrencias",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_ofertas_ajuda_OcorrenciaId",
                schema: "alerta",
                table: "ofertas_ajuda",
                column: "OcorrenciaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ofertas_ajuda_OcorrenciaId",
                schema: "alerta",
                table: "ofertas_ajuda");

            migrationBuilder.DropColumn(
                name: "InstrucaoEquipe",
                schema: "alerta",
                table: "ofertas_ajuda");

            migrationBuilder.DropColumn(
                name: "OcorrenciaId",
                schema: "alerta",
                table: "ofertas_ajuda");

            migrationBuilder.DropColumn(
                name: "Compartilhamento",
                schema: "alerta",
                table: "ocorrencias");

            migrationBuilder.DropColumn(
                name: "PosicoesRetransmissor",
                schema: "alerta",
                table: "ocorrencias");
        }
    }
}
