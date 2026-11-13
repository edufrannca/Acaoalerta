using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Identidade;
using AlertaEmAcao.Modulos.Localizacao;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Modulos.Notificacoes;

/// <summary>
/// Aviso a um familiar com vínculo aprovado sobre um pedido associado à residência do morador.
/// Fica guardado no servidor (o familiar consulta quando abrir o app); push e tempo real são tentativas extras.
/// </summary>
public sealed class NotificacaoFamiliar
{
    public Guid Id { get; private set; }
    public Guid FamiliarId { get; private set; }
    public Guid VinculoId { get; private set; }
    public Guid OcorrenciaId { get; private set; }
    public string Tipo { get; private set; } = default!;
    /// <summary>Chave de deduplicação (única por familiar): o mesmo evento nunca gera duas notificações.</summary>
    public string Chave { get; private set; } = default!;
    public string Titulo { get; private set; } = default!;
    public string Mensagem { get; private set; } = default!;
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? LidaEm { get; private set; }
    public List<EntregaCanal> Entregas { get; private set; } = [];

    private NotificacaoFamiliar() { }

    public static NotificacaoFamiliar Criar(Guid familiarId, Guid vinculoId, Guid ocorrenciaId, string tipo, string chave, string titulo, string mensagem, DateTimeOffset agora) => new()
    {
        Id = Guid.CreateVersion7(), FamiliarId = familiarId, VinculoId = vinculoId, OcorrenciaId = ocorrenciaId,
        Tipo = tipo, Chave = chave, Titulo = titulo, Mensagem = mensagem, CriadaEm = agora,
    };

    public void RegistrarEntrega(string canal, string resultado, DateTimeOffset em, string? detalhe = null) => Entregas.Add(new(canal, resultado, em, detalhe));
    public void MarcarLida(DateTimeOffset agora) => LidaEm ??= agora;
}

public sealed class ServicoNotificacaoFamiliar(IUnidadeTrabalho uow, IEnviadorPush push, INotificadorTempoReal tempoReal, IRelogio relogio,
    ServicoConsentimentos consentimentos, ILogger<ServicoNotificacaoFamiliar> log)
{
    /// <summary>Motivos de atualização da ocorrência que podem interessar ao familiar.</summary>
    public static readonly IReadOnlySet<string> MotivosRelevantes = new HashSet<string>
        { "registrada", "residencia_associada", "complemento", "encaminhamento", "retorno_central", "verificacao", "situacao" };

    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3);

    /// <summary>Descrição do estado que o familiar vê. Nunca afirma que um órgão real recebeu.</summary>
    public static string EstadoParaFamiliar(StatusOcorrencia s) => s switch
    {
        StatusOcorrencia.ConfirmadaParcialmente or StatusOcorrencia.ConfirmadaPorTodas => "A equipe de teste SIMULADA recebeu o pedido (nenhum órgão real foi acionado)",
        StatusOcorrencia.EmAtendimentoSimulado => "Em atendimento simulado",
        StatusOcorrencia.Encerrada => "Pedido encerrado",
        StatusOcorrencia.FalhaNoEncaminhamento => "Pedido registrado; ainda tentando entregar à equipe de teste",
        _ => "Pedido registrado, aguardando a confirmação da equipe",
    };

    public async Task<int> ProcessarAsync(Guid ocorrenciaId, string motivo, CancellationToken ct)
    {
        if (!MotivosRelevantes.Contains(motivo)) return 0;
        var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == ocorrenciaId), ct);
        if (o?.ResidenciaId is not { } residenciaId || o.UsuarioId is not { } moradorId) return 0;
        var res = await uow.PrimeiroAsync(uow.Consulta<LocalMonitorado>().Where(l => l.Id == residenciaId && l.UsuarioId == moradorId), ct);
        if (res is null) return 0;
        // Consentimento D do morador é verificado a cada evento: revogado, ninguém mais é avisado.
        if (!(await consentimentos.AtualAsync(moradorId, ct)).NotificacaoFamiliares) return 0;
        var vinculos = (await uow.ListarAsync(uow.Consulta<VinculoFamiliar>().Where(v => v.ResidenciaId == residenciaId && v.MoradorId == moradorId && v.Status == StatusVinculo.Ativo), ct))
            .Where(v => v.Permissoes.ReceberNotificacoes).ToList();
        if (vinculos.Count == 0) return 0;

        var municipio = MunicipiosRmr.Nome(res.MunicipioCodigo) ?? "município não identificado";
        var hora = o.CriadaNoDispositivoEm.ToOffset(Fuso).ToString("HH:mm");
        (string Tipo, string Chave, string Titulo, Func<VinculoFamiliar, string> Msg)? ev = motivo switch
        {
            "registrada" or "residencia_associada" => ("pedido", $"{o.Id}:pedido", "Pedido de socorro na residência monitorada",
                v => $"A residência \"{v.ApelidoParaFamiliar}\", em {municipio}, registrou um pedido de ajuda às {hora}. O pedido foi registrado. Toque para ver as informações compartilhadas e o estado de recebimento."),
            "complemento" => ("complemento", $"{o.Id}:complemento:{o.Historico.Count(h => h.Tipo == "complemento")}", "Novas informações no pedido",
                v => $"O pedido da residência \"{v.ApelidoParaFamiliar}\" recebeu informações complementares."),
            "situacao" => ("situacao", $"{o.Id}:situacao:{o.Historico.Count(h => h.Tipo == "situacao")}", "Atualização do solicitante",
                v => $"Quem pediu ajuda na residência \"{v.ApelidoParaFamiliar}\" atualizou a situação."),
            _ when o.Status is StatusOcorrencia.ConfirmadaParcialmente or StatusOcorrencia.ConfirmadaPorTodas or StatusOcorrencia.EmAtendimentoSimulado or StatusOcorrencia.Encerrada
                => ("estado", $"{o.Id}:estado:{Grupo(o.Status)}", "Atualização do pedido",
                    v => $"Pedido da residência \"{v.ApelidoParaFamiliar}\": {EstadoParaFamiliar(o.Status)}."),
            _ => null,
        };
        if (ev is null) return 0;

        var agora = relogio.Agora;
        var ids = vinculos.Select(v => v.FamiliarId).ToList();
        var jaExistem = (await uow.ListarAsync(uow.Consulta<NotificacaoFamiliar>().Where(n => ids.Contains(n.FamiliarId) && n.Chave == ev.Value.Chave), ct))
            .Select(n => n.FamiliarId).ToHashSet();
        var novas = vinculos.Where(v => !jaExistem.Contains(v.FamiliarId))
            .Select(v => NotificacaoFamiliar.Criar(v.FamiliarId, v.Id, o.Id, ev.Value.Tipo, ev.Value.Chave, ev.Value.Titulo, ev.Value.Msg(v), agora)).ToList();
        if (novas.Count == 0) return 0;
        foreach (var n in novas) { n.RegistrarEntrega("aplicativo", "disponivel_na_caixa", agora, "Guardada no servidor; aparece em Notificações ao abrir o app."); uow.Adicionar(n); }
        await uow.SalvarAsync(ct);

        foreach (var n in novas) await EntregarAsync(n, ct);
        await uow.SalvarAsync(ct);
        return novas.Count;
    }

    // Confirmações parciais e totais contam como o mesmo marco para o familiar (evita avisos repetidos).
    private static string Grupo(StatusOcorrencia s) => s is StatusOcorrencia.ConfirmadaParcialmente or StatusOcorrencia.ConfirmadaPorTodas ? "confirmada" : s.ToString();

    private async Task EntregarAsync(NotificacaoFamiliar n, CancellationToken ct)
    {
        try
        {
            await tempoReal.NotificacaoUsuarioAsync(n.FamiliarId, new { tipo = "familia", evento = n.Tipo, n.Id, n.Titulo, n.Mensagem, n.OcorrenciaId, url = $"/familia/pedidos/{n.OcorrenciaId}" });
            n.RegistrarEntrega("tempo_real", "publicada", relogio.Agora, "Publicada para conexões abertas (sem confirmação de leitura).");
        }
        catch (Exception e) { n.RegistrarEntrega("tempo_real", "falha", relogio.Agora, e.Message); }

        var u = await uow.PrimeiroAsync(uow.Consulta<Usuario>().Where(x => x.Id == n.FamiliarId), ct);
        if (u?.ReceberPush != true) { n.RegistrarEntrega("web_push", "nao_solicitado", relogio.Agora); return; }
        if (!push.Configurado) { n.RegistrarEntrega("web_push", "nao_configurado", relogio.Agora, "Chaves VAPID ausentes."); return; }
        var assinaturas = await uow.ListarAsync(uow.Consulta<AssinaturaPush>().Where(a => a.UsuarioId == n.FamiliarId && a.Ativa), ct);
        if (assinaturas.Count == 0) { n.RegistrarEntrega("web_push", "sem_assinatura", relogio.Agora); return; }
        foreach (var a in assinaturas)
        {
            try
            {
                var (ok, expirada, detalhe) = await push.EnviarAsync(a, new ConteudoPush(n.Titulo, n.Mensagem, $"/familia/pedidos/{n.OcorrenciaId}", $"familia-{n.Chave}"), ct);
                if (expirada) a.Desativar();
                n.RegistrarEntrega("web_push", ok ? "aceito_pelo_servico_push" : "falha", relogio.Agora, detalhe);
            }
            catch (Exception e)
            {
                // Falha de push nunca apaga o registro interno.
                n.RegistrarEntrega("web_push", "falha", relogio.Agora, e.Message);
                log.LogWarning(e, "Falha no push familiar {Notificacao}", n.Id);
            }
        }
    }
}
