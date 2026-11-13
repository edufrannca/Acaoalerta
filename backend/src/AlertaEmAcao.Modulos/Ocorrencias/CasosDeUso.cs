using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Encaminhamento;
using AlertaEmAcao.Modulos.Familias;
using AlertaEmAcao.Modulos.Localizacao;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Modulos.Ocorrencias;

public sealed record ComandoRegistrarSocorro(
    Guid Id,
    string DispositivoId,
    DateTimeOffset CriadaNoDispositivoEm,
    double? Latitude,
    double? Longitude,
    double? PrecisaoMetros,
    DateTimeOffset? LocalizacaoLidaEm,
    ComplementoOcorrencia? Complemento,
    Guid? AvisoId,
    bool IncluirResumoFamilia,
    CanalRecebimento Canal = CanalRecebimento.Internet,
    string? RetransmitidaPor = null,
    bool Demonstracao = true,
    string? IdentificadorMensagem = null,
    CompartilhamentoComunitario? Compartilhamento = null,
    bool? AssociarResidencia = null);

public sealed record ResultadoRegistro(Ocorrencia Ocorrencia, bool JaExistia, IReadOnlyList<EncaminhamentoInstitucional> Encaminhamentos);

public sealed class ServicoOcorrencias(IUnidadeTrabalho uow, ICatalogoMunicipios catalogo, ServicoAnalise analise,
    INotificadorTempoReal tempoReal, IRelogio relogio, ILogger<ServicoOcorrencias> log, Identidade.ServicoConsentimentos consentimentos)
{
    /// <summary>
    /// Preferências salvas no cadastro (consentimentos C e D) aplicadas a um pedido NOVO de pessoa cadastrada.
    /// Pedido anônimo nunca passa por aqui; nada é associado por proximidade de GPS.
    /// </summary>
    private async Task AplicarPreferenciasAsync(Ocorrencia o, Guid usuarioId, ComandoRegistrarSocorro cmd, DateTimeOffset agora, CancellationToken ct)
    {
        var pref = await consentimentos.AtualAsync(usuarioId, ct);
        if (pref.CompartilhamentoComunitario && cmd.Compartilhamento is null)
            o.DefinirCompartilhamento(new CompartilhamentoComunitario(true, false, true, null, CompartilhamentoComunitario.OrigemPreferencia), agora);
        if (pref.NotificacaoFamiliares && cmd.AssociarResidencia != false
            && await uow.PrimeiroAsync(uow.Consulta<Localizacao.LocalMonitorado>().Where(l => l.UsuarioId == usuarioId && l.Ativo && l.MoroAqui), ct) is { } res)
            o.AssociarResidencia(res.Id, $"Associado à residência declarada pelo solicitante (\"{res.Apelido}\"), conforme autorização de notificar familiares.", agora);
    }

    /// <summary>Associação explícita (ou remoção) feita pelo próprio solicitante cadastrado, ex.: SOS feito antes de entrar na conta.</summary>
    public async Task<Ocorrencia> AssociarResidenciaAsync(Guid id, string dispositivoId, Guid usuarioId, bool associar, CancellationToken ct)
    {
        var o = await ObterDoSolicitanteAsync(id, dispositivoId, usuarioId, ct);
        var agora = relogio.Agora;
        if (!associar) { o.AssociarResidencia(null, "Solicitante desfez a associação com a residência.", agora); await uow.SalvarAsync(ct); return o; }
        if (!(await consentimentos.AtualAsync(usuarioId, ct)).NotificacaoFamiliares)
            throw new ErroDominio("sem_consentimento", "Ative no perfil a autorização para avisar familiares vinculados.");
        var res = await uow.PrimeiroAsync(uow.Consulta<Localizacao.LocalMonitorado>().Where(l => l.UsuarioId == usuarioId && l.Ativo && l.MoroAqui), ct)
            ?? throw new ErroDominio("sem_residencia", "Cadastre a residência onde você mora (\"Eu moro aqui\") em Perfil.");
        o.VincularUsuario(usuarioId);
        o.AssociarResidencia(res.Id, $"Solicitante associou o pedido à residência \"{res.Apelido}\".", agora);
        await uow.SalvarAsync(ct);
        await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, [], "residencia_associada");
        return o;
    }

    /// <summary>Revogação do consentimento C: retira da comunidade os pedidos ativos que dependiam dele.</summary>
    public async Task<int> RetirarCompartilhamentoPorPreferenciaAsync(Guid usuarioId, CancellationToken ct)
    {
        var agora = relogio.Agora;
        var os = (await uow.ListarAsync(uow.Consulta<Ocorrencia>().Where(o => o.UsuarioId == usuarioId && o.Status != StatusOcorrencia.Encerrada), ct))
            .Where(o => o.Compartilhamento.Autorizado && o.Compartilhamento.Origem == CompartilhamentoComunitario.OrigemPreferencia).ToList();
        foreach (var o in os) o.DefinirCompartilhamento(CompartilhamentoComunitario.Nenhum, agora);
        await uow.SalvarAsync(ct);
        return os.Count;
    }

    /// <summary>
    /// Registra o pedido de socorro (idempotente pelo Id gerado no aparelho), aplica regras determinísticas,
    /// determina destinos por município e cria encaminhamentos pendentes — tudo numa única transação.
    /// A IA é processada depois, em segundo plano, e não atrasa a transmissão.
    /// </summary>
    public async Task<ResultadoRegistro> RegistrarSocorroAsync(ComandoRegistrarSocorro cmd, Guid? usuarioId, CancellationToken ct)
    {
        var existente = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(o => o.Id == cmd.Id), ct);
        if (existente is not null)
        {
            if (!existente.PertenceAoDispositivo(cmd.DispositivoId) && Ocorrencia.PseudonimoEmissor(existente.DispositivoId) != cmd.DispositivoId && (usuarioId is null || existente.UsuarioId != usuarioId))
                throw new ErroDominio("id_conflito", "Identificador já utilizado por outro dispositivo.");
            var encsExistentes = await uow.ListarAsync(uow.Consulta<EncaminhamentoInstitucional>().Where(e => e.ReferenciaId == cmd.Id), ct);
            return new ResultadoRegistro(existente, true, encsExistentes);
        }

        var agora = relogio.Agora;
        PosicaoDispositivo? posicao = null;
        string? municipio = null;
        if (PontoGeo.TentarCriar(cmd.Latitude, cmd.Longitude, out var ponto))
        {
            posicao = new PosicaoDispositivo(ponto.Latitude, ponto.Longitude, cmd.PrecisaoMetros, cmd.LocalizacaoLidaEm ?? cmd.CriadaNoDispositivoEm, "gps_dispositivo");
            municipio = catalogo.Identificar(ponto)?.Codigo;
        }

        var complemento = cmd.Complemento ?? new ComplementoOcorrencia();
        if (cmd.IncluirResumoFamilia && usuarioId is not null)
            complemento = await EnriquecerComFamiliaAsync(complemento, usuarioId.Value, ct);

        var o = Ocorrencia.Registrar(cmd.Id, usuarioId, cmd.DispositivoId, cmd.CriadaNoDispositivoEm, posicao, municipio, complemento,
            cmd.Canal, cmd.RetransmitidaPor, cmd.AvisoId, cmd.Demonstracao, agora);
        if (cmd.Compartilhamento is { Autorizado: true } comp) o.DefinirCompartilhamento(comp with { Origem = CompartilhamentoComunitario.OrigemPedido }, agora);
        if (usuarioId is { } uidPref) await AplicarPreferenciasAsync(o, uidPref, cmd, agora, ct);
        if (posicao is not null && municipio is null)
            o.Registrar(agora, "municipio", "Coordenadas fora dos 14 municípios da RMR. Sem destino configurado.");
        uow.Adicionar(o);
        // Nível 4: gravado na mesma transação da ocorrência — só existe se a ocorrência foi persistida.
        uow.Adicionar(ConfirmacaoEntrega.Criar(o.Id, cmd.IdentificadorMensagem, NivelEntrega.RecebidoPeloBackend,
            cmd.RetransmitidaPor ?? "aparelho-emissor", "api-alerta-em-acao", agora, cmd.Canal.ToString(), "persistido", o.Id.ToString(), agora));

        var a = await analise.AvaliarRegrasAsync(o, ct);
        var encs = await CriarEncaminhamentosAsync(o, TipoMensagem.Ocorrencia, [], ct);
        o.RecalcularStatus(encs.Select(e => e.Status).ToList(), agora);
        if (encs.Count == 0 && municipio is not null)
            o.Registrar(agora, "encaminhamento", "Nenhuma regra de encaminhamento configurada para este município/tipo.");

        try
        {
            await uow.SalvarAsync(ct);
        }
        catch (Exception e) when (EhViolacaoUnicidade(e))
        {
            // Corrida entre duas sincronizações do mesmo pedido: devolve o registro já gravado.
            log.LogInformation("Registro concorrente da ocorrência {Id}; retornando existente.", cmd.Id);
            throw new ErroDominio("registro_concorrente", "Pedido já recebido; consulte novamente.");
        }
        await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, encs.Select(e => e.InstituicaoCodigo).ToList(), "registrada");
        return new ResultadoRegistro(o, false, encs);
    }

    private static bool EhViolacaoUnicidade(Exception e) =>
        e.ToString().Contains("23505", StringComparison.Ordinal) || e.ToString().Contains("duplicate key", StringComparison.OrdinalIgnoreCase);

    private async Task<ComplementoOcorrencia> EnriquecerComFamiliaAsync(ComplementoOcorrencia c, Guid usuarioId, CancellationToken ct)
    {
        var f = await uow.PrimeiroAsync(uow.Consulta<GrupoFamiliar>().Where(g => g.UsuarioId == usuarioId), ct);
        if (f is null || f.Quantidade == 0) return c;
        // O cadastro familiar é referência; o que a pessoa informar agora prevalece.
        return c with
        {
            QuantidadePessoas = c.QuantidadePessoas,
            Pessoas = c.Pessoas ?? f.Integrantes.Select(i => new PessoaEnvolvida(i.Idade, false, i.MobilidadeReduzida, i.DependeAssistencia)).ToList(),
            Criancas = c.Criancas ?? (f.Criancas > 0 ? f.Criancas : null),
            Idosos = c.Idosos ?? (f.Idosos > 0 ? f.Idosos : null),
            MobilidadeReduzida = c.MobilidadeReduzida ?? (f.AlguemComMobilidadeReduzida ? true : null),
            EquipamentoMedicoEssencial = c.EquipamentoMedicoEssencial ?? (f.AlguemUsaEquipamentoMedico ? true : null),
        };
    }

    private async Task<List<EncaminhamentoInstitucional>> CriarEncaminhamentosAsync(Ocorrencia o, TipoMensagem tipo, IReadOnlyCollection<Guid> jaDestinados, CancellationToken ct)
    {
        var indicios = DetectorIndicios.Detectar(o.Complemento).Select(i => i.Tipo).ToHashSet();
        var regras = await uow.ListarAsync(uow.Consulta<RegraEncaminhamento>().Where(r => r.Ativa), ct);
        var insts = await uow.ListarAsync(uow.Consulta<Instituicao>().Where(i => i.Habilitada), ct);
        var destinos = RoteadorEncaminhamento.Determinar(o.MunicipioCodigo, indicios, regras, insts);
        var agora = relogio.Agora;
        var criados = new List<EncaminhamentoInstitucional>();
        foreach (var d in destinos.Where(d => !jaDestinados.Contains(d.Instituicao.Id)))
        {
            var e = EncaminhamentoInstitucional.Criar(tipo, o.Id, d.Instituicao, d.Justificativa, agora);
            uow.Adicionar(e);
            criados.Add(e);
            o.Registrar(agora, "roteamento", $"Destino determinado: {d.Instituicao.Nome}. {d.Justificativa}.");
        }
        return criados;
    }

    public async Task<Ocorrencia> ComplementarAsync(Guid id, string dispositivoId, Guid? usuarioId, ComplementoOcorrencia complemento, CancellationToken ct)
    {
        var o = await ObterDoSolicitanteAsync(id, dispositivoId, usuarioId, ct);
        var agora = relogio.Agora;
        o.Complementar(complemento, agora);
        await analise.AvaliarRegrasAsync(o, ct);

        var existentes = await uow.ListarAsync(uow.Consulta<EncaminhamentoInstitucional>().Where(e => e.ReferenciaId == id), ct);
        var novos = await CriarEncaminhamentosAsync(o, TipoMensagem.Ocorrencia, existentes.Select(e => e.InstituicaoId).ToList(), ct);
        // Centrais que já receberam o pedido recebem a atualização.
        foreach (var inst in existentes.Where(e => e.TipoMensagem == TipoMensagem.Ocorrencia).Select(e => e.InstituicaoId).Distinct())
        {
            var i = await uow.PrimeiroAsync(uow.Consulta<Instituicao>().Where(x => x.Id == inst), ct);
            if (i is not null) uow.Adicionar(EncaminhamentoInstitucional.Criar(TipoMensagem.AtualizacaoOcorrencia, o.Id, i, "Atualização de pedido já encaminhado", agora));
        }
        o.RecalcularStatus(existentes.Concat(novos).Where(e => e.TipoMensagem == TipoMensagem.Ocorrencia).Select(e => e.Status).ToList(), agora);
        await uow.SalvarAsync(ct);
        await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, existentes.Concat(novos).Select(e => e.InstituicaoCodigo).Distinct().ToList(), "complemento");
        return o;
    }

    public async Task<(Ocorrencia Ocorrencia, bool Aceita)> AtualizarLocalizacaoAsync(Guid id, string dispositivoId, Guid? usuarioId,
        double lat, double lon, double? precisao, DateTimeOffset lidaEm, CancellationToken ct)
    {
        var o = await ObterDoSolicitanteAsync(id, dispositivoId, usuarioId, ct);
        var p = new PontoGeo(lat, lon);
        var agora = relogio.Agora;
        var aceita = o.AtualizarLocalizacao(new PosicaoDispositivo(lat, lon, precisao, lidaEm > agora ? agora : lidaEm, "gps_dispositivo"),
            catalogo.Identificar(p)?.Codigo, agora);
        if (aceita)
        {
            await uow.SalvarAsync(ct);
            await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, [], "localizacao");
        }
        return (o, aceita);
    }

    public async Task<Ocorrencia> DefinirCompartilhamentoAsync(Guid id, string dispositivoId, Guid? usuarioId, CompartilhamentoComunitario c, CancellationToken ct)
    {
        var o = await ObterDoSolicitanteAsync(id, dispositivoId, usuarioId, ct);
        o.DefinirCompartilhamento(c with { Origem = CompartilhamentoComunitario.OrigemPedido }, relogio.Agora);
        await uow.SalvarAsync(ct);
        await tempoReal.OcorrenciaAtualizadaAsync(o.Id, o.MunicipioCodigo, [], "compartilhamento");
        return o;
    }

    public async Task<Ocorrencia> ObterDoSolicitanteAsync(Guid id, string? dispositivoId, Guid? usuarioId, CancellationToken ct)
    {
        var o = await uow.PrimeiroAsync(uow.Consulta<Ocorrencia>().Where(x => x.Id == id), ct)
                ?? throw new ErroNaoEncontrado("Pedido não encontrado.");
        var dono = (usuarioId is not null && o.UsuarioId == usuarioId) || o.PertenceAoDispositivo(dispositivoId);
        if (!dono) throw new ErroAcessoNegado("Este pedido pertence a outro solicitante.");
        return o;
    }

    public async Task<(SituacaoDeclarada Situacao, bool JaExistia)> DeclararSituacaoAsync(Guid id, string dispositivoId, Guid? usuarioId,
        StatusSituacao status, DateTimeOffset em, double? lat, double? lon, Guid? avisoId, bool desejaAjudar, string? mensagem, CancellationToken ct)
    {
        var existente = await uow.PrimeiroAsync(uow.Consulta<SituacaoDeclarada>().Where(s => s.Id == id), ct);
        if (existente is not null) return (existente, true);
        PontoGeo? ponto = PontoGeo.TentarCriar(lat, lon, out var p) ? p : null;
        var municipio = ponto is null ? null : catalogo.Identificar(ponto.Value)?.Codigo;
        var s = SituacaoDeclarada.Declarar(id, usuarioId, dispositivoId, status, em, ponto, municipio, avisoId, desejaAjudar, mensagem, relogio.Agora);
        uow.Adicionar(s);
        await uow.SalvarAsync(ct);
        return (s, false);
    }
}
