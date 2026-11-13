using System.Security.Claims;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Notificacoes;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Api.Endpoints;

public sealed record ConviteUsoRequest(string Codigo, string Apelido);
public sealed record DecisaoVinculoRequest(bool Aprovar, PermissoesVinculo? Permissoes);
public sealed record ConsentimentoRequest(FinalidadeConsentimento Finalidade, bool Concedido);
public sealed record AssociarResidenciaRequest(bool Associar);

/// <summary>Locais (CEP, endereço, mapa), moradores, vínculos familiares, consentimentos e visão do familiar autorizado.</summary>
public static class ResidenciasEndpoints
{
    public static ServicoIdentidade.DadosLocal Dados(this LocalRequest r) =>
        new(r.Apelido, r.Latitude, r.Longitude, r.Tipo, r.Origem, r.Precisao, r.Endereco, r.MoroAqui, r.Moradores);

    /// <summary>Visão do DONO do local (inclui endereço e moradores, que ficam cifrados em repouso).</summary>
    public static object VisaoLocal(LocalMonitorado l) => new
    {
        l.Id, l.Apelido, tipo = l.Tipo.ToString(), l.MoroAqui, l.Latitude, l.Longitude, l.MunicipioCodigo, municipio = MunicipiosRmr.Nome(l.MunicipioCodigo),
        origem = l.Origem.ToString(), precisao = l.DescricaoPrecisao, endereco = l.Endereco, moradores = l.Moradores, l.CriadoEm, l.AtualizadoEm,
    };

    public static void MapResidencias(this WebApplication app)
    {
        // ---------- CEP e endereço: só para quem está cadastrando locais (conta), com limite por IP ----------
        var geo = app.MapGroup("/api/geo").WithTags("Geocodificação").RequireAuthorization().RequireRateLimiting("geocodificacao");

        geo.MapGet("/cep/{cep}", async (string cep, IGeocodificador g, ICatalogoMunicipios catalogo, CancellationToken ct) =>
        {
            var digitos = EnderecoLocal.SoDigitos(cep);
            if (digitos.Length != 8) return Results.BadRequest(new { codigo = "cep_invalido", erro = "O CEP deve ter 8 dígitos." });
            EnderecoCep? e;
            try { e = await g.BuscarCepAsync(digitos, ct); }
            catch (HttpRequestException) { return Results.Json(new { codigo = "cep_indisponivel", erro = "Consulta de CEP indisponível agora. Pesquise pelo endereço ou escolha no mapa." }, statusCode: 503); }
            if (e is null) return Results.NotFound(new { codigo = "cep_inexistente", erro = "CEP não encontrado. Confira os números ou escolha o local no mapa." });
            var municipio = MunicipiosRmr.Todos.FirstOrDefault(m => Normalizar(m.Nome) == Normalizar(e.Municipio));
            IReadOnlyList<ResultadoGeocodificacao> posicoes = [];
            string? avisoGeo = null;
            if (e.Uf == "PE" && municipio.Codigo is not null)
            {
                try { posicoes = await g.BuscarEnderecoAsync(e.Logradouro, null, e.Bairro, e.Municipio, e.Uf, e.Cep, ct); }
                catch (HttpRequestException ex) { avisoGeo = ex.Message; }
                posicoes = posicoes.Where(r => catalogo.Identificar(new PontoGeo(r.Latitude, r.Longitude))?.Codigo == municipio.Codigo).ToList();
            }
            return Results.Ok(new
            {
                endereco = e,
                municipioCodigo = municipio.Codigo,
                dentroRmr = e.Uf == "PE" && municipio.Codigo is not null,
                posicoes,
                // ViaCEP não traz coordenadas: sem correspondência confiável, a pessoa escolhe no mapa.
                exigeEscolhaNoMapa = posicoes.Count == 0 || posicoes.All(p => p.ExigeConfirmacaoNoMapa),
                aviso = avisoGeo ?? (posicoes.Count == 0 ? "O CEP identifica o logradouro, mas não encontramos o ponto no mapa. Escolha a posição tocando no mapa." : null),
                fontes = new[] { "ViaCEP (endereço)", "OpenStreetMap/Nominatim (posição) — © colaboradores do OpenStreetMap, ODbL" },
            });
        });

        geo.MapGet("/endereco", async (string? logradouro, string? numero, string? bairro, string? municipio, string? uf, string? cep,
            IGeocodificador g, ICatalogoMunicipios catalogo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(municipio) || string.IsNullOrWhiteSpace(logradouro) && string.IsNullOrWhiteSpace(bairro))
                return Results.BadRequest(new { codigo = "endereco_incompleto", erro = "Informe ao menos a rua (ou o bairro) e o município." });
            IReadOnlyList<ResultadoGeocodificacao> r;
            try
            {
                r = await g.BuscarEnderecoAsync(logradouro, numero, bairro, municipio.Trim(), (uf ?? "PE").Trim(),
                    cep is null ? null : EnderecoLocal.SoDigitos(cep) is { Length: 8 } c ? c : null, ct);
            }
            catch (HttpRequestException ex) { return Results.Json(new { codigo = "geocodificacao_indisponivel", erro = ex.Message }, statusCode: 503); }
            var resultados = r.Select(x => new { x.Latitude, x.Longitude, x.Descricao, x.Precisao, x.ExigeConfirmacaoNoMapa,
                municipio = catalogo.Identificar(new PontoGeo(x.Latitude, x.Longitude)) }).Where(x => x.municipio is not null).ToList();
            return Results.Ok(new
            {
                resultados,
                ambiguo = resultados.Count > 1,
                aviso = resultados.Count == 0 ? "Nenhum ponto encontrado na RMR para esse endereço. Escolha a posição no mapa." : resultados.Count > 1 ? "Mais de um lugar corresponde. Escolha o certo e confirme no mapa." : null,
                fonte = "OpenStreetMap/Nominatim — © colaboradores do OpenStreetMap, ODbL",
            });
        });

        // Ponto escolhido no mapa: município pelos limites oficiais (sem serviço externo).
        geo.MapGet("/ponto", (double lat, double lon, ICatalogoMunicipios catalogo) =>
            PontoGeo.TentarCriar(lat, lon, out var p)
                ? Results.Ok(new { latitude = lat, longitude = lon, municipio = catalogo.Identificar(p), dentroRmr = catalogo.Identificar(p) is not null })
                : Results.BadRequest(new { codigo = "coordenadas_invalidas", erro = "Coordenadas inválidas." }));

        var eu = app.MapGroup("/api/eu").WithTags("Residências e vínculos").RequireAuthorization();

        // ---------- Convites e vínculos (morador) ----------
        eu.MapPost("/locais/{id:guid}/convites", async (Guid id, ClaimsPrincipal p, ServicoVinculos s, CancellationToken ct) =>
        {
            var (c, codigo) = await s.GerarConviteAsync(id, p.Id()!.Value, ct);
            // O código aparece só agora; o servidor guarda apenas o hash.
            return Results.Ok(new { codigo, c.ExpiraEm, instrucao = "Envie este código ao familiar. Ele digita em Perfil → Acompanhar uma residência, e você aprova o pedido aqui." });
        }).RequireRateLimiting("convite");

        eu.MapGet("/locais/{id:guid}/vinculos", async (Guid id, ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var vs = await uow.ListarAsync(uow.Consulta<VinculoFamiliar>().Where(v => v.ResidenciaId == id && v.MoradorId == uid).OrderByDescending(v => v.CriadoEm), ct);
            var ids = vs.Select(v => v.FamiliarId).ToList();
            var nomes = (await uow.ListarAsync(uow.Consulta<Usuario>().Where(u => ids.Contains(u.Id)), ct)).ToDictionary(u => u.Id, u => u.Nome);
            var agora = DateTimeOffset.UtcNow;
            var convites = await uow.ContarAsync(uow.Consulta<ConviteVinculo>().Where(c => c.ResidenciaId == id && c.MoradorId == uid && c.UsadoEm == null && !c.Cancelado && c.ExpiraEm > agora), ct);
            return Results.Ok(new
            {
                convitesEmAberto = convites,
                vinculos = vs.Select(v => new { v.Id, familiar = nomes.GetValueOrDefault(v.FamiliarId), status = v.Status.ToString(), v.Permissoes, v.CriadoEm, v.DecididoEm, v.RevogadoEm }),
            });
        });

        eu.MapPost("/vinculos/{id:guid}/decisao", async (Guid id, DecisaoVinculoRequest r, ClaimsPrincipal p, ServicoVinculos s, CancellationToken ct) =>
        {
            var v = await s.DecidirAsync(id, p.Id()!.Value, r.Aprovar, r.Permissoes, ct);
            return Results.Ok(new { v.Id, status = v.Status.ToString(), v.Permissoes });
        });

        eu.MapPut("/vinculos/{id:guid}/permissoes", async (Guid id, PermissoesVinculo r, ClaimsPrincipal p, ServicoVinculos s, CancellationToken ct) =>
        {
            var v = await s.AlterarPermissoesAsync(id, p.Id()!.Value, r, ct);
            return Results.Ok(new { v.Id, status = v.Status.ToString(), v.Permissoes });
        });

        eu.MapDelete("/vinculos/{id:guid}", async (Guid id, ClaimsPrincipal p, ServicoVinculos s, CancellationToken ct) =>
        {
            await s.RevogarAsync(id, p.Id()!.Value, ct);
            return Results.NoContent();
        });

        // ---------- Familiar ----------
        eu.MapPost("/acompanhar", async (ConviteUsoRequest r, ClaimsPrincipal p, ServicoVinculos s, CancellationToken ct) =>
        {
            var v = await s.UsarConviteAsync(r.Codigo, p.Id()!.Value, r.Apelido, ct);
            return Results.Ok(new { v.Id, status = v.Status.ToString(), v.ApelidoParaFamiliar, mensagem = "Pedido enviado. O morador precisa aprovar antes de você receber qualquer informação." });
        }).RequireRateLimiting("convite");

        eu.MapGet("/acompanho", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var vs = await uow.ListarAsync(uow.Consulta<VinculoFamiliar>().Where(v => v.FamiliarId == uid && v.Status != StatusVinculo.Recusado).OrderByDescending(v => v.CriadoEm), ct);
            var resIds = vs.Select(v => v.ResidenciaId).ToList();
            var res = (await uow.ListarAsync(uow.Consulta<LocalMonitorado>().Where(l => resIds.Contains(l.Id)), ct)).ToDictionary(l => l.Id);
            var vinIds = vs.Select(v => v.Id).ToList();
            var ultimos = (await uow.ListarAsync(uow.Consulta<NotificacaoFamiliar>().Where(n => n.FamiliarId == uid && vinIds.Contains(n.VinculoId)).OrderByDescending(n => n.CriadaEm), ct))
                .GroupBy(n => n.VinculoId).ToDictionary(g => g.Key, g => (object)new { g.First().OcorrenciaId, g.First().Titulo, g.First().CriadaEm });
            return Results.Ok(vs.Select(v => new
            {
                v.Id, v.ApelidoParaFamiliar, status = v.Status.ToString(), v.Permissoes, v.CriadoEm,
                // Antes da aprovação, nada da residência é revelado.
                municipio = v.Ativo && res.TryGetValue(v.ResidenciaId, out var l) ? MunicipiosRmr.Nome(l.MunicipioCodigo) : null,
                // Vínculo aprovado pelo morador: o familiar vê onde fica a residência e o último pedido de socorro dela.
                latitude = v.Ativo && res.TryGetValue(v.ResidenciaId, out var l2) ? l2.Latitude : (double?)null,
                longitude = v.Ativo && res.TryGetValue(v.ResidenciaId, out var l3) ? l3.Longitude : (double?)null,
                bairro = v.Ativo && res.TryGetValue(v.ResidenciaId, out var l4) ? l4.Endereco.Bairro : null,
                ultimoPedido = v.Ativo ? ultimos.GetValueOrDefault(v.Id) : null,
            }));
        });

        eu.MapGet("/notificacoes-familia", async (ClaimsPrincipal p, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var ns = await uow.ListarAsync(uow.Consulta<NotificacaoFamiliar>().Where(n => n.FamiliarId == uid).OrderByDescending(n => n.CriadaEm).Take(100), ct);
            return Results.Ok(ns.Select(n => new { n.Id, n.Tipo, n.Titulo, n.Mensagem, n.OcorrenciaId, n.CriadaEm, n.LidaEm, n.Entregas }));
        });

        eu.MapPost("/notificacoes-familia/{id:guid}/lida", async (Guid id, ClaimsPrincipal p, IUnidadeTrabalho uow, IRelogio relogio, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var n = await uow.PrimeiroAsync(uow.Consulta<NotificacaoFamiliar>().Where(x => x.Id == id && x.FamiliarId == uid), ct) ?? throw new ErroNaoEncontrado("Notificação não encontrada.");
            n.MarcarLida(relogio.Agora);
            await uow.SalvarAsync(ct);
            return Results.NoContent();
        });

        // Pedido visto pelo familiar autorizado: só o que o vínculo permite, e só enquanto vínculo e consentimento D valem.
        eu.MapGet("/familia/pedidos/{id:guid}", async (Guid id, ClaimsPrincipal p, IUnidadeTrabalho uow, ServicoConsentimentos sc, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == id), ct);
            var v = o?.ResidenciaId is { } rid
                ? await uow.PrimeiroAsync(uow.Consulta<VinculoFamiliar>().Where(x => x.ResidenciaId == rid && x.FamiliarId == uid && x.Status == StatusVinculo.Ativo && x.MoradorId == o.UsuarioId), ct)
                : null;
            // Mesma resposta para "não existe" e "sem vínculo": não revela pedidos de outras famílias.
            if (o is null || v is null || o.UsuarioId is not { } morador || !(await sc.AtualAsync(morador, ct)).NotificacaoFamiliares)
                throw new ErroNaoEncontrado("Pedido não encontrado ou sem autorização para você.");
            var res = await uow.PrimeiroAsync(uow.Consulta<LocalMonitorado>().Where(l => l.Id == o.ResidenciaId), ct);
            var cp = o.Complemento;
            var nivel = o.Status is StatusOcorrencia.ConfirmadaParcialmente or StatusOcorrencia.ConfirmadaPorTodas or StatusOcorrencia.EmAtendimentoSimulado ? 5 : 4;
            return Results.Ok(new
            {
                o.Id,
                referencia = o.Id.ToString("N")[..6].ToUpperInvariant(),
                residencia = v.ApelidoParaFamiliar,
                municipio = MunicipiosRmr.Nome(o.MunicipioCodigo ?? res?.MunicipioCodigo),
                criadoEm = o.CriadaNoDispositivoEm,
                recebidoPeloServidorEm = o.RecebidaNoServidorEm,
                estado = ServicoNotificacaoFamiliar.EstadoParaFamiliar(o.Status),
                nivelComprovado = nivel,
                situacao = ResumoComunitario.TiposEmergencia(cp),
                necessidades = v.Permissoes.VerNecessidades ? (cp.Necessidades ?? []).Select(n => NecessidadesDeclaradas.Rotulos[n]) : null,
                pessoasNoLocal = v.Permissoes.VerMoradores ? cp.QuantidadePessoas ?? cp.Pessoas?.Count : null,
                moradoresCadastrados = v.Permissoes.VerMoradores && res is not null ? new { res.Moradores.Total, res.Moradores.Criancas, res.Moradores.Idosos, res.Moradores.MobilidadeReduzida, res.Moradores.Animais } : null,
                posicao = v.Permissoes.VerPosicaoDoPedido && o.Latitude is not null ? new { o.Latitude, o.Longitude, o.PrecisaoMetros, lidaEm = o.LocalizacaoLidaEm } : null,
                historico = o.Historico.Where(h => h.Tipo is "registro" or "encaminhamento" or "retorno_central" or "complemento" or "status" or "residencia" or "situacao")
                    .Select(h => new { h.Em, h.Tipo }).TakeLast(20),
                permissoes = v.Permissoes,
                aviso = "Centrais SIMULADAS: nenhum órgão real foi acionado. Em risco imediato, ligue 193 (Bombeiros) ou 199 (Defesa Civil).",
            });
        });

        // ---------- Consentimentos ----------
        eu.MapGet("/consentimentos", async (ClaimsPrincipal p, ServicoConsentimentos sc, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var estado = await sc.AtualAsync(uid, ct);
            return Results.Ok(new
            {
                versao = TextosConsentimento.Versao,
                finalidades = Enum.GetValues<FinalidadeConsentimento>().Select(f => new { finalidade = f.ToString(), texto = TextosConsentimento.Textos[f], concedido = estado.Tem(f) }),
                historico = (await sc.HistoricoAsync(uid, ct)).Select(r => new { finalidade = r.Finalidade.ToString(), r.Concedido, r.VersaoTexto, r.Origem, r.Em }),
                nota = TextosConsentimento.NotaProtecaoDaVida,
            });
        });

        eu.MapPut("/consentimentos", async (ConsentimentoRequest r, ClaimsPrincipal p, ServicoConsentimentos sc, ServicoOcorrencias so, CancellationToken ct) =>
        {
            var uid = p.Id()!.Value;
            var mudou = await sc.DefinirAsync(uid, r.Finalidade, r.Concedido, "perfil", ct);
            var retirados = 0;
            // Revogar C retira da comunidade os pedidos ativos que dependiam da preferência (escolhas feitas no pedido prevalecem).
            if (mudou && !r.Concedido && r.Finalidade == FinalidadeConsentimento.CompartilhamentoComunitario)
                retirados = await so.RetirarCompartilhamentoPorPreferenciaAsync(uid, ct);
            return Results.Ok(new { finalidade = r.Finalidade.ToString(), r.Concedido, registrado = mudou, pedidosRetiradosDaComunidade = retirados });
        });

        // ---------- Pedido ↔ residência (escolha explícita do solicitante cadastrado) ----------
        app.MapGet("/api/ocorrencias/{id:guid}/residencia", async (Guid id, HttpContext ctx, ClaimsPrincipal p, ServicoOcorrencias s, IUnidadeTrabalho uow, CancellationToken ct) =>
        {
            var o = await s.ObterDoSolicitanteAsync(id, OcorrenciasEndpoints.Dispositivo(ctx), p.Id(), ct);
            var res = o.ResidenciaId is { } rid ? await uow.PrimeiroAsync(uow.Consulta<LocalMonitorado>().Where(l => l.Id == rid), ct) : null;
            var familiares = res is null ? 0 : await uow.ContarAsync(uow.Consulta<VinculoFamiliar>().Where(v => v.ResidenciaId == res.Id && v.Status == StatusVinculo.Ativo), ct);
            return Results.Ok(new { associada = res is not null, apelido = res?.Apelido, familiaresVinculados = familiares });
        }).RequireAuthorization();

        app.MapPost("/api/ocorrencias/{id:guid}/residencia", async (Guid id, AssociarResidenciaRequest r, HttpContext ctx, ClaimsPrincipal p, ServicoOcorrencias s, CancellationToken ct) =>
        {
            var o = await s.AssociarResidenciaAsync(id, OcorrenciasEndpoints.Dispositivo(ctx), p.Id()!.Value, r.Associar, ct);
            return Results.Ok(new { associada = o.ResidenciaId is not null });
        }).RequireAuthorization().RequireRateLimiting("socorro");
    }

    private static string Normalizar(string s) => VozLocalidadeEndpoints.Normalizar(s);
}
