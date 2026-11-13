using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlertaEmAcao.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class ResidenciasVinculosConsentimentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResidenciaId",
                schema: "alerta",
                table: "ocorrencias",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AtualizadoEm",
                schema: "alerta",
                table: "locais_monitorados",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Locais antigos: atualização = criação (sem inventar datas).
            migrationBuilder.Sql("UPDATE alerta.locais_monitorados SET \"AtualizadoEm\" = \"CriadoEm\"");

            migrationBuilder.AddColumn<string>(
                name: "DescricaoPrecisao",
                schema: "alerta",
                table: "locais_monitorados",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Endereco",
                schema: "alerta",
                table: "locais_monitorados",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "Moradores",
                schema: "alerta",
                table: "locais_monitorados",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<bool>(
                name: "MoroAqui",
                schema: "alerta",
                table: "locais_monitorados",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Origem",
                schema: "alerta",
                table: "locais_monitorados",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "GpsAtual");

            migrationBuilder.AddColumn<string>(
                name: "Tipo",
                schema: "alerta",
                table: "locais_monitorados",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Outro");

            migrationBuilder.CreateTable(
                name: "convites_vinculo",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    MoradorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodigoHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UsadoPor = table.Column<Guid>(type: "uuid", nullable: true),
                    Cancelado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_convites_vinculo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes_familiares",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamiliarId = table.Column<Guid>(type: "uuid", nullable: false),
                    VinculoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OcorrenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Chave = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Mensagem = table.Column<string>(type: "text", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Entregas = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes_familiares", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "registros_consentimento",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Finalidade = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Concedido = table.Column<bool>(type: "boolean", nullable: false),
                    VersaoTexto = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registros_consentimento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vinculos_familiares",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResidenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    MoradorId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamiliarId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApelidoParaFamiliar = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Permissoes = table.Column<string>(type: "jsonb", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecididoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevogadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevogadoPor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vinculos_familiares", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_convites_vinculo_CodigoHash",
                schema: "alerta",
                table: "convites_vinculo",
                column: "CodigoHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_convites_vinculo_ResidenciaId",
                schema: "alerta",
                table: "convites_vinculo",
                column: "ResidenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_familiares_FamiliarId_Chave",
                schema: "alerta",
                table: "notificacoes_familiares",
                columns: new[] { "FamiliarId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_familiares_OcorrenciaId",
                schema: "alerta",
                table: "notificacoes_familiares",
                column: "OcorrenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_registros_consentimento_UsuarioId_Finalidade_Em",
                schema: "alerta",
                table: "registros_consentimento",
                columns: new[] { "UsuarioId", "Finalidade", "Em" });

            migrationBuilder.CreateIndex(
                name: "IX_vinculos_familiares_FamiliarId",
                schema: "alerta",
                table: "vinculos_familiares",
                column: "FamiliarId");

            migrationBuilder.CreateIndex(
                name: "IX_vinculos_familiares_MoradorId",
                schema: "alerta",
                table: "vinculos_familiares",
                column: "MoradorId");

            migrationBuilder.CreateIndex(
                name: "IX_vinculos_familiares_ResidenciaId",
                schema: "alerta",
                table: "vinculos_familiares",
                column: "ResidenciaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "convites_vinculo",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "notificacoes_familiares",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "registros_consentimento",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "vinculos_familiares",
                schema: "alerta");

            migrationBuilder.DropColumn(
                name: "ResidenciaId",
                schema: "alerta",
                table: "ocorrencias");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                schema: "alerta",
                table: "locais_monitorados");

            migrationBuilder.DropColumn(
                name: "DescricaoPrecisao",
                schema: "alerta",
                table: "locais_monitorados");

            migrationBuilder.DropColumn(
                name: "Endereco",
                schema: "alerta",
                table: "locais_monitorados");

            migrationBuilder.DropColumn(
                name: "Moradores",
                schema: "alerta",
                table: "locais_monitorados");

            migrationBuilder.DropColumn(
                name: "MoroAqui",
                schema: "alerta",
                table: "locais_monitorados");

            migrationBuilder.DropColumn(
                name: "Origem",
                schema: "alerta",
                table: "locais_monitorados");

            migrationBuilder.DropColumn(
                name: "Tipo",
                schema: "alerta",
                table: "locais_monitorados");
        }
    }
}
