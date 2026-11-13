using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlertaEmAcao.Contratos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlertaEmAcao.CentraisSimuladas;

/// <summary>
/// CENTRAIS INSTITUCIONAIS SIMULADAS — AMBIENTE DE DEMONSTRAÇÃO.
/// Receptores HTTP reais (validam autenticação e esquema, persistem, emitem protocolo de teste)
/// que representam Defesa Civil, Bombeiros, SAMU, PM e organizações de apoio. Não têm qualquer
/// conexão com os órgãos reais.
/// </summary>
public enum ModoCentral
{
    Normal,
    /// <summary>Responde 503 (central fora do ar).</summary>
    Indisponivel,
    /// <summary>Espera AtrasoMs e responde normalmente.</summary>
    Atraso,
    /// <summary>Responde 422 (rejeição de negócio).</summary>
    Rejeitar,
    /// <summary>Responde 401 independentemente da chave.</summary>
    ErroAutenticacao,
    /// <summary>Falha 503 em ~50% das requisições.</summary>
    Intermitente,
    /// <summary>Não responde a tempo (atraso de 30 s), provocando timeout no remetente.</summary>
    Timeout,
}

public sealed class CentralSimulada
{
    public string Codigo { get; set; } = default!;
    public string Nome { get; set; } = default!;
    public string Tipo { get; set; } = default!;
    public string? MunicipioCodigo { get; set; }
    public ModoCentral Modo { get; set; }
    public int AtrasoMs { get; set; }
    public long Sequencial { get; set; }
    public DateTimeOffset CriadaEm { get; set; }
    public DateTimeOffset? ModoAlteradoEm { get; set; }
    public uint Versao { get; set; }
}

public sealed record EventoRecebimento(DateTimeOffset Em, string Descricao, string? Ator);

public sealed class RecebimentoCentral
{
    public Guid Id { get; set; }
    public string CentralCodigo { get; set; } = default!;
    public string ChaveIdempotencia { get; set; } = default!;
    public string Protocolo { get; set; } = default!;
    public string TipoMensagem { get; set; } = default!;
    public Guid IdentificadorReferencia { get; set; }
    public Guid IdentificadorCorrelacao { get; set; }
    public string MunicipioCodigo { get; set; } = default!;
    public string PayloadJson { get; set; } = default!;
    public DateTimeOffset RecebidoEm { get; set; }
    public int Duplicatas { get; set; }
    public string Estado { get; set; } = "Recebido";
    public string HistoricoJson { get; set; } = "[]";
}

public sealed class SimuladorDbContext(DbContextOptions<SimuladorDbContext> o) : DbContext(o)
{
    public DbSet<CentralSimulada> Centrais => Set<CentralSimulada>();
    public DbSet<RecebimentoCentral> Recebimentos => Set<RecebimentoCentral>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("simulador");
        b.Entity<CentralSimulada>(e =>
        {
            e.ToTable("centrais"); e.HasKey(x => x.Codigo);
            e.Property(x => x.Modo).HasConversion<string>();
            e.Property(x => x.Versao).IsRowVersion();
        });
        b.Entity<RecebimentoCentral>(e =>
        {
            e.ToTable("recebimentos"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CentralCodigo, x.ChaveIdempotencia }).IsUnique();
            e.HasIndex(x => x.Protocolo).IsUnique();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.HistoricoJson).HasColumnType("jsonb");
        });
    }
}

public sealed class OpcoesCentraisSimuladas
{
    /// <summary>Segredo para derivar a chave de cada central (HMAC-SHA256). Obrigatório fora de desenvolvimento.</summary>
    public string SegredoMestre { get; set; } = "";
    /// <summary>URL base do Alerta em Ação para retorno de estado (callback).</summary>
    public string? UrlRetorno { get; set; }
    public string? ChaveRetorno { get; set; }
}

public static class ValidadorMensagens
{
    public static List<string> ValidarOcorrencia(MensagemCentralV1 m, CentralSimulada c)
    {
        var e = new List<string>();
        if (m.Versao != VersoesContrato.MensagemCentral) e.Add($"Versão de contrato não suportada: {m.Versao}.");
        if (m.Ambiente != VersoesContrato.AmbienteDemonstracao) e.Add("Central simulada aceita apenas mensagens do ambiente DEMONSTRACAO.");
        if (m.IdentificadorOcorrencia == Guid.Empty) e.Add("identificadorOcorrencia obrigatório.");
        if (m.IdentificadorCorrelacao == Guid.Empty) e.Add("identificadorCorrelacao obrigatório.");
        if (m.MunicipioCodigo is not { Length: 7 } || !m.MunicipioCodigo.All(char.IsDigit)) e.Add("municipioCodigo deve ter 7 dígitos (IBGE).");
        if (m.Latitude is < -90 or > 90 || m.Longitude is < -180 or > 180) e.Add("Coordenadas fora do intervalo.");
        if ((m.Latitude is null) != (m.Longitude is null)) e.Add("Latitude e longitude devem vir juntas.");
        if (m.TiposRisco is null || m.TiposRisco.Count == 0) e.Add("tiposRisco obrigatório.");
        if (m.Relato?.Length > 4000) e.Add("Relato excede 4000 caracteres.");
        if (c.MunicipioCodigo is not null && m.MunicipioCodigo != c.MunicipioCodigo) e.Add($"Ocorrência fora da jurisdição desta central ({c.MunicipioCodigo}).");
        return e;
    }

    public static List<string> ValidarOferta(MensagemOfertaApoioV1 m)
    {
        var e = new List<string>();
        if (m.Ambiente != VersoesContrato.AmbienteDemonstracao) e.Add("Apenas ambiente DEMONSTRACAO.");
        if (m.IdentificadorOferta == Guid.Empty || m.IdentificadorNecessidade == Guid.Empty) e.Add("Identificadores obrigatórios.");
        if (string.IsNullOrWhiteSpace(m.Descricao)) e.Add("Descrição obrigatória.");
        return e;
    }
}

public static class ExtensoesCentraisSimuladas
{
    public const string Aviso = "CENTRAL SIMULADA — AMBIENTE DE DEMONSTRAÇÃO. Nenhum órgão real foi acionado.";
    private static readonly JsonSerializerOptions Js = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static IServiceCollection AddCentraisSimuladas(this IServiceCollection s, string connectionString, Action<OpcoesCentraisSimuladas> configurar)
    {
        s.Configure(configurar);
        s.AddDbContext<SimuladorDbContext>(o => o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__historico_migracoes", "simulador")));
        s.AddHttpClient("retorno-centrais", c => c.Timeout = TimeSpan.FromSeconds(10));
        return s;
    }

    public static RouteGroupBuilder MapCentraisSimuladas(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/simulador").WithTags("Centrais simuladas (demonstração)");

        g.MapPost("/centrais/{codigo}/mensagens", async (string codigo, HttpRequest req, SimuladorDbContext db, IOptions<OpcoesCentraisSimuladas> op,
            ILoggerFactory lf, CancellationToken ct) =>
        {
            var log = lf.CreateLogger("CentraisSimuladas");
            var central = await db.Centrais.AsNoTracking().FirstOrDefaultAsync(c => c.Codigo == codigo, ct);
            if (central is null) return Results.NotFound(new ErroCentralV1("central_inexistente", "Central não cadastrada."));

            // Simulação controlada de falhas (antes de qualquer processamento).
            switch (central.Modo)
            {
                case ModoCentral.Indisponivel:
                    return Results.Json(new ErroCentralV1("indisponivel", "Central simulada indisponível (modo de falha ativado)."), statusCode: 503);
                case ModoCentral.Intermitente when Random.Shared.Next(2) == 0:
                    return Results.Json(new ErroCentralV1("falha_temporaria", "Falha temporária simulada."), statusCode: 503);
                case ModoCentral.Timeout:
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                    break;
                case ModoCentral.Atraso:
                    await Task.Delay(Math.Clamp(central.AtrasoMs, 0, 20_000), ct);
                    break;
            }

            var chaveEsperada = ChavesCentrais.Derivar(op.Value.SegredoMestre, codigo);
            if (central.Modo == ModoCentral.ErroAutenticacao || !ChavesCentrais.Comparar(req.Headers[CabecalhosIntegracao.ChaveCentral].FirstOrDefault(), chaveEsperada))
                return Results.Json(new ErroCentralV1("nao_autenticado", "Chave da central ausente ou inválida."), statusCode: 401);

            var idem = req.Headers[CabecalhosIntegracao.ChaveIdempotencia].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idem) || idem.Length > 100)
                return Results.BadRequest(new ErroCentralV1("idempotencia_ausente", $"Cabeçalho {CabecalhosIntegracao.ChaveIdempotencia} obrigatório."));

            // Reenvio: mesma chave → mesmo protocolo, sem duplicar registro.
            var anterior = await db.Recebimentos.FirstOrDefaultAsync(r => r.CentralCodigo == codigo && r.ChaveIdempotencia == idem, ct);
            if (anterior is not null)
            {
                anterior.Duplicatas++;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new RespostaRecebimentoV1(anterior.Protocolo, central.Codigo, central.Nome, anterior.RecebidoEm, true, VersoesContrato.AmbienteDemonstracao, Aviso));
            }

            string corpo;
            using (var leitor = new StreamReader(req.Body, Encoding.UTF8)) corpo = await leitor.ReadToEndAsync(ct);
            if (corpo.Length > 64_000) return Results.Json(new ErroCentralV1("payload_grande", "Mensagem excede 64 KB."), statusCode: 413);

            string tipo; Guid referencia, correlacao; string municipio; List<string> erros;
            try
            {
                using var doc = JsonDocument.Parse(corpo);
                tipo = doc.RootElement.TryGetProperty("tipoMensagem", out var t) ? t.GetString() ?? "" : "";
                if (tipo == "oferta_apoio")
                {
                    var m = JsonSerializer.Deserialize<MensagemOfertaApoioV1>(corpo, Js)!;
                    erros = ValidadorMensagens.ValidarOferta(m);
                    referencia = m.IdentificadorOferta; correlacao = m.IdentificadorCorrelacao; municipio = m.MunicipioCodigo;
                }
                else if (tipo is "ocorrencia" or "atualizacao_ocorrencia")
                {
                    var m = JsonSerializer.Deserialize<MensagemCentralV1>(corpo, Js)!;
                    erros = ValidadorMensagens.ValidarOcorrencia(m, central);
                    referencia = m.IdentificadorOcorrencia; correlacao = m.IdentificadorCorrelacao; municipio = m.MunicipioCodigo;
                }
                else return Results.UnprocessableEntity(new ErroCentralV1("tipo_desconhecido", $"tipoMensagem não suportado: {tipo}"));
            }
            catch (JsonException ex)
            {
                return Results.BadRequest(new ErroCentralV1("esquema_invalido", "JSON fora do esquema do contrato v1.", [ex.Message]));
            }
            if (erros.Count > 0) return Results.UnprocessableEntity(new ErroCentralV1("validacao", "Mensagem rejeitada pela validação.", erros));
            if (central.Modo == ModoCentral.Rejeitar)
                return Results.UnprocessableEntity(new ErroCentralV1("rejeitada", "Central simulada configurada para rejeitar mensagens."));

            // Persistência efetiva ANTES da confirmação. Protocolo gerado por contador transacional da central.
            var agora = DateTimeOffset.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            var seq = await db.Database.SqlQuery<long>($"UPDATE simulador.centrais SET \"Sequencial\" = \"Sequencial\" + 1 WHERE \"Codigo\" = {codigo} RETURNING \"Sequencial\" AS \"Value\"").ToListAsync(ct);
            var protocolo = $"SIM-{codigo}-{agora.ToOffset(TimeSpan.FromHours(-3)):yyyyMMdd}-{seq[0]:D6}";
            var rec = new RecebimentoCentral
            {
                Id = Guid.CreateVersion7(), CentralCodigo = codigo, ChaveIdempotencia = idem, Protocolo = protocolo, TipoMensagem = tipo,
                IdentificadorReferencia = referencia, IdentificadorCorrelacao = correlacao, MunicipioCodigo = municipio,
                PayloadJson = corpo, RecebidoEm = agora,
                HistoricoJson = JsonSerializer.Serialize(new[] { new EventoRecebimento(agora, "Mensagem recebida, validada e armazenada.", null) }, Js),
            };
            db.Recebimentos.Add(rec);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                var existente = await db.Recebimentos.AsNoTracking().FirstOrDefaultAsync(r => r.CentralCodigo == codigo && r.ChaveIdempotencia == idem, ct);
                if (existente is null) throw;
                return Results.Ok(new RespostaRecebimentoV1(existente.Protocolo, central.Codigo, central.Nome, existente.RecebidoEm, true, VersoesContrato.AmbienteDemonstracao, Aviso));
            }
            log.LogInformation("[SIMULADA] {Central} recebeu {Tipo} {Ref} → {Protocolo}", codigo, tipo, referencia, protocolo);
            return Results.Created($"/simulador/centrais/{codigo}/recebimentos/{protocolo}",
                new RespostaRecebimentoV1(protocolo, central.Codigo, central.Nome, agora, false, VersoesContrato.AmbienteDemonstracao, Aviso));
        }).WithName("ReceberMensagemCentralSimulada").AllowAnonymous();

        g.MapGet("/centrais", async (SimuladorDbContext db, CancellationToken ct) =>
            Results.Ok(new
            {
                aviso = Aviso,
                centrais = await db.Centrais.AsNoTracking().OrderBy(c => c.MunicipioCodigo == null).ThenBy(c => c.Nome)
                    .Select(c => new { c.Codigo, c.Nome, c.Tipo, c.MunicipioCodigo, Modo = c.Modo.ToString(), c.AtrasoMs, c.ModoAlteradoEm,
                        Recebimentos = db.Recebimentos.Count(r => r.CentralCodigo == c.Codigo) })
                    .ToListAsync(ct),
            })).AllowAnonymous();

        g.MapGet("/centrais/{codigo}/recebimentos", async (string codigo, ClaimsPrincipal u, SimuladorDbContext db, CancellationToken ct) =>
        {
            if (!PodeOperar(u, codigo)) return Results.Forbid();
            var lista = await db.Recebimentos.AsNoTracking().Where(r => r.CentralCodigo == codigo).OrderByDescending(r => r.RecebidoEm).Take(200).ToListAsync(ct);
            return Results.Ok(new
            {
                aviso = Aviso,
                recebimentos = lista.Select(r => new
                {
                    r.Protocolo, r.TipoMensagem, r.IdentificadorReferencia, r.IdentificadorCorrelacao, r.MunicipioCodigo, r.RecebidoEm, r.Estado, r.Duplicatas,
                    // Localização precisa só para operador autorizado desta central (autorização demonstrativa).
                    Mensagem = JsonDocument.Parse(r.PayloadJson).RootElement,
                    Historico = JsonDocument.Parse(r.HistoricoJson).RootElement,
                }),
            });
        }).RequireAuthorization();

        g.MapPatch("/centrais/{codigo}/recebimentos/{protocolo}/estado", async (string codigo, string protocolo, AtualizacaoEstado corpo, ClaimsPrincipal u,
            SimuladorDbContext db, IHttpClientFactory http, IOptions<OpcoesCentraisSimuladas> op, ILoggerFactory lf, CancellationToken ct) =>
        {
            if (!PodeOperar(u, codigo)) return Results.Forbid();
            var permitidos = new[] { "EmAvaliacao", "EquipeDesignadaFicticia", "EmAtendimentoFicticio", "ConcluidoFicticio", "Aceita", "Recusada", "Concluida" };
            if (!permitidos.Contains(corpo.Estado)) return Results.BadRequest(new ErroCentralV1("estado_invalido", "Estado não permitido.", permitidos));
            var r = await db.Recebimentos.FirstOrDefaultAsync(x => x.CentralCodigo == codigo && x.Protocolo == protocolo, ct);
            if (r is null) return Results.NotFound();
            var agora = DateTimeOffset.UtcNow;
            var hist = JsonSerializer.Deserialize<List<EventoRecebimento>>(r.HistoricoJson, Js) ?? [];
            r.Estado = corpo.Estado;
            hist.Add(new(agora, $"Estado alterado para {corpo.Estado}{(corpo.Nota is null ? "" : $": {corpo.Nota}")} (evolução FICTÍCIA).", u.Identity?.Name));

            // Retorno ao Alerta em Ação por HTTP (comunicação bidirecional real entre sistemas de teste).
            var retornoOk = false;
            if (op.Value.UrlRetorno is { } url && op.Value.ChaveRetorno is { } chave)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var cli = http.CreateClient("retorno-centrais");
                    using var msg = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/api/integracoes/retorno-central")
                    {
                        Content = JsonContent.Create(new RetornoEstadoCentralV1(VersoesContrato.RetornoCentral, codigo, protocolo, r.IdentificadorReferencia,
                            r.TipoMensagem, corpo.Estado, corpo.Nota, agora, VersoesContrato.AmbienteDemonstracao)),
                    };
                    msg.Headers.Add(CabecalhosIntegracao.ChaveRetorno, chave);
                    var resp = await cli.SendAsync(msg, ct);
                    retornoOk = resp.IsSuccessStatusCode;
                    hist.Add(new(DateTimeOffset.UtcNow, $"Retorno ao Alerta em Ação: HTTP {(int)resp.StatusCode} em {sw.ElapsedMilliseconds} ms.", null));
                }
                catch (Exception ex)
                {
                    lf.CreateLogger("CentraisSimuladas").LogWarning(ex, "Falha no retorno ao Alerta em Ação");
                    hist.Add(new(DateTimeOffset.UtcNow, $"Falha no retorno ao Alerta em Ação: {ex.Message}", null));
                }
            }
            r.HistoricoJson = JsonSerializer.Serialize(hist, Js);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Protocolo, r.Estado, retornoEntregue = retornoOk, aviso = Aviso });
        }).RequireAuthorization();

        g.MapPut("/centrais/{codigo}/modo", async (string codigo, AlteracaoModo corpo, ClaimsPrincipal u, SimuladorDbContext db, CancellationToken ct) =>
        {
            if (!u.IsInRole("Administrador")) return Results.Forbid();
            var c = await db.Centrais.FirstOrDefaultAsync(x => x.Codigo == codigo, ct);
            if (c is null) return Results.NotFound();
            c.Modo = corpo.Modo;
            c.AtrasoMs = Math.Clamp(corpo.AtrasoMs ?? 0, 0, 20_000);
            c.ModoAlteradoEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { c.Codigo, Modo = c.Modo.ToString(), c.AtrasoMs });
        }).RequireAuthorization();

        return g;
    }

    public static bool PodeOperar(ClaimsPrincipal u, string codigo) =>
        u.IsInRole("Administrador") || (u.FindFirst("central")?.Value == codigo && (u.IsInRole("OperadorCentral") || u.IsInRole("Organizacao")));

    public static async Task GarantirCentraisAsync(SimuladorDbContext db, IEnumerable<(string Codigo, string Nome, string Tipo, string? Municipio)> centrais, CancellationToken ct)
    {
        var existentes = await db.Centrais.Select(c => c.Codigo).ToListAsync(ct);
        foreach (var c in centrais.Where(c => !existentes.Contains(c.Codigo)))
            db.Centrais.Add(new CentralSimulada { Codigo = c.Codigo, Nome = c.Nome, Tipo = c.Tipo, MunicipioCodigo = c.Municipio, Modo = ModoCentral.Normal, CriadaEm = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
    }
}

public sealed record AtualizacaoEstado(string Estado, string? Nota);
public sealed record AlteracaoModo(ModoCentral Modo, int? AtrasoMs);
