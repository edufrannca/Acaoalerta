using System.Security.Cryptography;
using System.Text;
using AlertaEmAcao.Modulos.Abrigos;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AlertaEmAcao.Infraestrutura.Persistencia;

/// <summary>Chaves do ASP.NET Data Protection persistidas no banco (tokens sobrevivem a reinícios).</summary>
public sealed class ChaveProtecao
{
    public int Id { get; set; }
    public string? FriendlyName { get; set; }
    public string? Xml { get; set; }
}

public sealed class RegistroAuditoria
{
    public long Id { get; set; }
    public DateTimeOffset Em { get; set; }
    public string? UsuarioId { get; set; }
    public string? Papel { get; set; }
    public string Acao { get; set; } = default!;
    public string Recurso { get; set; } = default!;
    public string? Detalhe { get; set; }
}

public sealed class AlertaDbContext(DbContextOptions<AlertaDbContext> opcoes) : DbContext(opcoes)
{
    public DbSet<ChaveProtecao> ChavesProtecao => Set<ChaveProtecao>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // PostgreSQL (timestamptz) exige UTC; o domínio pode trabalhar com qualquer offset.
        c.Properties<DateTimeOffset>().HaveConversion<ConversorUtc>();
        c.Properties<DateTimeOffset?>().HaveConversion<ConversorUtcNulo>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("alerta");
        b.HasPostgresExtension("postgis");

        b.Entity<ChaveProtecao>().ToTable("chaves_protecao");
        b.Entity<RegistroAuditoria>(e => { e.ToTable("auditoria"); e.HasIndex(x => x.Em); });

        b.Entity<Municipio>(e =>
        {
            e.ToTable("municipios"); e.HasKey(x => x.CodigoIbge);
            e.Property(x => x.CodigoIbge).HasMaxLength(7);
            e.Property(x => x.GeometriaGeoJson).HasColumnType("text");
        });

        b.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Papel).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
        });

        b.Entity<GrupoFamiliar>(e =>
        {
            e.ToTable("grupos_familiares"); e.HasKey(x => x.UsuarioId);
            // Dados de saúde, idades e contatos: cifrados em repouso (AES-GCM).
            e.Property(x => x.Integrantes).HasConversion(Cifrado<List<IntegranteFamilia>>(), Comparador<List<IntegranteFamilia>>()).HasColumnType("text");
            e.Property(x => x.Contatos).HasConversion(Cifrado<List<ContatoReferencia>>(), Comparador<List<ContatoReferencia>>()).HasColumnType("text");
        });

        b.Entity<LocalMonitorado>(e =>
        {
            e.ToTable("locais_monitorados"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.UsuarioId);
        });

        b.Entity<Aviso>(e =>
        {
            e.ToTable("avisos"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.CodigoExterno).IsUnique();
            e.HasIndex(x => new { x.Processado, x.FimEm });
            e.Property(x => x.Origem).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PoligonoGeoJson).HasColumnType("text");
            e.Property(x => x.Riscos).HasConversion(JsonConv<List<string>>(), Comparador<List<string>>()).HasColumnType("jsonb");
            e.Property(x => x.Instrucoes).HasConversion(JsonConv<List<string>>(), Comparador<List<string>>()).HasColumnType("jsonb");
        });

        b.Entity<Ocorrencia>(e =>
        {
            e.ToTable("ocorrencias"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.RecebidaNoServidorEm);
            e.HasIndex(x => x.DispositivoId);
            e.HasIndex(x => x.UsuarioId);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Canal).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Verificacao).HasConversion<string>().HasMaxLength(30);
            // Relato e dados de saúde das pessoas envolvidas: cifrados em repouso.
            e.Property(x => x.Complemento).HasConversion(Cifrado<ComplementoOcorrencia>(), Comparador<ComplementoOcorrencia>()).HasColumnType("text");
            e.Property(x => x.Historico).HasConversion(JsonConv<List<EventoHistorico>>(), Comparador<List<EventoHistorico>>()).HasColumnType("jsonb");
            e.Property(x => x.TrilhaLocalizacao).HasConversion(Cifrado<List<PosicaoDispositivo>>(), Comparador<List<PosicaoDispositivo>>()).HasColumnType("text");
            e.Ignore(x => x.Ponto);
            e.Property(x => x.Versao).IsRowVersion();
        });

        b.Entity<SituacaoDeclarada>(e =>
        {
            e.ToTable("situacoes_declaradas"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.UsuarioId);
        });

        b.Entity<AnaliseOcorrencia>(e =>
        {
            e.ToTable("analises_ocorrencias"); e.HasKey(x => x.OcorrenciaId);
            e.Property(x => x.StatusIA).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.NivelMaximo).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Sinalizacoes).HasConversion(JsonConv<List<Sinalizacao>>(), Comparador<List<Sinalizacao>>()).HasColumnType("jsonb");
            e.Property(x => x.Contexto).HasConversion(JsonConvNulo<ContextoTerritorialResumo>(), Comparador<ContextoTerritorialResumo?>()).HasColumnType("jsonb");
            e.Property(x => x.Fontes).HasConversion(JsonConv<List<ReferenciaFonte>>(), Comparador<List<ReferenciaFonte>>()).HasColumnType("jsonb");
            e.Property(x => x.ResultadoIA).HasConversion(CifradoNulo<ResultadoAnaliseIA>(), Comparador<ResultadoAnaliseIA?>()).HasColumnType("text");
            e.Property(x => x.ProblemasValidacaoIA).HasConversion(JsonConv<List<string>>(), Comparador<List<string>>()).HasColumnType("jsonb");
            e.HasIndex(x => x.StatusIA);
            e.Ignore(x => x.PrecisaIA);
        });

        b.Entity<Instituicao>(e =>
        {
            e.ToTable("instituicoes"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Canal).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(40);
        });

        b.Entity<RegraEncaminhamento>(e =>
        {
            e.ToTable("regras_encaminhamento"); e.HasKey(x => x.Id);
            e.Property(x => x.TipoInstituicao).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Gatilho).HasConversion<string>().HasMaxLength(40);
        });

        b.Entity<EncaminhamentoInstitucional>(e =>
        {
            e.ToTable("encaminhamentos"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Status, x.ProximaTentativaEm });
            e.HasIndex(x => x.ReferenciaId);
            e.HasIndex(x => new { x.InstituicaoCodigo, x.Protocolo });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.TipoMensagem).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.HistoricoTentativas).HasConversion(JsonConv<List<TentativaEnvio>>(), Comparador<List<TentativaEnvio>>()).HasColumnType("jsonb");
            e.Property(x => x.Versao).IsRowVersion();
        });

        b.Entity<AreaRiscoDocumentada>(e =>
        {
            e.ToTable("areas_risco"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Fonte, x.IdExterno }).IsUnique();
            e.HasIndex(x => new { x.MunicipioCodigo, x.Latitude, x.Longitude });
            e.Ignore(x => x.Ponto);
        });

        b.Entity<Abrigo>(e =>
        {
            e.ToTable("abrigos"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Fonte, x.IdExterno }).IsUnique();
            e.Property(x => x.Acessibilidade).HasConversion<string>().HasMaxLength(20);
            e.Ignore(x => x.DescricaoDisponibilidade);
        });

        b.Entity<NecessidadePublicada>(e =>
        {
            e.ToTable("necessidades_publicadas"); e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<OfertaAjuda>(e =>
        {
            e.ToTable("ofertas_ajuda"); e.HasKey(x => x.Id);
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.ContatoAutorizado).HasConversion(CifradoTexto()).HasColumnType("text");
            e.Property(x => x.Historico).HasConversion(JsonConv<List<EventoOferta>>(), Comparador<List<EventoOferta>>()).HasColumnType("jsonb");
            e.HasIndex(x => x.DispositivoId);
        });

        b.Entity<Notificacao>(e =>
        {
            e.ToTable("notificacoes"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.AvisoId, x.UsuarioId }).IsUnique();
            e.HasIndex(x => x.UsuarioId);
            e.Property(x => x.Resposta).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Entregas).HasConversion(JsonConv<List<EntregaCanal>>(), Comparador<List<EntregaCanal>>()).HasColumnType("jsonb");
        });

        b.Entity<AssinaturaPush>(e =>
        {
            e.ToTable("assinaturas_push"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.Endpoint).IsUnique();
        });
    }

    private static ValueConverter<T, string> JsonConv<T>() where T : class, new() =>
        new(v => Json.Serializar(v), s => Json.Desserializar<T>(s) ?? new T());

    private static ValueConverter<T?, string?> JsonConvNulo<T>() where T : class =>
        new(v => v == null ? null : Json.Serializar(v), s => s == null ? null : Json.Desserializar<T>(s));

    private static ValueConverter<T, string> Cifrado<T>() where T : class, new() =>
        new(v => CifradorCampos.Cifrar(Json.Serializar(v)), s => Json.Desserializar<T>(CifradorCampos.Decifrar(s)) ?? new T());

    private static ValueConverter<T?, string?> CifradoNulo<T>() where T : class =>
        new(v => v == null ? null : CifradorCampos.Cifrar(Json.Serializar(v)), s => s == null ? null : Json.Desserializar<T>(CifradorCampos.Decifrar(s)));

    private static ValueConverter<string?, string?> CifradoTexto() =>
        new(v => v == null ? null : CifradorCampos.Cifrar(v), s => s == null ? null : CifradorCampos.Decifrar(s));

    private static ValueComparer<T> Comparador<T>() =>
        new((a, b) => Json.Serializar(a) == Json.Serializar(b), v => v == null ? 0 : Json.Serializar(v).GetHashCode(),
            v => Json.Desserializar<T>(Json.Serializar(v))!);
}

public sealed class ConversorUtc() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);
public sealed class ConversorUtcNulo() : ValueConverter<DateTimeOffset?, DateTimeOffset?>(v => v == null ? null : v.Value.ToUniversalTime(), v => v);

/// <summary>
/// Cifra campos sensíveis com AES-256-GCM. Chave em DADOS__CHAVECIFRAGEM (base64, 32 bytes).
/// Formato: "v1:" + base64(nonce[12] | tag[16] | ciphertext).
/// </summary>
public static class CifradorCampos
{
    private static byte[]? _chave;
    public static bool ChaveDeDesenvolvimento { get; private set; }

    public static void Configurar(string? chaveBase64)
    {
        if (string.IsNullOrWhiteSpace(chaveBase64))
        {
            _chave = SHA256.HashData(Encoding.UTF8.GetBytes("alerta-em-acao-chave-SOMENTE-desenvolvimento"));
            ChaveDeDesenvolvimento = true;
            return;
        }
        var k = Convert.FromBase64String(chaveBase64);
        if (k.Length != 32) throw new InvalidOperationException("DADOS__CHAVECIFRAGEM deve ter 32 bytes em base64.");
        _chave = k;
        ChaveDeDesenvolvimento = false;
    }

    private static byte[] Chave => _chave ?? throw new InvalidOperationException("CifradorCampos não configurado.");

    public static string Cifrar(string texto)
    {
        var dados = Encoding.UTF8.GetBytes(texto);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cifrado = new byte[dados.Length];
        using var aes = new AesGcm(Chave, 16);
        aes.Encrypt(nonce, dados, cifrado, tag);
        return "v1:" + Convert.ToBase64String([.. nonce, .. tag, .. cifrado]);
    }

    public static string Decifrar(string valor)
    {
        if (!valor.StartsWith("v1:")) return valor; // compatibilidade com dados não cifrados
        var bruto = Convert.FromBase64String(valor[3..]);
        var nonce = bruto.AsSpan(0, 12);
        var tag = bruto.AsSpan(12, 16);
        var cifrado = bruto.AsSpan(28);
        var texto = new byte[cifrado.Length];
        using var aes = new AesGcm(Chave, 16);
        aes.Decrypt(nonce, cifrado, tag, texto);
        return Encoding.UTF8.GetString(texto);
    }
}
