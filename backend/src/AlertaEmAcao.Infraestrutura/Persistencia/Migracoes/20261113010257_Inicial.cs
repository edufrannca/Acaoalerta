using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AlertaEmAcao.Infraestrutura.Persistencia.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "alerta");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "abrigos",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdExterno = table.Column<string>(type: "text", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: true),
                    Endereco = table.Column<string>(type: "text", nullable: true),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Acessibilidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Fonte = table.Column<string>(type: "text", nullable: false),
                    FonteUrl = table.Column<string>(type: "text", nullable: true),
                    StatusFonte = table.Column<string>(type: "text", nullable: true),
                    EstagioOperacional = table.Column<string>(type: "text", nullable: true),
                    AtualizadoNaFonteEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObtidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisponibilidadeVerificada = table.Column<bool>(type: "boolean", nullable: false),
                    Ficticio = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_abrigos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "analises_ocorrencias",
                schema: "alerta",
                columns: table => new
                {
                    OcorrenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersaoRegras = table.Column<string>(type: "text", nullable: false),
                    Sinalizacoes = table.Column<string>(type: "jsonb", nullable: false),
                    NivelMaximo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Contexto = table.Column<string>(type: "jsonb", nullable: true),
                    Fontes = table.Column<string>(type: "jsonb", nullable: false),
                    StatusIA = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResultadoIA = table.Column<string>(type: "text", nullable: true),
                    ProblemasValidacaoIA = table.Column<string>(type: "jsonb", nullable: false),
                    ModeloIA = table.Column<string>(type: "text", nullable: true),
                    DuracaoIAMs = table.Column<long>(type: "bigint", nullable: true),
                    TentativasIA = table.Column<int>(type: "integer", nullable: false),
                    RegrasAvaliadasEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IAConcluidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analises_ocorrencias", x => x.OcorrenciaId);
                });

            migrationBuilder.CreateTable(
                name: "areas_risco",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Fonte = table.Column<string>(type: "text", nullable: false),
                    FonteUrl = table.Column<string>(type: "text", nullable: true),
                    IdExterno = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Bairro = table.Column<string>(type: "text", nullable: true),
                    Localidade = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: false),
                    Licenca = table.Column<string>(type: "text", nullable: false),
                    ObtidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_areas_risco", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "assinaturas_push",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "text", nullable: false),
                    P256dh = table.Column<string>(type: "text", nullable: false),
                    Auth = table.Column<string>(type: "text", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assinaturas_push", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "auditoria",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsuarioId = table.Column<string>(type: "text", nullable: true),
                    Papel = table.Column<string>(type: "text", nullable: true),
                    Acao = table.Column<string>(type: "text", nullable: false),
                    Recurso = table.Column<string>(type: "text", nullable: false),
                    Detalhe = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auditoria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "avisos",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Origem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CodigoExterno = table.Column<string>(type: "text", nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Severidade = table.Column<string>(type: "text", nullable: false),
                    Cor = table.Column<string>(type: "text", nullable: true),
                    InicioEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FimEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PoligonoGeoJson = table.Column<string>(type: "text", nullable: true),
                    MunicipiosCodigos = table.Column<List<string>>(type: "text[]", nullable: false),
                    Riscos = table.Column<string>(type: "jsonb", nullable: false),
                    Instrucoes = table.Column<string>(type: "jsonb", nullable: false),
                    FonteNome = table.Column<string>(type: "text", nullable: false),
                    FonteUrl = table.Column<string>(type: "text", nullable: true),
                    ObtidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Processado = table.Column<bool>(type: "boolean", nullable: false),
                    ProcessadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_avisos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "chaves_protecao",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chaves_protecao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "encaminhamentos",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoMensagem = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReferenciaId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstituicaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstituicaoCodigo = table.Column<string>(type: "text", nullable: false),
                    Justificativa = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProximaTentativaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Protocolo = table.Column<string>(type: "text", nullable: true),
                    ConfirmadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UltimoErro = table.Column<string>(type: "text", nullable: true),
                    EstadoNaCentral = table.Column<string>(type: "text", nullable: true),
                    HistoricoTentativas = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encaminhamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "grupos_familiares",
                schema: "alerta",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Integrantes = table.Column<string>(type: "text", nullable: false),
                    Contatos = table.Column<string>(type: "text", nullable: false),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grupos_familiares", x => x.UsuarioId);
                });

            migrationBuilder.CreateTable(
                name: "instituicoes",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "text", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: true),
                    Jurisdicao = table.Column<string>(type: "text", nullable: false),
                    Canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EndpointUrl = table.Column<string>(type: "text", nullable: false),
                    Habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    TimeoutSegundos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instituicoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "locais_monitorados",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Apelido = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locais_monitorados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "municipios",
                schema: "alerta",
                columns: table => new
                {
                    CodigoIbge = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Uf = table.Column<string>(type: "text", nullable: false),
                    GeometriaGeoJson = table.Column<string>(type: "text", nullable: false),
                    FonteGeometria = table.Column<string>(type: "text", nullable: false),
                    GeometriaOficialAoVivo = table.Column<bool>(type: "boolean", nullable: false),
                    GeometriaAtualizadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ObservacoesDados = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_municipios", x => x.CodigoIbge);
                });

            migrationBuilder.CreateTable(
                name: "necessidades_publicadas",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizacaoCodigo = table.Column<string>(type: "text", nullable: false),
                    OrganizacaoNome = table.Column<string>(type: "text", nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    PontoEntregaVerificado = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ficticia = table.Column<bool>(type: "boolean", nullable: false),
                    PublicadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_necessidades_publicadas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notificacoes",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvisoId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalMonitoradoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titulo = table.Column<string>(type: "text", nullable: false),
                    Mensagem = table.Column<string>(type: "text", nullable: false),
                    Pergunta = table.Column<string>(type: "text", nullable: false),
                    AvisoOficial = table.Column<bool>(type: "boolean", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Entregas = table.Column<string>(type: "jsonb", nullable: false),
                    LidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resposta = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    RespondidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notificacoes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ocorrencias",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispositivoId = table.Column<string>(type: "text", nullable: false),
                    CriadaNoDispositivoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecebidaNoServidorEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Canal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RetransmitidaPor = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    PrecisaoMetros = table.Column<double>(type: "double precision", nullable: true),
                    LocalizacaoLidaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: true),
                    Complemento = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Verificacao = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AvisoRelacionadoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Historico = table.Column<string>(type: "jsonb", nullable: false),
                    TrilhaLocalizacao = table.Column<string>(type: "text", nullable: false),
                    Demonstracao = table.Column<bool>(type: "boolean", nullable: false),
                    AtualizadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocorrencias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ofertas_ajuda",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispositivoId = table.Column<string>(type: "text", nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: false),
                    RegiaoDisponibilidade = table.Column<string>(type: "text", nullable: true),
                    Tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Disponibilidade = table.Column<string>(type: "text", nullable: true),
                    OrganizacaoReferencia = table.Column<string>(type: "text", nullable: true),
                    ContatoAutorizado = table.Column<string>(type: "text", nullable: true),
                    NecessidadeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProtocoloOrganizacao = table.Column<string>(type: "text", nullable: true),
                    Historico = table.Column<string>(type: "jsonb", nullable: false),
                    CriadaEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ofertas_ajuda", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "regras_encaminhamento",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: true),
                    TipoInstituicao = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Gatilho = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regras_encaminhamento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "situacoes_declaradas",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispositivoId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeclaradaNoDispositivoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecebidaNoServidorEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: true),
                    AvisoId = table.Column<Guid>(type: "uuid", nullable: true),
                    DesejaAjudar = table.Column<bool>(type: "boolean", nullable: false),
                    Mensagem = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_situacoes_declaradas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                schema: "alerta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Telefone = table.Column<string>(type: "text", nullable: true),
                    SenhaHash = table.Column<string>(type: "text", nullable: false),
                    Papel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CentralCodigo = table.Column<string>(type: "text", nullable: true),
                    MunicipioCodigo = table.Column<string>(type: "text", nullable: true),
                    EnderecoReferencia = table.Column<string>(type: "text", nullable: true),
                    ConsenteAlertas = table.Column<bool>(type: "boolean", nullable: false),
                    ConsenteLocalizacao = table.Column<bool>(type: "boolean", nullable: false),
                    ReceberPush = table.Column<bool>(type: "boolean", nullable: false),
                    ContaDemonstracao = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_abrigos_Fonte_IdExterno",
                schema: "alerta",
                table: "abrigos",
                columns: new[] { "Fonte", "IdExterno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_analises_ocorrencias_StatusIA",
                schema: "alerta",
                table: "analises_ocorrencias",
                column: "StatusIA");

            migrationBuilder.CreateIndex(
                name: "IX_areas_risco_Fonte_IdExterno",
                schema: "alerta",
                table: "areas_risco",
                columns: new[] { "Fonte", "IdExterno" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_areas_risco_MunicipioCodigo_Latitude_Longitude",
                schema: "alerta",
                table: "areas_risco",
                columns: new[] { "MunicipioCodigo", "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_assinaturas_push_Endpoint",
                schema: "alerta",
                table: "assinaturas_push",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_auditoria_Em",
                schema: "alerta",
                table: "auditoria",
                column: "Em");

            migrationBuilder.CreateIndex(
                name: "IX_avisos_CodigoExterno",
                schema: "alerta",
                table: "avisos",
                column: "CodigoExterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_avisos_Processado_FimEm",
                schema: "alerta",
                table: "avisos",
                columns: new[] { "Processado", "FimEm" });

            migrationBuilder.CreateIndex(
                name: "IX_encaminhamentos_InstituicaoCodigo_Protocolo",
                schema: "alerta",
                table: "encaminhamentos",
                columns: new[] { "InstituicaoCodigo", "Protocolo" });

            migrationBuilder.CreateIndex(
                name: "IX_encaminhamentos_ReferenciaId",
                schema: "alerta",
                table: "encaminhamentos",
                column: "ReferenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_encaminhamentos_Status_ProximaTentativaEm",
                schema: "alerta",
                table: "encaminhamentos",
                columns: new[] { "Status", "ProximaTentativaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_instituicoes_Codigo",
                schema: "alerta",
                table: "instituicoes",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_locais_monitorados_UsuarioId",
                schema: "alerta",
                table: "locais_monitorados",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_AvisoId_UsuarioId",
                schema: "alerta",
                table: "notificacoes",
                columns: new[] { "AvisoId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notificacoes_UsuarioId",
                schema: "alerta",
                table: "notificacoes",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ocorrencias_DispositivoId",
                schema: "alerta",
                table: "ocorrencias",
                column: "DispositivoId");

            migrationBuilder.CreateIndex(
                name: "IX_ocorrencias_RecebidaNoServidorEm",
                schema: "alerta",
                table: "ocorrencias",
                column: "RecebidaNoServidorEm");

            migrationBuilder.CreateIndex(
                name: "IX_ocorrencias_UsuarioId",
                schema: "alerta",
                table: "ocorrencias",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ofertas_ajuda_DispositivoId",
                schema: "alerta",
                table: "ofertas_ajuda",
                column: "DispositivoId");

            migrationBuilder.CreateIndex(
                name: "IX_situacoes_declaradas_UsuarioId",
                schema: "alerta",
                table: "situacoes_declaradas",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_Email",
                schema: "alerta",
                table: "usuarios",
                column: "Email",
                unique: true);

            // Colunas geográficas PostGIS (geradas a partir dos campos do domínio) e índices espaciais.
            migrationBuilder.Sql("""
                ALTER TABLE alerta.municipios ADD COLUMN geom geometry(Geometry, 4326)
                    GENERATED ALWAYS AS (ST_SetSRID(ST_GeomFromGeoJSON("GeometriaGeoJson"), 4326)) STORED;
                CREATE INDEX ix_municipios_geom ON alerta.municipios USING GIST (geom);
                ALTER TABLE alerta.avisos ADD COLUMN geom geometry(Geometry, 4326)
                    GENERATED ALWAYS AS (CASE WHEN "PoligonoGeoJson" IS NULL THEN NULL ELSE ST_SetSRID(ST_GeomFromGeoJSON("PoligonoGeoJson"), 4326) END) STORED;
                CREATE INDEX ix_avisos_geom ON alerta.avisos USING GIST (geom);
                ALTER TABLE alerta.locais_monitorados ADD COLUMN geom geometry(Point, 4326)
                    GENERATED ALWAYS AS (ST_SetSRID(ST_MakePoint("Longitude", "Latitude"), 4326)) STORED;
                CREATE INDEX ix_locais_geom ON alerta.locais_monitorados USING GIST (geom);
                ALTER TABLE alerta.areas_risco ADD COLUMN geom geometry(Point, 4326)
                    GENERATED ALWAYS AS (ST_SetSRID(ST_MakePoint("Longitude", "Latitude"), 4326)) STORED;
                CREATE INDEX ix_areas_risco_geom ON alerta.areas_risco USING GIST (geom);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abrigos",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "analises_ocorrencias",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "areas_risco",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "assinaturas_push",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "auditoria",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "avisos",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "chaves_protecao",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "encaminhamentos",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "grupos_familiares",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "instituicoes",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "locais_monitorados",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "municipios",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "necessidades_publicadas",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "notificacoes",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "ocorrencias",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "ofertas_ajuda",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "regras_encaminhamento",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "situacoes_declaradas",
                schema: "alerta");

            migrationBuilder.DropTable(
                name: "usuarios",
                schema: "alerta");
        }
    }
}
