using System.Security.Claims;
using AlertaEmAcao.Contratos;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record OfertaRequest(Guid Id, string MunicipioCodigo, string? Regiao, TipoAjuda Tipo, string Descricao, string? Disponibilidade,
    string? OrganizacaoReferencia, string? ContatoAutorizado);
public sealed record ParticipacaoRequest(Guid NecessidadeId);

public static class ApoioEndpoints
{
    public static void MapApoio(this WebApplication app)
    {
        var g = app.MapGroup("/api/apoio").WithTags("Apoio comunitário");

        g.MapGet("/necessidades", async (IUnidadeTrabalho uow, string? municipio, CancellationToken ct) =>
        {
            var q = uow.Consulta<NecessidadePublicada>().Where(n => n.Status == StatusNecessidade.Aberta);
            if (municipio is not null) q = q.Where(n => n.MunicipioCodigo == municipio);
            var l = await uow.ListarAsync(q.OrderByDescending(n => n.PublicadaEm), ct);
            return Results.Ok(new
            {
                observacao = "Necessidades publicadas por organizações verificadas. Na demonstração, organizações e necessidades são FICTÍCIAS. Nunca se desloque para áreas de risco.",
                necessidades = l.Select(n => new { n.Id, n.OrganizacaoCodigo, n.OrganizacaoNome, n.MunicipioCodigo, tipo = n.Tipo.ToString(), n.Titulo, n.Descricao, n.PontoEntregaVerificado, n.Ficticia, n.PublicadaEm }),
            });
        });

        g.MapPost("/ofertas", async (OfertaRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoApoio s, CancellationToken ct) =>
        {
            var (o, ja) = await s.RegistrarOfertaAsync(new ComandoOferta(r.Id, OcorrenciasEndpoints.Dispositivo(ctx), r.MunicipioCodigo, r.Regiao, r.Tipo,
                r.Descricao, r.Disponibilidade, r.OrganizacaoReferencia, r.ContatoAutorizado), p.Id(), ct);
            return ja ? Results.Ok(Visao(o, null)) : Results.Created($"/api/apoio/ofertas/{o.Id}", Visao(o, null));
        });

        g.MapPost("/ofertas/{id:guid}/participacao", async (Guid id, ParticipacaoRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoApoio s, CancellationToken ct) =>
        {
            var o = await s.SolicitarParticipacaoAsync(id, r.NecessidadeId, OcorrenciasEndpoints.Dispositivo(ctx), p.Id(), ct);
            return Results.Ok(Visao(o, null));
        });

        g.MapGet("/ofertas/minhas", async (HttpContext ctx, ClaimsPrincipal p, AlertaDbContext db, CancellationToken ct) =>
        {
            var d = OcorrenciasEndpoints.Dispositivo(ctx);
            var uid = p.Id();
            var os = await db.Set<OfertaAjuda>().AsNoTracking().Where(o => o.DispositivoId == d || (uid != null && o.UsuarioId == uid))
                .OrderByDescending(o => o.CriadaEm).Take(30).ToListAsync(ct);
            var ids = os.Select(o => o.Id).ToList();
            var encs = (await db.Set<EncaminhamentoInstitucional>().AsNoTracking().Where(e => ids.Contains(e.ReferenciaId)).ToListAsync(ct))
                .GroupBy(e => e.ReferenciaId).ToDictionary(x => x.Key, x => x.OrderByDescending(e => e.CriadoEm).First());
            return Results.Ok(os.Select(o => Visao(o, encs.GetValueOrDefault(o.Id))));
        });

        // Painel da organização (simulada)
        app.MapGet("/api/painel/organizacao/ofertas", async (ClaimsPrincipal u, AlertaDbContext db, CancellationToken ct) =>
        {
            var central = u.Central();
            var encs = await db.Set<EncaminhamentoInstitucional>().AsNoTracking()
                .Where(e => e.TipoMensagem == TipoMensagem.OfertaApoio && (u.Admin() || e.InstituicaoCodigo == central)).ToListAsync(ct);
            var ids = encs.Select(e => e.ReferenciaId).ToList();
            var os = await db.Set<OfertaAjuda>().AsNoTracking().Where(o => ids.Contains(o.Id)).ToListAsync(ct);
            return Results.Ok(new { aviso = CentraisSimuladas.ExtensoesCentraisSimuladas.Aviso, ofertas = os.Select(o => Visao(o, encs.FirstOrDefault(e => e.ReferenciaId == o.Id), incluirContato: true)) });
        }).RequireAuthorization("Organizacao").WithTags("Apoio comunitário");
    }

    private static object Visao(OfertaAjuda o, EncaminhamentoInstitucional? e, bool incluirContato = false) => new
    {
        o.Id, o.MunicipioCodigo, o.RegiaoDisponibilidade, tipo = o.Tipo.ToString(), o.Descricao, o.Disponibilidade, o.NecessidadeId,
        status = o.Status.ToString(), o.ProtocoloOrganizacao, o.Historico, o.CriadaEm,
        contatoAutorizado = incluirContato ? o.ContatoAutorizado : null,
        encaminhamento = e is null ? null : new { e.Id, organizacao = e.InstituicaoCodigo, status = e.Status.ToString(), e.Protocolo, e.ConfirmadoEm, e.Tentativas, e.UltimoErro },
    };
}

public static class IntegracoesEndpoints
{
    public const string CabecalhoGateway = "X-Gateway-Chave";

    public static void MapIntegracoes(this WebApplication app)
    {
        var g = app.MapGroup("/api").WithTags("Integrações");

        // Retorno assíncrono das centrais (simuladas) — autenticado por chave compartilhada.
        g.MapPost("/integracoes/retorno-central", async (RetornoEstadoCentralV1 r, HttpRequest req, ChaveRetornoCentrais chave, ServicoDespacho s, CancellationToken ct) =>
        {
            if (!ChavesCentrais.Comparar(req.Headers[CabecalhosIntegracao.ChaveRetorno].FirstOrDefault(), chave.Valor)) return Results.Unauthorized();
            if (r.Versao != VersoesContrato.RetornoCentral) return Results.BadRequest(new { erro = "Versão de contrato não suportada." });
            await s.RegistrarRetornoAsync(r, ct);
            return Results.Ok(new { recebido = true });
        });

        // Gateway SIMULADO (LoRa) / retransmissão Nearby: recebe pacote compacto e registra a ocorrência.
        g.MapPost("/gateway/pacotes", async (PacoteGatewayV1 p, HttpRequest req, IConfiguration cfg, ServicoOcorrencias s, CancellationToken ct) =>
        {
            var segredo = cfg["Centrais:SegredoMestre"]!;
            if (!ChavesCentrais.Comparar(req.Headers[CabecalhoGateway].FirstOrDefault(), ChavesCentrais.Derivar(segredo, "gateway:" + p.Receptor)))
                return Results.Unauthorized();
            if (p.Versao != VersoesContrato.PacoteGateway) return Results.BadRequest(new { erro = "Versão de pacote não suportada." });
            if (p.Saltos is < 0 or > 8) return Results.BadRequest(new { erro = "Número de saltos inválido." });
            var canal = p.Canal == "nearby" ? CanalRecebimento.ProximidadeNearby : CanalRecebimento.GatewaySimulado;
            var res = await s.RegistrarSocorroAsync(new ComandoRegistrarSocorro(p.Id, p.DispositivoOrigem, p.CriadoEm, p.Latitude, p.Longitude, p.PrecisaoMetros, p.CriadoEm,
                new ComplementoOcorrencia { QuantidadePessoas = p.Pessoas, AguaSubindo = p.AguaSubindo, PessoasFeridas = p.Feridos == true ? 1 : null, Descricao = p.Texto },
                null, false, canal, $"{p.Receptor} ({p.Saltos} salto(s))"), null, ct);
            return Results.Ok(new { id = res.Ocorrencia.Id, recebidoEm = res.Ocorrencia.RecebidaNoServidorEm, duplicado = res.JaExistia, status = res.Ocorrencia.Status.ToString() });
        });

        // Protocolo de proximidade v2 (Android/iOS): o aparelho que recebeu por Nearby e tem internet atua como gateway.
        g.MapPost("/gateway/v2/mensagens", async (EntregaGatewayV2 entrega, HttpRequest req, IConfiguration cfg, ServicoOcorrencias s,
            IUnidadeTrabalho uow, IRelogio relogio, CancellationToken ct) =>
        {
            var receptor = req.Headers[CabecalhoReceptor].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(receptor) || receptor.Length > 64 ||
                !ChavesCentrais.Comparar(req.Headers[CabecalhoGateway].FirstOrDefault(), ChavesCentrais.Derivar(cfg["Centrais:SegredoMestre"]!, "gateway:" + receptor)))
                return Results.Unauthorized();
            var m = entrega.Mensagem;
            var erros = ProtocoloProximidade.Validar(m);
            if (erros.Count > 0) return Results.UnprocessableEntity(new { codigo = "mensagem_invalida", erros });
            if (!ProtocoloProximidade.VerificarIntegridade(m, ChaveGrupo(cfg)))
                return Results.UnprocessableEntity(new { codigo = "integridade_invalida", erros = new[] { "integridadeMensagem não confere com o grupo de demonstração." } });
            if (m.TipoMensagem is not (ProtocoloProximidade.Tipos.PedidoSocorro or ProtocoloProximidade.Tipos.Atualizacao))
                return Results.UnprocessableEntity(new { codigo = "tipo_nao_encaminhavel", erros = new[] { $"{m.TipoMensagem} não é encaminhado ao servidor." } });

            var ocorrenciaId = Guid.Parse(m.IdentificadorOcorrencia);
            var complemento = ConversorProximidade.Complemento(m.DadosEstruturados);
            var criadoEm = DateTimeOffset.Parse(m.HorarioCriacao, System.Globalization.CultureInfo.InvariantCulture);
            var recebidoLocal = DateTimeOffset.TryParse(entrega.RecebidoLocalmenteEm, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var rl) ? rl : relogio.Agora;
            var canalDesc = $"nearby:{entrega.Canal}";
            bool duplicado;
            if (m.TipoMensagem == ProtocoloProximidade.Tipos.PedidoSocorro)
            {
                var c = m.CoordenadasAutorizadas;
                var res = await s.RegistrarSocorroAsync(new ComandoRegistrarSocorro(ocorrenciaId, m.RemetentePseudonimizado, criadoEm,
                    c?.Latitude, c?.Longitude, c?.PrecisaoMetros, criadoEm, complemento, null, false, CanalRecebimento.ProximidadeNearby,
                    $"{receptor} (Nearby)", true, m.IdentificadorMensagem), null, ct);
                duplicado = res.JaExistia;
            }
            else
            {
                await s.ComplementarAsync(ocorrenciaId, m.RemetentePseudonimizado, null, complemento, ct);
                duplicado = false;
            }

            // Níveis 1–3 declarados na cadeia de proximidade (apenas uma vez por mensagem).
            var existentes = await uow.ListarAsync(uow.Consulta<ConfirmacaoEntrega>()
                .Where(x => x.OcorrenciaId == ocorrenciaId && x.IdentificadorMensagem == m.IdentificadorMensagem), ct);
            var agora = relogio.Agora;
            void Add(NivelEntrega n, string origem, string destino, DateTimeOffset h, string resultado, string? comp)
            {
                if (existentes.Any(x => x.Nivel == n)) return;
                uow.Adicionar(ConfirmacaoEntrega.Criar(ocorrenciaId, m.IdentificadorMensagem, n, origem, destino, h, canalDesc, resultado, comp, agora));
            }
            Add(NivelEntrega.PersistenciaLocal, m.RemetentePseudonimizado, m.RemetentePseudonimizado, criadoEm, "declarado pelo emissor", null);
            Add(NivelEntrega.RecebidoPorDispositivoProximo, m.RemetentePseudonimizado, entrega.ReceptorPseudonimizado, recebidoLocal, "persistido no receptor", m.IntegridadeMensagem);
            Add(NivelEntrega.RecebidoPorGateway, entrega.ReceptorPseudonimizado, receptor, recebidoLocal, "aceito para encaminhamento", null);
            try { await uow.SalvarAsync(ct); }
            catch (DbUpdateException) { /* corrida entre dois gateways entregando a mesma mensagem: o outro já gravou */ }

            var lista = await ListarConfirmacoesAsync(uow, ocorrenciaId, ct);
            return Results.Ok(new
            {
                identificadorOcorrencia = ocorrenciaId,
                identificadorMensagem = m.IdentificadorMensagem,
                duplicado,
                estadoTransmissao = lista.Max(x => (int)x.Nivel) >= 5 ? ProtocoloProximidade.Estados.RecebidoPelaCentralSimulada : ProtocoloProximidade.Estados.RecebidoPeloServidor,
                nivelComprovado = lista.Max(x => (int)x.Nivel),
                confirmacoes = lista.Select(ConfirmacaoDto),
            });
        });

        g.MapGet("/gateway/v2/ocorrencias/{id:guid}/confirmacoes", async (Guid id, HttpRequest req, IConfiguration cfg, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var receptor = req.Headers[CabecalhoReceptor].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(receptor) ||
                !ChavesCentrais.Comparar(req.Headers[CabecalhoGateway].FirstOrDefault(), ChavesCentrais.Derivar(cfg["Centrais:SegredoMestre"]!, "gateway:" + receptor)))
                return Results.Unauthorized();
            var lista = await ListarConfirmacoesAsync(uow, id, ct);
            if (lista.Count == 0) return Results.NotFound(new { codigo = "nao_recebida", mensagem = "O servidor ainda não recebeu esta ocorrência." });
            return Results.Ok(new { identificadorOcorrencia = id, nivelComprovado = lista.Max(x => (int)x.Nivel), confirmacoes = lista.Select(ConfirmacaoDto) });
        });
    }

    public const string CabecalhoReceptor = "X-Gateway-Receptor";

    public static byte[] ChaveGrupo(IConfiguration cfg) => ProtocoloProximidade.ChaveGrupo(cfg["Proximidade:CodigoGrupo"] ?? "ALERTA-DEMO-2026");

    public static async Task<List<ConfirmacaoEntrega>> ListarConfirmacoesAsync(IUnidadeTrabalho uow, Guid id, CancellationToken ct) =>
        (await uow.ListarAsync(uow.Consulta<ConfirmacaoEntrega>().Where(x => x.OcorrenciaId == id), ct))
            .OrderBy(x => x.Nivel).ThenBy(x => x.Horario).ToList();

    public static object ConfirmacaoDto(ConfirmacaoEntrega c) => new
    {
        nivel = (int)c.Nivel, descricao = ConfirmacaoEntrega.Descrever(c.Nivel), c.IdentificadorMensagem, c.OcorrenciaId,
        c.Origem, c.Destino, c.Horario, c.Canal, c.Resultado, c.Comprovante, c.RegistradoEm,
    };
}

/// <summary>Converte dadosEstruturados (protocolo v2) no complemento da ocorrência. Campos desconhecidos são ignorados.</summary>
public static class ConversorProximidade
{
    public static ComplementoOcorrencia Complemento(System.Text.Json.JsonElement? dados)
    {
        if (dados is not { ValueKind: System.Text.Json.JsonValueKind.Object } d) return new();
        bool? B(string k) => d.TryGetProperty(k, out var v) && v.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False ? v.GetBoolean() : null;
        int? I(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number && v.TryGetInt32(out var n) && n is >= 0 and <= 500 ? n : null;
        string? S(string k, int max) => d.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? (v.GetString() is { } t && t.Length > max ? t[..max] : v.GetString()) : null;
        List<PessoaEnvolvida>? pessoas = null;
        if (d.TryGetProperty("idades", out var ids) && ids.ValueKind == System.Text.Json.JsonValueKind.Array)
            pessoas = ids.EnumerateArray().Where(x => x.ValueKind == System.Text.Json.JsonValueKind.Number && x.TryGetInt32(out var a) && a is >= 0 and <= 120)
                .Take(30).Select(x => new PessoaEnvolvida(x.GetInt32(), false, false, false)).ToList();
        return new ComplementoOcorrencia
        {
            QuantidadePessoas = I("quantidadePessoas"),
            Pessoas = pessoas is { Count: > 0 } ? pessoas : null,
            PessoasFeridas = I("pessoasFeridas"),
            MobilidadeReduzida = B("mobilidadeReduzida"),
            Ilhado = B("ilhado"),
            AguaSubindo = B("aguaSubindo"),
            Correnteza = B("correnteza"),
            RiscoDeslizamento = B("riscoDeslizamento"),
            Desmoronamento = B("desmoronamento"),
            BarreiraComSinaisInstabilidade = B("barreiraInstavel"),
            AcessoBloqueado = B("acessoBloqueado"),
            ReferenciaLocal = S("referenciaLocal", 200),
            Descricao = S("descricao", 2000),
        };
    }
}
