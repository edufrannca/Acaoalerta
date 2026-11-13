using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Municipios;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Modulos.Notificacoes;

/// <summary>Último nível avaliado por local monitorado (para avisar só quando AGRAVA, nunca repetir).</summary>
public sealed class EstadoIndicadorLocal
{
    public Guid LocalId { get; private set; }
    public NivelAtencaoAlagamento Nivel { get; private set; }
    public Guid Episodio { get; private set; }
    public DateTimeOffset AvaliadoEm { get; private set; }

    private EstadoIndicadorLocal() { }
    public static EstadoIndicadorLocal Novo(Guid localId, DateTimeOffset agora) => new() { LocalId = localId, Nivel = NivelAtencaoAlagamento.SemEvidenciaSuficiente, Episodio = Guid.CreateVersion7(), AvaliadoEm = agora };

    /// <summary>Retorna true se o nível subiu para Elevado/Crítico (agravamento que merece aviso).</summary>
    public bool Atualizar(NivelAtencaoAlagamento novo, DateTimeOffset agora)
    {
        var agravou = novo >= NivelAtencaoAlagamento.Elevado && novo > Nivel;
        // Ao voltar abaixo de Elevado, um novo episódio pode gerar novo aviso depois.
        if (novo < NivelAtencaoAlagamento.Elevado && Nivel >= NivelAtencaoAlagamento.Elevado) Episodio = Guid.CreateVersion7();
        Nivel = novo;
        AvaliadoEm = agora;
        return agravou;
    }
}

/// <summary>Aviso preventivo guardado no servidor (o usuário consulta ao abrir o app); tempo real e push são tentativas extras.</summary>
public sealed class NotificacaoPreventiva
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid LocalId { get; private set; }
    public NivelAtencaoAlagamento Nivel { get; private set; }
    public string Chave { get; private set; } = default!;
    public string Titulo { get; private set; } = default!;
    public string Mensagem { get; private set; } = default!;
    public string? Url { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? LidaEm { get; private set; }
    public List<EntregaCanal> Entregas { get; private set; } = [];

    private NotificacaoPreventiva() { }
    public static NotificacaoPreventiva Criar(Guid usuarioId, Guid localId, NivelAtencaoAlagamento nivel, string chave, string titulo, string mensagem, string? url, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(), UsuarioId = usuarioId, LocalId = localId, Nivel = nivel, Chave = chave, Titulo = titulo, Mensagem = mensagem, Url = url, CriadaEm = agora,
    };
    public void RegistrarEntrega(string canal, string resultado, DateTimeOffset em, string? detalhe = null) => Entregas.Add(new(canal, resultado, em, detalhe));
    public void MarcarLida(DateTimeOffset agora) => LidaEm ??= agora;
}

/// <summary>
/// Avalia os locais monitorados após novas leituras e avisa quem está autorizado: o dono (consentimento A) e,
/// se for a residência onde ele mora, familiares com vínculo aprovado (morador com consentimento D; familiar com A).
/// Nunca dispara SOS nem afirma que alguém está ilhado.
/// </summary>
public sealed class ServicoAlertaPreventivo(IUnidadeTrabalho uow, ServicoIndicadorAlagamento indicador, ServicoConsentimentos consentimentos,
    IEnviadorPush push, INotificadorTempoReal tempoReal, IFonteChuva fonteChuva, IRelogio relogio, ILogger<ServicoAlertaPreventivo> log)
{
    private static DateTimeOffset? _ultimaLeituraDivulgada;

    public async Task<int> ProcessarAsync(CancellationToken ct)
    {
        var agora = relogio.Agora;
        var chuva = await fonteChuva.ObterRmrAsync(ct);
        if (chuva.Disponivel && chuva.ConsultadoEm != _ultimaLeituraDivulgada)
        {
            _ultimaLeituraDivulgada = chuva.ConsultadoEm;
            await tempoReal.CondicoesAtualizadasAsync(new { consultadoEm = chuva.ConsultadoEm, fonte = chuva.Fonte });
        }

        var donos = await uow.ListarAsync(uow.Consulta<Usuario>().Where(u => u.ConsenteAlertas).Select(u => u.Id), ct);
        var locais = await uow.ListarAsync(uow.Consulta<LocalMonitorado>().Where(l => l.Ativo && donos.Contains(l.UsuarioId)), ct);
        var ids = locais.Select(l => l.Id).ToList();
        var estados = (await uow.ListarAsync(uow.Consulta<EstadoIndicadorLocal>().Where(e => ids.Contains(e.LocalId)), ct)).ToDictionary(e => e.LocalId);
        var enviadas = 0;
        var porCelula = new Dictionary<(double, double), IndicadorAlagamento>();
        foreach (var l in locais)
        {
            // Locais vizinhos (~110 m) compartilham a avaliação: menos consultas.
            var celula = (Math.Round(l.Latitude, 3), Math.Round(l.Longitude, 3));
            if (!porCelula.TryGetValue(celula, out var ind)) porCelula[celula] = ind = await indicador.AvaliarAsync(l.Ponto, MunicipiosRmr.Nome(l.MunicipioCodigo), ct);
            if (!estados.TryGetValue(l.Id, out var est)) { est = EstadoIndicadorLocal.Novo(l.Id, agora); uow.Adicionar(est); estados[l.Id] = est; }
            if (!est.Atualizar(ind.Nivel, agora)) continue;
            enviadas += await AvisarAsync(l, ind, est.Episodio, ct);
        }
        await uow.SalvarAsync(ct);
        return enviadas;
    }

    private async Task<int> AvisarAsync(LocalMonitorado l, IndicadorAlagamento ind, Guid episodio, CancellationToken ct)
    {
        var agora = relogio.Agora;
        var municipio = MunicipiosRmr.Nome(l.MunicipioCodigo) ?? "região monitorada";
        var destino = new List<(Guid Usuario, string Nome, string? Url)> { (l.UsuarioId, l.Apelido, $"/locais/{l.Id}") };
        if (l.MoroAqui && (await consentimentos.AtualAsync(l.UsuarioId, ct)).NotificacaoFamiliares)
        {
            var vs = (await uow.ListarAsync(uow.Consulta<VinculoFamiliar>().Where(v => v.ResidenciaId == l.Id && v.Status == StatusVinculo.Ativo), ct))
                .Where(v => v.Permissoes.ReceberNotificacoes).ToList();
            var fids = vs.Select(v => v.FamiliarId).ToList();
            var comA = (await uow.ListarAsync(uow.Consulta<Usuario>().Where(u => fids.Contains(u.Id) && u.ConsenteAlertas).Select(u => u.Id), ct)).ToHashSet();
            destino.AddRange(vs.Where(v => comA.Contains(v.FamiliarId)).Select(v => (v.FamiliarId, v.ApelidoParaFamiliar, (string?)"/perfil")));
        }
        var chave = $"{l.Id}:{episodio}:{ind.Nivel}";
        var uids = destino.Select(d => d.Usuario).ToList();
        var ja = (await uow.ListarAsync(uow.Consulta<NotificacaoPreventiva>().Where(n => uids.Contains(n.UsuarioId) && n.Chave == chave), ct)).Select(n => n.UsuarioId).ToHashSet();
        var novas = new List<NotificacaoPreventiva>();
        foreach (var (u, nome, url) in destino.Where(d => !ja.Contains(d.Usuario)).DistinctBy(d => d.Usuario))
        {
            var titulo = l.Tipo == TipoLocal.Residencia ? "Atenção à região da residência monitorada" : "Atenção à região do local monitorado";
            var msg = $"{ind.Rotulo} em \"{nome}\" ({municipio}): {ind.Justificativa.FirstOrDefault()} " +
                      "Consulte os dados atualizados e evite deslocamentos por áreas inundadas. Isto não confirma alagamento.";
            var n = NotificacaoPreventiva.Criar(u, l.Id, ind.Nivel, chave, titulo, msg, url, agora);
            n.RegistrarEntrega("aplicativo", "disponivel_na_caixa", agora);
            uow.Adicionar(n);
            novas.Add(n);
        }
        if (novas.Count == 0) return 0;
        await uow.SalvarAsync(ct);
        foreach (var n in novas)
        {
            try { await tempoReal.NotificacaoUsuarioAsync(n.UsuarioId, new { tipo = "preventivo", n.Id, n.Titulo, n.Mensagem, nivel = n.Nivel.ToString(), url = n.Url }); n.RegistrarEntrega("tempo_real", "publicada", agora); }
            catch (Exception e) { n.RegistrarEntrega("tempo_real", "falha", agora, e.Message); }
            var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Id == n.UsuarioId), ct);
            if (u?.ReceberPush != true || !push.Configurado) { n.RegistrarEntrega("web_push", u?.ReceberPush != true ? "nao_solicitado" : "nao_configurado", agora); continue; }
            foreach (var a in await uow.ListarAsync(uow.Consulta<AssinaturaPush>().Where(x => x.UsuarioId == n.UsuarioId && x.Ativa), ct))
            {
                try
                {
                    var (ok, expirada, det) = await push.EnviarAsync(a, new ConteudoPush(n.Titulo, n.Mensagem, n.Url ?? "/", $"preventivo-{n.Chave}"), ct);
                    if (expirada) a.Desativar();
                    n.RegistrarEntrega("web_push", ok ? "aceito_pelo_servico_push" : "falha", agora, det);
                }
                catch (Exception e) { n.RegistrarEntrega("web_push", "falha", agora, e.Message); log.LogWarning(e, "Push preventivo falhou"); }
            }
        }
        return novas.Count;
    }
}
