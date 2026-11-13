using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Notificacoes;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Modulos.Alertas;

public sealed record ComandoAvisoDemonstrativo(string Titulo, string Severidade, string? MunicipioCodigo, string? PoligonoGeoJson, int DuracaoMinutos, IReadOnlyList<string>? Instrucoes);

public sealed class ServicoAlertas(IUnidadeTrabalho uow, IFonteAvisosOficiais fonte, IConsultaEspacial espacial, ServicoNotificacoes notificacoes,
    INotificadorTempoReal tempoReal, IRegistroFontes registro, IRelogio relogio, ILogger<ServicoAlertas> log)
{
    /// <summary>Consulta avisos oficiais (INMET), valida vigência e grava os novos (deduplicados pelo código).</summary>
    public async Task<int> SincronizarOficiaisAsync(CancellationToken ct)
    {
        var (avisos, disponivel, erro) = await fonte.ObterAsync(ct);
        registro.Registrar(NomesFontes.InmetAvisos, disponivel, erro, avisos.Count, ParserAvisosInmet.Url, relogio.Agora);
        if (!disponivel) return 0;
        var agora = relogio.Agora;
        var codigos = avisos.Select(a => a.CodigoExterno).ToList();
        var existentes = (await uow.ListarAsync(uow.Consulta<Aviso>().Where(a => codigos.Contains(a.CodigoExterno)).Select(a => a.CodigoExterno), ct)).ToHashSet();
        var novos = 0;
        foreach (var a in avisos.Where(a => !existentes.Contains(a.CodigoExterno) && a.FimEm > agora))
        {
            uow.Adicionar(a);
            novos++;
        }
        if (novos > 0) await uow.SalvarAsync(ct);
        return novos;
    }

    public async Task<Aviso> PublicarDemonstrativoAsync(ComandoAvisoDemonstrativo cmd, CancellationToken ct)
    {
        if (cmd.MunicipioCodigo is null && cmd.PoligonoGeoJson is null)
            throw new ErroDominio("aviso_sem_area", "Informe um município ou um polígono.");
        if (cmd.DuracaoMinutos is < 5 or > 1440) throw new ErroDominio("duracao_invalida", "Duração entre 5 e 1440 minutos.");
        var agora = relogio.Agora;
        var a = Aviso.Criar(OrigemAviso.Demonstrativo, $"demo-{Guid.NewGuid():N}", cmd.Titulo, cmd.Severidade, "#A83232", agora, agora.AddMinutes(cmd.DuracaoMinutos),
            cmd.PoligonoGeoJson, cmd.MunicipioCodigo is null ? [] : [cmd.MunicipioCodigo],
            ["Cenário fictício para demonstração do fluxo de alerta proativo."],
            cmd.Instrucoes ?? ["Em emergência real, ligue 193 (Bombeiros) ou 199 (Defesa Civil)."],
            "Alerta em Ação — gerador de avisos DEMONSTRATIVOS", null, agora);
        uow.Adicionar(a);
        await uow.SalvarAsync(ct);
        log.LogInformation("Aviso demonstrativo {Id} publicado", a.Id);
        return a;
    }

    /// <summary>
    /// Para cada aviso vigente ainda não processado: cruza a área (PostGIS) com locais monitorados de quem consentiu,
    /// cria notificações sem duplicar (único por aviso+usuário) e envia pelos canais disponíveis.
    /// </summary>
    public async Task<int> ProcessarNovosAsync(CancellationToken ct)
    {
        var agora = relogio.Agora;
        var avisos = await uow.ListarAsync(uow.Consulta<Aviso>().Where(a => !a.Processado && a.FimEm >= agora && a.InicioEm <= agora), ct);
        var total = 0;
        foreach (var aviso in avisos)
        {
            var locais = await espacial.LocaisAbrangidosPorAvisoAsync(aviso.Id, ct);
            var porUsuario = locais.GroupBy(l => l.UsuarioId).Select(g => g.First()).ToList();
            var usuariosIds = porUsuario.Select(p => p.UsuarioId).ToList();
            var jaNotificados = (await uow.ListarAsync(uow.Consulta<Notificacao>()
                .Where(n => n.AvisoId == aviso.Id && usuariosIds.Contains(n.UsuarioId)).Select(n => n.UsuarioId), ct)).ToHashSet();
            var criadas = new List<Notificacao>();
            foreach (var (localId, usuarioId, apelido) in porUsuario.Where(p => !jaNotificados.Contains(p.UsuarioId)))
            {
                var n = Notificacao.Criar(usuarioId, aviso.Id, localId, apelido, aviso.Titulo, aviso.Oficial, agora);
                uow.Adicionar(n);
                criadas.Add(n);
            }
            aviso.MarcarProcessado(agora);
            await uow.SalvarAsync(ct);
            foreach (var n in criadas) await notificacoes.EntregarAsync(n, aviso, ct);
            await uow.SalvarAsync(ct);
            await tempoReal.AvisoPublicadoAsync(aviso.Id, aviso.Oficial, aviso.MunicipiosCodigos);
            log.LogInformation("Aviso {Id} ({Origem}) processado: {N} notificação(ões)", aviso.Id, aviso.Origem, criadas.Count);
            total += criadas.Count;
        }
        return total;
    }
}

public sealed class ServicoNotificacoes(IUnidadeTrabalho uow, IEnviadorPush push, INotificadorTempoReal tempoReal, IRelogio relogio, ILogger<ServicoNotificacoes> log)
{
    public async Task EntregarAsync(Notificacao n, Aviso aviso, CancellationToken ct)
    {
        var agora = relogio.Agora;
        n.RegistrarEntrega("aplicativo", "disponivel_na_caixa", agora, "Visível em Notificações ao abrir o aplicativo.");
        try
        {
            await tempoReal.NotificacaoUsuarioAsync(n.UsuarioId, new { n.Id, n.Titulo, n.Mensagem, n.Pergunta, n.AvisoOficial, AvisoId = aviso.Id });
            n.RegistrarEntrega("tempo_real", "publicada", relogio.Agora, "Publicada para conexões abertas (sem confirmação de leitura).");
        }
        catch (Exception e) { n.RegistrarEntrega("tempo_real", "falha", relogio.Agora, e.Message); }

        var usuario = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(u => u.Id == n.UsuarioId), ct);
        if (usuario?.ReceberPush != true) { n.RegistrarEntrega("web_push", "nao_solicitado", relogio.Agora); return; }
        if (!push.Configurado) { n.RegistrarEntrega("web_push", "nao_configurado", relogio.Agora, "Chaves VAPID ausentes."); return; }
        var assinaturas = await uow.ListarAsync(uow.Consulta<AssinaturaPush>().Where(a => a.UsuarioId == n.UsuarioId && a.Ativa), ct);
        if (assinaturas.Count == 0) { n.RegistrarEntrega("web_push", "sem_assinatura", relogio.Agora); return; }
        foreach (var a in assinaturas)
        {
            var (ok, expirada, detalhe) = await push.EnviarAsync(a, new ConteudoPush(n.Titulo, $"{n.Mensagem} {n.Pergunta}", $"/?notificacao={n.Id}", $"aviso-{aviso.Id}"), ct);
            if (expirada) a.Desativar();
            // "aceito_pelo_servico_push" ≠ entregue no aparelho: o serviço de push não confirma exibição.
            n.RegistrarEntrega("web_push", ok ? "aceito_pelo_servico_push" : "falha", relogio.Agora, detalhe);
            if (!ok) log.LogWarning("Falha no push para {Usuario}: {Detalhe}", n.UsuarioId, detalhe);
        }
    }

    public async Task<Notificacao> ResponderAsync(Guid id, Guid usuarioId, RespostaNotificacao resposta, CancellationToken ct)
    {
        var n = await uow.PrimeiroAsync(uow.Consulta<Notificacao>().Where(x => x.Id == id && x.UsuarioId == usuarioId), ct)
                ?? throw new ErroNaoEncontrado("Notificação não encontrada.");
        n.Responder(resposta, relogio.Agora);
        await uow.SalvarAsync(ct);
        return n;
    }
}
