using System.Text.Json;
using System.Text.Json.Nodes;
using AlertaEmAcao.Infraestrutura.Persistencia;
using AlertaEmAcao.Modulos.Abrigos;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.EntityFrameworkCore;

namespace AlertaEmAcao.Api.Endpoints;

public static class Endpoints
{
    public static void MapearEndpoints(this WebApplication app)
    {
        app.MapPublico();
        app.MapIdentidade();
        app.MapOcorrencias();
        app.MapPainel();
        app.MapApoio();
        app.MapIntegracoes();
        app.MapVozLocalidade();
    }

    public static object Fonte(ReferenciaFonte f) => new { f.Nome, f.Url, f.AtualizadoEm, f.Oficial, f.Demonstrativa };

    /// <summary>Rede da estação a partir do nome técnico da APAC ("[APAC] Janga 2" → "APAC"). O nome técnico nunca é usado como bairro.</summary>
    public static string? RedeEstacao(string nome) => nome.StartsWith('[') && nome.IndexOf(']') is > 1 and var f ? nome[1..f] : null;
    public static string NomeEstacao(string nome) => nome.StartsWith('[') && nome.IndexOf(']') is > 0 and var f ? nome[(f + 1)..].Trim() : nome;
}

public static class PublicoEndpoints
{
    public static readonly object[] ContatosEmergencia =
    [
        new { numero = "193", nome = "Corpo de Bombeiros", uso = "Incêndio, resgate, desabamento, afogamento, pessoas ilhadas" },
        new { numero = "199", nome = "Defesa Civil", uso = "Risco de deslizamento, alagamento, desabrigados, vistoria de imóvel" },
        new { numero = "192", nome = "SAMU", uso = "Urgência e emergência médica" },
        new { numero = "190", nome = "Polícia Militar", uso = "Segurança pública, crime em andamento" },
    ];

    public static void MapPublico(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Público");

        app.MapGet("/health", async (AlertaDbContext db, CancellationToken ct) =>
        {
            try
            {
                var postgis = await db.Database.SqlQuery<string>($"SELECT postgis_lib_version() AS \"Value\"").FirstAsync(ct);
                return Results.Ok(new { status = "ok", banco = "ok", postgis, em = DateTimeOffset.UtcNow });
            }
            catch (Exception e) { return Results.Json(new { status = "degradado", banco = e.GetType().Name }, statusCode: 503); }
        }).AllowAnonymous();

        api.MapGet("/estado", (IRegistroFontes fontes, IProvedorIA ia, IEnviadorPush push) => Results.Ok(new
        {
            ambiente = "DEMONSTRACAO",
            aviso = "Ambiente de demonstração. Centrais institucionais são SIMULADAS; nenhum órgão real é acionado.",
            ia = new { configurada = ia.Configurado, descricao = ia.Descricao, motorRegras = MotorRegrasUrgencia.Versao },
            push = new { configurado = push.Configurado },
            fontes = fontes.Listar(),
            contatosEmergencia = ContatosEmergencia,
            em = DateTimeOffset.UtcNow,
        }));

        api.MapGet("/contatos-emergencia", () => Results.Ok(new
        {
            contatos = ContatosEmergencia,
            observacao = "Números nacionais de emergência. A ligação é feita pelo seu telefone; o aplicativo não acompanha o atendimento da chamada.",
        }));

        api.MapGet("/municipios", (ICatalogoMunicipios cat) => Results.Ok(cat.Listar().Select(m => new
        {
            codigo = m.CodigoIbge, m.Nome, m.Uf, fonteLimites = m.FonteGeometria, limitesAoVivo = m.GeometriaOficialAoVivo,
            limitesAtualizadosEm = m.GeometriaAtualizadaEm, observacoes = m.ObservacoesDados,
        })));

        api.MapGet("/municipios/limites", (ICatalogoMunicipios cat) =>
        {
            var fc = new JsonObject
            {
                ["type"] = "FeatureCollection",
                ["features"] = new JsonArray(cat.Listar().Select(m => (JsonNode)new JsonObject
                {
                    ["type"] = "Feature",
                    ["properties"] = new JsonObject { ["codigo"] = m.CodigoIbge, ["nome"] = m.Nome, ["fonte"] = m.FonteGeometria },
                    ["geometry"] = JsonNode.Parse(m.GeometriaGeoJson),
                }).ToArray()),
            };
            return Results.Text(fc.ToJsonString(), "application/geo+json");
        });

        api.MapGet("/municipios/identificar", (double lat, double lon, ICatalogoMunicipios cat) =>
        {
            var m = cat.Identificar(new PontoGeo(lat, lon));
            return Results.Ok(new { encontrado = m is not null, codigo = m?.Codigo, nome = m?.Nome, porTolerancia = m?.PorTolerancia ?? false });
        });

        api.MapGet("/monitoramento/chuva", async (IFonteChuva fonte, IRelogio relogio, string? municipio, CancellationToken ct) =>
        {
            var r = await fonte.ObterRmrAsync(ct);
            var agora = relogio.Agora;
            return Results.Ok(new
            {
                fonte = r.Fonte, url = r.Url, disponivel = r.Disponivel, erro = r.Erro, consultadoEm = r.ConsultadoEm,
                observacao = "Acumulados em mm por estação. Leituras com mais de 2 h são marcadas como desatualizadas.",
                estacoes = r.Leituras.Where(l => municipio is null || l.CodigoMunicipio == municipio).Select(l => new
                {
                    l.EstacaoId, l.Nome, rede = Endpoints.RedeEstacao(l.Nome), nomeEstacao = Endpoints.NomeEstacao(l.Nome), l.Latitude, l.Longitude, l.Municipio, l.CodigoMunicipio, l.UltimaLeituraEm,
                    estado = l.Estado(agora).ToString(), l.Mm1h, l.Mm3h, l.Mm6h, l.Mm12h, l.Mm24h, l.Mm48h, l.Mm72h,
                }),
            });
        });

        api.MapGet("/alertas/vigentes", async (IUnidadeTrabalho uow, IRelogio relogio, string? municipio, CancellationToken ct) =>
        {
            var agora = relogio.Agora;
            var avisos = await uow.ListarAsync(uow.Consulta<Aviso>().Where(a => a.FimEm >= agora && a.InicioEm <= agora).OrderByDescending(a => a.InicioEm), ct);
            return Results.Ok(avisos.Where(a => municipio is null || a.MunicipiosCodigos.Contains(municipio) || a.PoligonoGeoJson is not null)
                .Select(a => new
                {
                    a.Id, origem = a.Origem.ToString(), oficial = a.Oficial, a.Titulo, a.Severidade, a.Cor, a.InicioEm, a.FimEm,
                    municipios = a.MunicipiosCodigos, a.Riscos, a.Instrucoes, a.FonteNome, a.FonteUrl, a.ObtidoEm,
                    poligono = a.PoligonoGeoJson is null ? null : JsonNode.Parse(a.PoligonoGeoJson),
                    rotulo = a.Oficial ? "Aviso oficial" : "AVISO DEMONSTRATIVO — não oficial",
                }));
        });

        api.MapGet("/territorio/contexto", async (double lat, double lon, ServicoTerritorio s, CancellationToken ct) =>
        {
            var c = await s.AvaliarAsync(new PontoGeo(lat, lon), ct);
            return Results.Ok(new
            {
                municipio = c.MunicipioCodigo is null ? null : new { codigo = c.MunicipioCodigo, nome = c.MunicipioNome },
                c.ForaDaRmr,
                estados = c.Estados.Select(e => new { tipo = e.Tipo.ToString(), e.Titulo, e.Detalhe, fonte = e.Fonte is null ? null : Endpoints.Fonte(e.Fonte) }),
                orientacoes = c.OrientacoesPreventivas,
                limitacoes = c.LimitacoesConhecidas,
                estacaoMaisProxima = c.EstacaoMaisProxima is { } l ? new
                {
                    l.Nome, nomeOficial = l.Nome, rede = Endpoints.RedeEstacao(l.Nome), nomeEstacao = Endpoints.NomeEstacao(l.Nome),
                    l.Municipio, l.UltimaLeituraEm, l.Mm1h, l.Mm24h, l.Mm72h, distanciaKm = c.DistanciaEstacaoKm, estado = l.Estado(c.AvaliadoEm).ToString(), l.Fonte,
                } : null,
                avaliadoEm = c.AvaliadoEm,
                aviso = "A ausência de registros não comprova ausência de perigo. Siga as orientações da Defesa Civil.",
            });
        });

        api.MapGet("/territorio/areas-risco", async (IUnidadeTrabalho uow, IRegistroFontes fontes, string? municipio, CancellationToken ct) =>
        {
            var q = uow.Consulta<AreaRiscoDocumentada>();
            if (municipio is not null) q = q.Where(a => a.MunicipioCodigo == municipio);
            var lista = await uow.ListarAsync(q.Take(2000), ct);
            return Results.Ok(new
            {
                fonte = fontes.Obter(NomesFontes.RecifeAreasRisco),
                cobertura = "Recife — Regional Sul (base aberta da Defesa Civil do Recife). Outros municípios: sem base aberta identificada.",
                areas = lista.Select(a => new { a.Id, a.Descricao, a.Bairro, a.Localidade, a.Latitude, a.Longitude, a.MunicipioCodigo, a.Fonte, a.Licenca, a.ObtidaEm }),
            });
        });

        // Experiência pública: somente registros reais publicados pela fonte oficial. Os fictícios só aparecem
        // na área de demonstração (incluirDemonstrativos=true).
        api.MapGet("/abrigos", async (IUnidadeTrabalho uow, IRegistroFontes fontes, string? municipio, bool? incluirDemonstrativos, CancellationToken ct) =>
        {
            var q = uow.Consulta<Abrigo>();
            if (incluirDemonstrativos != true) q = q.Where(a => !a.Ficticio);
            if (municipio is not null) q = q.Where(a => a.MunicipioCodigo == municipio);
            var lista = await uow.ListarAsync(q.OrderBy(a => a.Ficticio).ThenBy(a => a.Nome), ct);
            return Results.Ok(new
            {
                fonte = fontes.Obter(NomesFontes.CopRecifeAbrigos),
                observacao = "Constar na base não significa ter vagas. Confirme com a Defesa Civil (199) antes de se deslocar.",
                cobertura = "COP Recife publica somente os abrigos ATIVOS no momento (abertos durante emergências). Demais municípios: sem base aberta identificada.",
                abrigos = lista.Select(a => new
                {
                    a.Id, a.Nome, a.Tipo, a.Endereco, a.MunicipioCodigo, a.Latitude, a.Longitude, acessibilidade = a.Acessibilidade.ToString(),
                    a.Fonte, a.FonteUrl, a.StatusFonte, a.EstagioOperacional, a.AtualizadoNaFonteEm, a.ObtidoEm,
                    disponibilidade = a.DescricaoDisponibilidade, a.Ficticio,
                }),
            });
        });

        api.MapGet("/push/chave-publica", (IEnviadorPush push) =>
            push.Configurado ? Results.Ok(new { chave = push.ChavePublicaVapid }) : Results.Ok(new { chave = (string?)null, motivo = "Web Push não configurado neste ambiente." }));
    }
}
