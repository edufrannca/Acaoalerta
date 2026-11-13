using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record OfertaPedidoRequest(Guid Id, TipoAjuda Tipo, string Descricao, string? Disponibilidade, string? ContatoAutorizado, bool CienteSeguranca);
public sealed record AcompanhamentoRequest(string Acao, string? Nota);
public sealed record DecisaoOfertaRequest(bool Aceita, string? Nota);
public sealed record EncaminhamentoOfertaRequest(string Instrucao);

/// <summary>
/// Pedido gravado na PWA de um aparelho SEM internet e lido por câmera (QR) num aparelho próximo com internet.
/// Leva só o mínimo: identificador gerado no emissor, pseudônimo do emissor, horário, posição do EMISSOR e marcações.
/// </summary>
public sealed record PedidoQrV1(
    string V,
    Guid Id,
    string Em,
    DateTimeOffset Criado,
    double? Lat,
    double? Lon,
    double? Prec,
    DateTimeOffset? Lido,
    Dictionary<string, bool>? M,
    int? Pessoas,
    List<string>? Nec,
    CompartilhamentoComunitario? Comp,
    string H,
    bool? D = null);

public sealed record RetransmissaoQrRequest(PedidoQrV1 Pedido, double? LatitudeRetransmissor, double? LongitudeRetransmissor, double? PrecisaoRetransmissor);

public static class ComunidadeEndpoints
{
    public const string VersaoQr = "aea-qr/1";

    /// <summary>Texto canônico do QR para verificação de integridade (detecta corrupção/edição acidental; não é autenticação).</summary>
    public static string CanonicoQr(PedidoQrV1 p)
    {
        static string N(double? v) => v is null ? "" : v.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var marcas = p.M is null ? "" : string.Join(",", p.M.Where(x => x.Value).Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
        var nec = p.Nec is null ? "" : string.Join(",", p.Nec.OrderBy(x => x, StringComparer.Ordinal));
        var comp = p.Comp is { Autorizado: true } c ? $"1{(c.IncluirPessoas ? 1 : 0)}{(c.IncluirNecessidades ? 1 : 0)}" : "0";
        return string.Join('|', p.V, p.Id.ToString("D"), p.Em, p.Criado.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'"), N(p.Lat), N(p.Lon), N(p.Prec),
            marcas, p.Pessoas?.ToString() ?? "", nec, comp, p.D == true ? "d" : "");
    }

    public static string HashQr(PedidoQrV1 p) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CanonicoQr(p))))[..32];

    public static void MapComunidade(this WebApplication app)
    {
        var g = app.MapGroup("/api").WithTags("Comunidade");

        // Resumo comunitário: só pedidos que o solicitante autorizou, área aproximada, sem dados pessoais.
        g.MapGet("/comunidade/pedidos", async (AlertaDbContext db, IRelogio relogio, ClaimsPrincipal u, double? lat, double? lon, double? raioKm,
            string? municipio, bool? incluirDemonstrativos, CancellationToken ct) =>
        {
            var agora = relogio.Agora;
            var desde = agora.AddHours(-24);
            var raio = Math.Clamp(raioKm ?? 5, 0.5, 30);
            PontoGeo? origem = PontoGeo.TentarCriar(lat, lon, out var po) ? po : null;
            var demo = incluirDemonstrativos == true;
            var q = db.Set<Ocorrencia>().AsNoTracking().Where(o => o.RecebidaNoServidorEm >= desde && o.Status != StatusOcorrencia.Encerrada
                && o.Verificacao != VerificacaoOcorrencia.DescartadaPorOperador && o.Demonstracao == demo);
            if (municipio is not null) q = q.Where(o => o.MunicipioCodigo == municipio);
            var todas = await q.OrderByDescending(o => o.RecebidaNoServidorEm).Take(500).ToListAsync(ct);

            var compartilhadas = todas.Where(o => o.Compartilhamento.Autorizado && o.Ponto is not null).Select(o =>
            {
                var area = AreaAproximada.De(o.Ponto!.Value, o.PrecisaoMetros);
                // Distância de QUEM CONSULTA até a BORDA da área (nunca até o ponto real).
                double? dist = origem is null ? null : Math.Max(0, origem.Value.DistanciaMetros(area.Centro) - area.RaioMetros) / 1000d;
                return (o, area, dist);
            }).Where(x => x.dist is null || x.dist <= raio).OrderBy(x => x.dist ?? 0).ThenByDescending(x => x.o.RecebidaNoServidorEm).Take(100).ToList();

            var ids = compartilhadas.Select(x => x.o.Id).ToList();
            var ofertas = (await db.Set<OfertaAjuda>().AsNoTracking().Where(f => f.OcorrenciaId != null && ids.Contains(f.OcorrenciaId.Value)).ToListAsync(ct))
                .Where(f => f.Ativa || f.Status == StatusOferta.Concluida).GroupBy(f => f.OcorrenciaId!.Value).ToDictionary(x => x.Key, x => x.ToList());

            var itens = compartilhadas.Select(x =>
            {
                var (o, area, dist) = x;
                var c = o.Compartilhamento;
                var cp = o.Complemento;
                var ofs = ofertas.GetValueOrDefault(o.Id) ?? [];
                return new
                {
                    id = o.Id,
                    referencia = o.Id.ToString("N")[..6].ToUpperInvariant(),
                    tipos = ResumoComunitario.TiposEmergencia(cp),
                    pessoas = c.IncluirPessoas ? cp.QuantidadePessoas ?? cp.Pessoas?.Count : null,
                    criancas = c.IncluirPessoas ? ResumoComunitario.Faixa(ResumoComunitario.Criancas(cp)) : null,
                    idosos = c.IncluirPessoas ? ResumoComunitario.Faixa(ResumoComunitario.Idosos(cp)) : null,
                    mobilidadeReduzida = c.IncluirPessoas ? ResumoComunitario.Faixa(ResumoComunitario.Mobilidade(cp)) : null,
                    necessidades = c.IncluirNecessidades ? (cp.Necessidades ?? []).Select(n => new { codigo = n, rotulo = NecessidadesDeclaradas.Rotulos[n] }) : null,
                    horarioAproximado = ResumoComunitario.HorarioAproximado(o.CriadaNoDispositivoEm),
                    situacao = ResumoComunitario.Situacao(o.Status),
                    verificacao = ResumoComunitario.Verificacao(o.Verificacao),
                    municipio = MunicipiosRmr.Nome(o.MunicipioCodigo),
                    area = new { latitude = area.CentroLatitude, longitude = area.CentroLongitude, raioMetros = area.RaioMetros },
                    distanciaKm = dist is null ? (double?)null : Math.Round(dist.Value * 2, MidpointRounding.AwayFromZero) / 2,
                    ofertasEmAndamento = ofs.Count(f => f.Ativa),
                    podeOferecerAjuda = true, // Todo pedido compartilhado e ativo aceita oferta, mesmo sem necessidades declaradas.
                    demonstrativo = o.Demonstracao,
                };
            }).ToList();

            // Totais regionais apenas com número mínimo de pedidos (evita reidentificar uma família).
            object? agregados = null;
            if (compartilhadas.Count >= ResumoComunitario.MinimoParaAgregar)
            {
                var comPessoas = compartilhadas.Where(x => x.o.Compartilhamento.IncluirPessoas).Select(x => x.o.Complemento).ToList();
                var comNec = compartilhadas.Where(x => x.o.Compartilhamento.IncluirNecessidades).SelectMany(x => x.o.Complemento.Necessidades ?? []).ToList();
                agregados = new
                {
                    pedidos = compartilhadas.Count,
                    pessoasInformadas = comPessoas.Sum(c => c.QuantidadePessoas ?? c.Pessoas?.Count ?? 0),
                    pedidosComCriancas = comPessoas.Count(c => ResumoComunitario.Criancas(c) > 0),
                    pedidosComIdosos = comPessoas.Count(c => ResumoComunitario.Idosos(c) > 0),
                    pedidosComMobilidadeReduzida = comPessoas.Count(c => ResumoComunitario.Mobilidade(c) > 0),
                    necessidades = comNec.GroupBy(n => n).Select(n => new { codigo = n.Key, rotulo = NecessidadesDeclaradas.Rotulos[n.Key], pedidos = n.Count() }).OrderByDescending(n => n.pedidos),
                };
            }

            return Results.Ok(new
            {
                atualizadoEm = agora,
                janelaHoras = 24,
                busca = origem is null ? null : new { centro = "sua localização atual (não armazenada)", raioKm = raio },
                demonstrativos = demo,
                pedidos = itens,
                // Só a contagem, dentro do mesmo raio da busca (ou município), nunca detalhes.
                naoCompartilhados = todas.Count(o => !o.Compartilhamento.Autorizado
                    && (origem is null || (o.Ponto is { } pn && origem.Value.DistanciaMetros(pn) <= raio * 1000))),
                agregados,
                agregadosOmitidos = agregados is null && compartilhadas.Count > 0
                    ? $"Totais da região aparecem a partir de {ResumoComunitario.MinimoParaAgregar} pedidos compartilhados, para proteger as famílias." : null,
                privacidade = "A comunidade vê apenas o que cada solicitante autorizou: tipo de emergência, número de pessoas, faixas de crianças, idosos e mobilidade reduzida, " +
                              "necessidades e área aproximada. Nomes, telefones, dados de saúde, relatos e posição exata ficam só com a equipe autorizada.",
                areaAproximada = "Cada pedido aparece como um círculo que contém o local registrado. O centro do círculo NÃO é a posição da pessoa.",
                seguranca = OfertaAjuda.AvisoSeguranca,
                ambiente = "Ambiente de demonstração: centrais simuladas; nenhum órgão real é acionado.",
            });
        });

        // Solicitante decide (e pode revogar) o que a comunidade vê.
        g.MapPut("/ocorrencias/{id:guid}/compartilhamento", async (Guid id, CompartilhamentoComunitario c, HttpContext ctx, ClaimsPrincipal p,
            ServicoOcorrencias s, CancellationToken ct) =>
        {
            var o = await s.DefinirCompartilhamentoAsync(id, OcorrenciasEndpoints.Dispositivo(ctx), p.Id(), c, ct);
            return Results.Ok(new { o.Compartilhamento.Autorizado, o.Compartilhamento.IncluirPessoas, o.Compartilhamento.IncluirNecessidades, o.Compartilhamento.AtualizadoEm });
        }).RequireRateLimiting("socorro");

        // Oferta de ajuda a um pedido compartilhado.
        g.MapPost("/comunidade/pedidos/{id:guid}/ofertas", async (Guid id, OfertaPedidoRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoApoio s, CancellationToken ct) =>
        {
            var (o, ja) = await s.OfertarParaPedidoAsync(new ComandoOfertaPedido(r.Id, OcorrenciasEndpoints.Dispositivo(ctx), id, r.Tipo, r.Descricao,
                r.Disponibilidade, r.ContatoAutorizado, r.CienteSeguranca), p.Id(), ct);
            return ja ? Results.Ok(VisaoOfertaVoluntario(o)) : Results.Created($"/api/apoio/ofertas/{o.Id}", VisaoOfertaVoluntario(o));
        }).RequireRateLimiting("socorro");

        g.MapPost("/apoio/ofertas/{id:guid}/acompanhamento", async (Guid id, AcompanhamentoRequest r, HttpContext ctx, ClaimsPrincipal p,
            AlertaDbContext db, IRelogio relogio, CancellationToken ct) =>
        {
            var d = OcorrenciasEndpoints.Dispositivo(ctx);
            var o = await db.Set<OfertaAjuda>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ErroNaoEncontrado("Oferta não encontrada.");
            if (o.DispositivoId != d && (p.Id() is null || o.UsuarioId != p.Id())) throw new ErroAcessoNegado("Oferta de outro voluntário.");
            o.Acompanhar(r.Acao, r.Nota, relogio.Agora);
            await db.SaveChangesAsync(ct);
            return Results.Ok(VisaoOfertaVoluntario(o));
        });

        // Pedido lido por QR: o aparelho com internet encaminha; a posição dele fica SEPARADA da do emissor.
        g.MapPost("/retransmissoes/qr", async (RetransmissaoQrRequest r, HttpContext ctx, ServicoOcorrencias s, AlertaDbContext db,
            IRelogio relogio, CancellationToken ct) =>
        {
            var retransmissor = OcorrenciasEndpoints.Dispositivo(ctx);
            var p = r.Pedido;
            if (p.V != VersaoQr) return Results.UnprocessableEntity(new { codigo = "versao_qr", erro = "Versão do código QR não suportada." });
            if (string.IsNullOrWhiteSpace(p.Em) || !p.Em.StartsWith("p:", StringComparison.Ordinal) || p.Em.Length > 40)
                return Results.UnprocessableEntity(new { codigo = "emissor_invalido", erro = "Pseudônimo do emissor inválido." });
            if (!string.Equals(HashQr(p), p.H, StringComparison.OrdinalIgnoreCase))
                return Results.UnprocessableEntity(new { codigo = "integridade_invalida", erro = "O conteúdo do QR não confere com o código de integridade. Leia novamente." });
            if (Ocorrencia.PseudonimoEmissor(retransmissor) == p.Em)
                return Results.UnprocessableEntity(new { codigo = "mesmo_aparelho", erro = "Este é o próprio aparelho que criou o pedido." });
            var m = p.M ?? [];
            bool? B(string k) => m.TryGetValue(k, out var v) && v ? true : null;
            var comp = new ComplementoOcorrencia
            {
                QuantidadePessoas = p.Pessoas is >= 1 and <= 500 ? p.Pessoas : null,
                Ilhado = B("ilhado"), AguaSubindo = B("aguaSubindo"), Correnteza = B("correnteza"), RiscoDeslizamento = B("riscoDeslizamento"),
                Desmoronamento = B("desmoronamento"), BarreiraComSinaisInstabilidade = B("barreiraComSinaisInstabilidade"), AcessoBloqueado = B("acessoBloqueado"),
                EquipamentoMedicoEssencial = B("equipamentoMedicoEssencial"), MobilidadeReduzida = B("mobilidadeReduzida"), Sozinho = B("sozinho"),
                RiscoSegurancaPublica = B("riscoSegurancaPublica"),
                Necessidades = p.Nec?.Where(NecessidadesDeclaradas.Rotulos.ContainsKey).Distinct().ToList() is { Count: > 0 } n ? n : null,
            };
            // A posição gravada é a do EMISSOR (lida no aparelho da família). Sem coordenadas no QR, o pedido fica "sem localização".
            var res = await s.RegistrarSocorroAsync(new ComandoRegistrarSocorro(p.Id, p.Em, p.Criado, p.Lat, p.Lon, p.Prec, p.Lido ?? p.Criado,
                comp, null, false, CanalRecebimento.ProximidadeQr, $"aparelho próximo {retransmissor[..Math.Min(8, retransmissor.Length)]}… (QR)",
                p.D == true, $"qr:{p.H}", p.Comp), null, ct);
            var o = res.Ocorrencia;
            if (!res.JaExistia && PontoGeo.TentarCriar(r.LatitudeRetransmissor, r.LongitudeRetransmissor, out var pr))
            {
                var tracked = await db.Set<Ocorrencia>().FirstAsync(x => x.Id == o.Id, ct);
                tracked.RegistrarPosicaoRetransmissor(new PosicaoDispositivo(pr.Latitude, pr.Longitude, r.PrecisaoRetransmissor, relogio.Agora, "gps_retransmissor"),
                    "QR", relogio.Agora);
                await db.SaveChangesAsync(ct);
            }
            var nivel = res.Encaminhamentos.Any(e => e.Status == Modulos.Encaminhamento.StatusEncaminhamento.Recebido) ? 5 : 4;
            return Results.Ok(new
            {
                id = o.Id,
                referencia = o.Id.ToString("N")[..6].ToUpperInvariant(),
                duplicado = res.JaExistia,
                recebidoEm = o.RecebidaNoServidorEm,
                nivelComprovado = nivel,
                nivelDescricao = ConfirmacaoEntrega.Descrever(nivel == 5 ? NivelEntrega.RecebidoPelaCentralDemonstrativa : NivelEntrega.RecebidoPeloBackend),
                localizacaoEmissor = o.Latitude is null ? "não informada pelo aparelho da família" : "registrada pelo aparelho da família",
                observacao = "Você encaminhou o pedido de outra pessoa. Ela verá a confirmação quando o aparelho dela voltar a ter internet. " +
                             "Centrais SIMULADAS: nenhum órgão real foi acionado. Em risco imediato, ligue 193 ou 199.",
            });
        }).RequireRateLimiting("socorro");

        // Coordenação: equipe autorizada avalia e encaminha ofertas da comunidade.
        var painel = app.MapGroup("/api/painel").WithTags("Painel de coordenação (demonstração)").RequireAuthorization("Operador");

        painel.MapGet("/ofertas-comunitarias", async (ClaimsPrincipal u, AlertaDbContext db, CancellationToken ct) =>
        {
            var visiveis = await PainelEndpoints.IdsVisiveis(u, db, ct);
            var q = db.Set<OfertaAjuda>().AsNoTracking().Where(o => o.OcorrenciaId != null);
            if (visiveis is not null) q = q.Where(o => visiveis.Contains(o.OcorrenciaId!.Value));
            var l = await q.OrderByDescending(o => o.CriadaEm).Take(200).ToListAsync(ct);
            return Results.Ok(l.Select(o => VisaoOfertaEquipe(o)));
        });

        painel.MapPost("/ofertas/{id:guid}/decisao", async (Guid id, DecisaoOfertaRequest r, ClaimsPrincipal u, AlertaDbContext db, IRelogio relogio, CancellationToken ct) =>
        {
            var o = await OfertaVisivelAsync(id, u, db, ct);
            o.AvaliarPelaEquipe(r.Aceita, r.Nota, Ator(u), relogio.Agora);
            Auditar(db, u, relogio, r.Aceita ? "aceitar_oferta_comunitaria" : "recusar_oferta_comunitaria", o.Id);
            await db.SaveChangesAsync(ct);
            return Results.Ok(VisaoOfertaEquipe(o));
        });

        painel.MapPost("/ofertas/{id:guid}/encaminhamento", async (Guid id, EncaminhamentoOfertaRequest r, ClaimsPrincipal u, AlertaDbContext db, IRelogio relogio, CancellationToken ct) =>
        {
            var o = await OfertaVisivelAsync(id, u, db, ct);
            o.Encaminhar(r.Instrucao, Ator(u), relogio.Agora);
            Auditar(db, u, relogio, "encaminhar_oferta_comunitaria", o.Id);
            await db.SaveChangesAsync(ct);
            return Results.Ok(VisaoOfertaEquipe(o));
        });
    }

    private static string Ator(ClaimsPrincipal u) => u.Central() ?? (u.Admin() ? "administrador" : "equipe");

    private static void Auditar(AlertaDbContext db, ClaimsPrincipal u, IRelogio relogio, string acao, Guid id) =>
        db.Add(new RegistroAuditoria { Em = relogio.Agora, UsuarioId = u.Id()?.ToString(), Papel = u.Central() ?? "admin", Acao = acao, Recurso = $"oferta:{id}" });

    private static async Task<OfertaAjuda> OfertaVisivelAsync(Guid id, ClaimsPrincipal u, AlertaDbContext db, CancellationToken ct)
    {
        var o = await db.Set<OfertaAjuda>().FirstOrDefaultAsync(x => x.Id == id && x.OcorrenciaId != null, ct) ?? throw new ErroNaoEncontrado("Oferta não encontrada.");
        var visiveis = await PainelEndpoints.IdsVisiveis(u, db, ct);
        if (visiveis is not null && !visiveis.Contains(o.OcorrenciaId!.Value)) throw new ErroAcessoNegado("Pedido fora do escopo desta central.");
        return o;
    }

    /// <summary>O voluntário nunca vê dados do solicitante; só a orientação da equipe, depois do encaminhamento.</summary>
    public static object VisaoOfertaVoluntario(OfertaAjuda o) => new
    {
        o.Id, tipo = o.Tipo.ToString(), o.Descricao, o.Disponibilidade, status = o.Status.ToString(), o.CriadaEm, o.Historico,
        pedido = o.OcorrenciaId is null ? null : o.OcorrenciaId.Value.ToString("N")[..6].ToUpperInvariant(),
        municipio = MunicipiosRmr.Nome(o.MunicipioCodigo),
        instrucaoEquipe = o.Status is StatusOferta.Encaminhada or StatusOferta.Concluida ? o.InstrucaoEquipe : null,
        seguranca = OfertaAjuda.AvisoSeguranca,
    };

    public static object VisaoOfertaEquipe(OfertaAjuda o) => new
    {
        o.Id, o.OcorrenciaId, tipo = o.Tipo.ToString(), o.Descricao, o.Disponibilidade, status = o.Status.ToString(), o.CriadaEm, o.Historico,
        municipio = MunicipiosRmr.Nome(o.MunicipioCodigo), o.InstrucaoEquipe,
        // Contato do voluntário: apenas se ele autorizou, e só para a equipe.
        contatoAutorizado = o.ContatoAutorizado,
    };
}
