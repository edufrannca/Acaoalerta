using AlertaEmAcao.Contratos;
using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.ApoioComunitario;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Municipios;
using AlertaEmAcao.Modulos.Ocorrencias;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Modulos.Encaminhamento;

public static class ConstrutorMensagens
{
    public static MensagemCentralV1 Ocorrencia(Ocorrencia o, AnaliseOcorrencia? a, Guid correlacao, TipoMensagem tipo)
    {
        var c = o.Complemento;
        var indicios = DetectorIndicios.Detectar(c).Select(i => i.Tipo).ToHashSet();
        var riscos = new List<string>();
        if (indicios.Contains(Indicio.RiscoHidrologico)) riscos.Add("hidrologico");
        if (indicios.Contains(Indicio.RiscoGeologico)) riscos.Add("geologico");
        if (indicios.Contains(Indicio.PessoaFerida)) riscos.Add("pessoa_ferida");
        if (indicios.Contains(Indicio.SegurancaPublica)) riscos.Add("seguranca_publica");
        if (riscos.Count == 0) riscos.Add("nao_especificado");
        var necessidades = new List<string>();
        if (indicios.Contains(Indicio.NecessidadeMedica)) necessidades.Add("atendimento_ou_equipamento_medico");
        if (indicios.Contains(Indicio.Vulnerabilidade)) necessidades.Add("assistencia_na_retirada");
        if (indicios.Contains(Indicio.PessoaIsolada)) necessidades.Add("pessoa_isolada");
        if (indicios.Contains(Indicio.AcessoBloqueado)) necessidades.Add("rota_alternativa");
        var extras = new Dictionary<string, string>();
        void Flag(string k, bool? v) { if (v is not null) extras[k] = v.Value ? "sim" : "nao"; }
        Flag("ilhado", c.Ilhado); Flag("aguaSubindo", c.AguaSubindo); Flag("correnteza", c.Correnteza);
        Flag("riscoDeslizamento", c.RiscoDeslizamento); Flag("desmoronamento", c.Desmoronamento);
        Flag("barreiraInstavel", c.BarreiraComSinaisInstabilidade); Flag("acessoBloqueado", c.AcessoBloqueado);
        if (c.OutrosRiscos is not null) extras["outrosRiscos"] = c.OutrosRiscos;

        return new MensagemCentralV1
        {
            Versao = VersoesContrato.MensagemCentral,
            TipoMensagem = tipo == TipoMensagem.AtualizacaoOcorrencia ? "atualizacao_ocorrencia" : "ocorrencia",
            IdentificadorOcorrencia = o.Id,
            IdentificadorCorrelacao = correlacao,
            MunicipioCodigo = o.MunicipioCodigo ?? "",
            MunicipioNome = MunicipiosRmr.Nome(o.MunicipioCodigo) ?? "",
            Latitude = o.Latitude, Longitude = o.Longitude, PrecisaoMetros = o.PrecisaoMetros,
            HorarioOcorrencia = o.CriadaNoDispositivoEm, HorarioLocalizacao = o.LocalizacaoLidaEm,
            TiposRisco = riscos,
            PessoasEnvolvidas = new PessoasEnvolvidasV1(c.QuantidadePessoas ?? c.Pessoas?.Count, c.IdadeSolicitante,
                c.Pessoas?.Select(p => p.Idade).ToList() ?? [], c.PessoasFeridas, c.Criancas, c.Idosos, c.MobilidadeReduzida,
                c.EquipamentoMedicoEssencial, c.Sozinho),
            Necessidades = necessidades,
            Relato = c.Descricao,
            ReferenciaLocal = c.ReferenciaLocal,
            InformacoesComplementares = extras,
            Sinalizacoes = a?.Sinalizacoes.Select(s => new SinalizacaoV1(s.Codigo, s.Nivel.ToString(), s.Titulo, s.Explicacao)).ToList() ?? [],
            CanalOrigem = o.Canal.ToString(),
            Ambiente = VersoesContrato.AmbienteDemonstracao,
        };
    }

    public static MensagemOfertaApoioV1 Oferta(OfertaAjuda o, NecessidadePublicada n, Guid correlacao) => new()
    {
        Versao = VersoesContrato.MensagemCentral, TipoMensagem = "oferta_apoio", IdentificadorOferta = o.Id, IdentificadorNecessidade = n.Id,
        IdentificadorCorrelacao = correlacao, MunicipioCodigo = o.MunicipioCodigo, TipoAjuda = o.Tipo.ToString(), Descricao = o.Descricao,
        Disponibilidade = o.Disponibilidade, RegiaoDisponibilidade = o.RegiaoDisponibilidade, ContatoAutorizado = o.ContatoAutorizado,
        CriadaEm = o.CriadaEm, Ambiente = VersoesContrato.AmbienteDemonstracao,
    };
}

/// <summary>
/// Transmite encaminhamentos pendentes por HTTP às centrais e registra confirmações individuais.
/// Cada destino tem falha tratada de forma independente; idempotência pela chave = Id do encaminhamento.
/// </summary>
public sealed class ServicoDespacho(IUnidadeTrabalho uow, IClienteCentral cliente, INotificadorTempoReal tempoReal, IRelogio relogio, ILogger<ServicoDespacho> log)
{
    public async Task<int> ProcessarPendentesAsync(int limite, CancellationToken ct)
    {
        var agora = relogio.Agora;
        var prontos = await uow.ListarAsync(uow.Consulta<EncaminhamentoInstitucional>()
            .Where(e => (e.Status == StatusEncaminhamento.Pendente || e.Status == StatusEncaminhamento.FalhaTemporaria) && e.ProximaTentativaEm <= agora)
            .OrderBy(e => e.ProximaTentativaEm).Take(limite), ct);
        if (prontos.Count == 0) return 0;

        var ids = prontos.Select(p => p.InstituicaoId).Distinct().ToList();
        var instituicoes = (await uow.ListarAsync(uow.Consulta<Instituicao>().Where(i => ids.Contains(i.Id)), ct)).ToDictionary(i => i.Id);

        // Marca como "Transmitindo" antes de chamar a rede (evita envio concorrente).
        foreach (var e in prontos) e.IniciarTransmissao();
        await uow.SalvarAsync(ct);

        // Mensagens montadas sequencialmente (acesso ao banco); envios HTTP em paralelo, cada um independente.
        var lote = new List<(EncaminhamentoInstitucional E, Instituicao? I, object? M)>();
        foreach (var e in prontos) lote.Add((e, instituicoes.GetValueOrDefault(e.InstituicaoId), await MontarMensagemAsync(e, ct)));
        var resultados = await Task.WhenAll(lote.Select(x => EnviarAsync(x.E, x.I, x.M, ct)));
        foreach (var (e, inst, resultado, iniciada) in resultados) Aplicar(e, inst, resultado, iniciada);
        await uow.SalvarAsync(ct);

        foreach (var grupo in prontos.GroupBy(p => (p.TipoMensagem, p.ReferenciaId)))
            await AtualizarReferenciaAsync(grupo.Key.TipoMensagem, grupo.Key.ReferenciaId, ct);
        await uow.SalvarAsync(ct);
        return prontos.Count;
    }

    private async Task<(EncaminhamentoInstitucional, Instituicao?, ResultadoEnvioCentral, DateTimeOffset)> EnviarAsync(
        EncaminhamentoInstitucional e, Instituicao? inst, object? mensagem, CancellationToken ct)
    {
        var iniciada = relogio.Agora;
        if (inst is null || !inst.Habilitada)
            return (e, inst, new ResultadoEnvioCentral(false, false, null, null, null, false, "Instituição inexistente ou desabilitada.", 0), iniciada);
        if (mensagem is null)
            return (e, inst, new ResultadoEnvioCentral(false, false, null, null, null, false, "Referência não encontrada para montar a mensagem.", 0), iniciada);
        try
        {
            return (e, inst, await cliente.EnviarAsync(inst, mensagem, e.Id.ToString(), ct), iniciada);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (e, inst, new ResultadoEnvioCentral(false, true, null, null, null, false, $"{ex.GetType().Name}: {ex.Message}",
                (long)(relogio.Agora - iniciada).TotalMilliseconds), iniciada);
        }
    }

    private void Aplicar(EncaminhamentoInstitucional e, Instituicao? inst, ResultadoEnvioCentral r, DateTimeOffset iniciada)
    {
        if (r.Sucesso && r.Protocolo is not null)
            e.ConfirmarRecebimento(r.Protocolo, r.RecebidoEm ?? relogio.Agora, r.HttpStatus ?? 200, r.DuracaoMs, iniciada, r.Duplicado);
        else
            e.RegistrarFalha(r.FalhaTemporaria, r.HttpStatus, r.Erro ?? "Resposta sem protocolo.", r.DuracaoMs, iniciada, relogio.Agora);
        log.LogInformation("Encaminhamento {Id} → {Central}: {Status} (HTTP {Http}, {Ms} ms)", e.Id, inst?.Codigo, e.Status, r.HttpStatus, r.DuracaoMs);
    }

    private async Task<object?> MontarMensagemAsync(EncaminhamentoInstitucional e, CancellationToken ct)
    {
        if (e.TipoMensagem is TipoMensagem.Ocorrencia or TipoMensagem.AtualizacaoOcorrencia)
        {
            var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == e.ReferenciaId), ct);
            if (o is null) return null;
            var a = await uow.PrimeiroAsync(uow.Consulta<AnaliseOcorrencia>().Where(x => x.OcorrenciaId == o.Id), ct);
            return ConstrutorMensagens.Ocorrencia(o, a, e.Id, e.TipoMensagem);
        }
        var oferta = await uow.PrimeiroAsync(uow.Consulta<OfertaAjuda>().Where(x => x.Id == e.ReferenciaId), ct);
        if (oferta?.NecessidadeId is null) return null;
        var n = await uow.PrimeiroAsync(uow.Consulta<NecessidadePublicada>().Where(x => x.Id == oferta.NecessidadeId), ct);
        return n is null ? null : ConstrutorMensagens.Oferta(oferta, n, e.Id);
    }

    public async Task AtualizarReferenciaAsync(TipoMensagem tipo, Guid referenciaId, CancellationToken ct)
    {
        var encs = await uow.ListarAsync(uow.Consulta<EncaminhamentoInstitucional>().Where(x => x.ReferenciaId == referenciaId), ct);
        var agora = relogio.Agora;
        if (tipo is TipoMensagem.Ocorrencia or TipoMensagem.AtualizacaoOcorrencia)
        {
            var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == referenciaId), ct);
            if (o is null) return;
            var principais = encs.Where(x => x.TipoMensagem == TipoMensagem.Ocorrencia).ToList();
            foreach (var enc in encs.Where(x => x.TipoMensagem == TipoMensagem.Ocorrencia && x.HistoricoTentativas.Count > 0))
            {
                var ultima = enc.HistoricoTentativas[^1];
                var chave = $"{enc.InstituicaoCodigo}#{ultima.Numero}#{ultima.Resultado}";
                if (o.Historico.Any(h => h.Tipo == "encaminhamento:" + chave)) continue;
                if (enc.Status == StatusEncaminhamento.Recebido && enc.Protocolo is not null)
                    uow.Adicionar(ConfirmacaoEntrega.Criar(o.Id, enc.Id.ToString(), NivelEntrega.RecebidoPelaCentralDemonstrativa,
                        "api-alerta-em-acao", enc.InstituicaoCodigo + " (SIMULADA)", enc.ConfirmadoEm ?? agora, "http",
                        "persistido pela central simulada", enc.Protocolo, agora));
                o.Registrar(agora, "encaminhamento:" + chave, enc.Status switch
                {
                    StatusEncaminhamento.Recebido => $"Central SIMULADA {enc.InstituicaoCodigo} confirmou recebimento técnico. Protocolo demonstrativo {enc.Protocolo}.",
                    StatusEncaminhamento.FalhaTemporaria => $"Falha ao transmitir para {enc.InstituicaoCodigo} (tentativa {enc.Tentativas}): {enc.UltimoErro}. Nova tentativa agendada.",
                    StatusEncaminhamento.Rejeitado => $"Central {enc.InstituicaoCodigo} rejeitou a mensagem: {enc.UltimoErro}.",
                    StatusEncaminhamento.Esgotado => $"Tentativas esgotadas para {enc.InstituicaoCodigo}. Reenvio manual disponível.",
                    _ => $"{enc.InstituicaoCodigo}: {enc.Status}",
                }, enc.InstituicaoCodigo);
            }
            o.RecalcularStatus(principais.Select(x => x.Status).ToList(), agora);
            await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, encs.Select(x => x.InstituicaoCodigo).Distinct().ToList(), "encaminhamento");
        }
        else
        {
            var oferta = await uow.PrimeiroAsync(uow.Consulta<OfertaAjuda>().Where(x => x.Id == referenciaId), ct);
            var enc = encs.OrderByDescending(x => x.CriadoEm).FirstOrDefault();
            if (oferta is null || enc is null) return;
            if (enc.Status == StatusEncaminhamento.Recebido && enc.Protocolo is not null) oferta.RegistrarRecebimento(enc.Protocolo, agora);
            await tempoReal.OfertaAtualizadaAsync(oferta.Id, enc.InstituicaoCodigo);
        }
    }

    /// <summary>Reenvio manual de um encaminhamento (ex.: após reativar a central).</summary>
    public async Task ReenviarAsync(Guid encaminhamentoId, CancellationToken ct)
    {
        var e = await uow.PrimeiroAsync(uow.Consulta<EncaminhamentoInstitucional>().Where(x => x.Id == encaminhamentoId), ct)
                ?? throw new ErroNaoEncontrado("Encaminhamento não encontrado.");
        e.SolicitarReenvio(relogio.Agora);
        await uow.SalvarAsync(ct);
    }

    /// <summary>Processa retorno assíncrono de uma central (mudança de estado do atendimento demonstrativo).</summary>
    public async Task RegistrarRetornoAsync(RetornoEstadoCentralV1 r, CancellationToken ct)
    {
        var e = await uow.PrimeiroAsync(uow.Consulta<EncaminhamentoInstitucional>()
                    .Where(x => x.Protocolo == r.Protocolo && x.InstituicaoCodigo == r.CentralCodigo), ct)
                ?? throw new ErroNaoEncontrado("Protocolo não reconhecido para esta central.");
        e.AtualizarEstadoNaCentral(r.Estado);
        var agora = relogio.Agora;
        if (e.TipoMensagem is TipoMensagem.Ocorrencia or TipoMensagem.AtualizacaoOcorrencia)
        {
            var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == e.ReferenciaId), ct);
            if (o is not null)
            {
                var desc = $"Central SIMULADA {r.CentralCodigo} atualizou o atendimento demonstrativo para \"{r.Estado}\"{(r.Nota is null ? "" : $": {r.Nota}")}.";
                if (r.Estado is "EquipeDesignadaFicticia" or "EmAtendimentoFicticio") o.MarcarEmAtendimentoSimulado(r.CentralCodigo, desc, agora);
                else if (r.Estado == "ConcluidoFicticio") o.Encerrar(r.CentralCodigo, desc, agora);
                else o.Registrar(agora, "retorno_central", desc, r.CentralCodigo);
                await uow.SalvarAsync(ct);
                await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, [r.CentralCodigo], "retorno_central");
            }
        }
        else
        {
            var oferta = await uow.PrimeiroAsync(uow.Consulta<OfertaAjuda>().Where(x => x.Id == e.ReferenciaId), ct);
            if (oferta is not null)
            {
                if (r.Estado == "Aceita") oferta.RegistrarRespostaOrganizacao(true, r.Nota, agora);
                else if (r.Estado == "Recusada") oferta.RegistrarRespostaOrganizacao(false, r.Nota, agora);
                else if (r.Estado == "Concluida") oferta.Concluir(agora);
                await uow.SalvarAsync(ct);
                await tempoReal.OfertaAtualizadaAsync(oferta.Id, r.CentralCodigo);
            }
        }
    }

    public async Task<int> RecuperarInterrompidosAsync(CancellationToken ct)
    {
        var presos = await uow.ListarAsync(uow.Consulta<EncaminhamentoInstitucional>().Where(e => e.Status == StatusEncaminhamento.Transmitindo), ct);
        foreach (var e in presos) e.RecuperarTransmissaoInterrompida(relogio.Agora);
        await uow.SalvarAsync(ct);
        return presos.Count;
    }
}
