using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Modulos.ApoioComunitario;

public enum TipoAjuda { DoacaoAgua, DoacaoAlimentos, Roupas, MedicamentosViaCanalAdequado, KitHigiene, ApoioEmAbrigo, ApoioLogisticoSeguro, VoluntariadoLocalVerificado, TransporteAssistido }

public enum StatusNecessidade { Aberta, Atendida, Encerrada }

/// <summary>
/// Necessidade publicada por organização verificada (na demonstração: organizações fictícias).
/// O ponto de entrega é sempre um local verificado pela organização — nunca a localização de vítimas
/// e nunca dentro de área de perigo.
/// </summary>
public sealed class NecessidadePublicada
{
    public Guid Id { get; private set; }
    public Guid OrganizacaoId { get; private set; }
    public string OrganizacaoCodigo { get; private set; } = default!;
    public string OrganizacaoNome { get; private set; } = default!;
    public string MunicipioCodigo { get; private set; } = default!;
    public TipoAjuda Tipo { get; private set; }
    public string Titulo { get; private set; } = default!;
    public string Descricao { get; private set; } = default!;
    public string PontoEntregaVerificado { get; private set; } = default!;
    public StatusNecessidade Status { get; private set; }
    public bool Ficticia { get; private set; }
    public DateTimeOffset PublicadaEm { get; private set; }

    private NecessidadePublicada() { }

    public static NecessidadePublicada Publicar(Guid orgId, string orgCodigo, string orgNome, string municipio, TipoAjuda tipo,
        string titulo, string descricao, string pontoEntrega, bool ficticia, DateTimeOffset agora)
    {
        if (!MunicipiosRmr.Pertence(municipio)) throw new ErroDominio("municipio_invalido", "Município fora da RMR.");
        if (string.IsNullOrWhiteSpace(pontoEntrega)) throw new ErroDominio("ponto_entrega", "Informe um ponto de entrega verificado e seguro.");
        return new NecessidadePublicada
        {
            Id = Guid.CreateVersion7(), OrganizacaoId = orgId, OrganizacaoCodigo = orgCodigo, OrganizacaoNome = orgNome,
            MunicipioCodigo = municipio, Tipo = tipo, Titulo = (ficticia ? "[FICTÍCIA] " : "") + titulo, Descricao = descricao,
            PontoEntregaVerificado = pontoEntrega, Status = StatusNecessidade.Aberta, Ficticia = ficticia, PublicadaEm = agora,
        };
    }

    public void Encerrar() => Status = StatusNecessidade.Encerrada;
}

public enum StatusOferta
{
    Registrada,
    /// <summary>Participação solicitada; aguardando confirmação técnica da organização.</summary>
    EnviadaOrganizacao,
    /// <summary>Organização (simulada) confirmou o recebimento com protocolo.</summary>
    RecebidaPelaOrganizacao,
    /// <summary>Organização aceitou a participação.</summary>
    ConfirmadaPelaOrganizacao,
    RecusadaPelaOrganizacao,
    Concluida,
    Cancelada,
    /// <summary>Oferta para um pedido da comunidade, aguardando avaliação da equipe responsável.</summary>
    AguardandoAvaliacao,
    AceitaPelaEquipe,
    RecusadaPelaEquipe,
    /// <summary>Equipe enviou orientações ao voluntário (ponto seguro, o que levar). Não é despacho de resgate.</summary>
    Encaminhada,
}

public sealed record EventoOferta(DateTimeOffset Em, string Descricao);

public sealed class OfertaAjuda
{
    public Guid Id { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public string DispositivoId { get; private set; } = default!;
    public string MunicipioCodigo { get; private set; } = default!;
    public string? RegiaoDisponibilidade { get; private set; }
    public TipoAjuda Tipo { get; private set; }
    public string Descricao { get; private set; } = default!;
    public string? Disponibilidade { get; private set; }
    public string? OrganizacaoReferencia { get; private set; }
    /// <summary>Contato compartilhado apenas com a organização escolhida, com autorização explícita.</summary>
    public string? ContatoAutorizado { get; private set; }
    public Guid? NecessidadeId { get; private set; }
    /// <summary>Pedido de socorro ao qual a oferta se refere (ajuda comunitária).</summary>
    public Guid? OcorrenciaId { get; private set; }
    /// <summary>Orientação da equipe ao voluntário — único canal pelo qual detalhes adicionais chegam a ele.</summary>
    public string? InstrucaoEquipe { get; private set; }
    public StatusOferta Status { get; private set; }
    public string? ProtocoloOrganizacao { get; private set; }
    public List<EventoOferta> Historico { get; private set; } = [];
    public DateTimeOffset CriadaEm { get; private set; }

    private OfertaAjuda() { }

    public static OfertaAjuda Registrar(Guid id, Guid? usuarioId, string dispositivoId, string municipio, string? regiao, TipoAjuda tipo,
        string descricao, string? disponibilidade, string? orgReferencia, string? contato, DateTimeOffset agora)
    {
        if (!MunicipiosRmr.Pertence(municipio)) throw new ErroDominio("municipio_invalido", "Município fora da RMR.");
        descricao = (descricao ?? "").Trim();
        if (descricao.Length is < 3 or > 500) throw new ErroDominio("descricao_invalida", "Descreva a ajuda em 3 a 500 caracteres.");
        var o = new OfertaAjuda
        {
            Id = id == Guid.Empty ? Guid.CreateVersion7() : id, UsuarioId = usuarioId, DispositivoId = dispositivoId, MunicipioCodigo = municipio,
            RegiaoDisponibilidade = regiao, Tipo = tipo, Descricao = descricao, Disponibilidade = disponibilidade,
            OrganizacaoReferencia = orgReferencia, ContatoAutorizado = contato, Status = StatusOferta.Registrada, CriadaEm = agora,
        };
        o.Historico.Add(new(agora, "Oferta registrada."));
        return o;
    }

    public void SolicitarParticipacao(NecessidadePublicada n, DateTimeOffset agora)
    {
        if (n.Status != StatusNecessidade.Aberta) throw new ErroDominio("necessidade_fechada", "Esta necessidade não está aberta.");
        if (Status is not (StatusOferta.Registrada or StatusOferta.RecusadaPelaOrganizacao))
            throw new ErroDominio("oferta_em_andamento", "Esta oferta já está vinculada a uma solicitação.");
        NecessidadeId = n.Id;
        Status = StatusOferta.EnviadaOrganizacao;
        Historico.Add(new(agora, $"Participação solicitada em \"{n.Titulo}\" ({n.OrganizacaoNome})."));
    }

    public void RegistrarRecebimento(string protocolo, DateTimeOffset agora)
    {
        if (Status != StatusOferta.EnviadaOrganizacao) return;
        Status = StatusOferta.RecebidaPelaOrganizacao;
        ProtocoloOrganizacao = protocolo;
        Historico.Add(new(agora, $"Organização SIMULADA confirmou recebimento técnico (protocolo {protocolo})."));
    }

    public void RegistrarRespostaOrganizacao(bool aceita, string? nota, DateTimeOffset agora)
    {
        if (Status is not (StatusOferta.RecebidaPelaOrganizacao or StatusOferta.EnviadaOrganizacao)) return;
        Status = aceita ? StatusOferta.ConfirmadaPelaOrganizacao : StatusOferta.RecusadaPelaOrganizacao;
        Historico.Add(new(agora, (aceita ? "Organização aceitou a participação" : "Organização recusou a participação") + (nota is null ? "." : $": {nota}")));
    }

    public void Concluir(DateTimeOffset agora) { Status = StatusOferta.Concluida; Historico.Add(new(agora, "Concluída.")); }

    public const string AvisoSeguranca =
        "Ajuda comunitária não é resgate. Não entre em água, correnteza, encostas ou imóveis danificados. " +
        "Aguarde a orientação da equipe e entregue somente em local seguro indicado por ela.";

    public static OfertaAjuda RegistrarParaPedido(Guid id, Guid? usuarioId, string dispositivoId, Guid ocorrenciaId, string? municipio, TipoAjuda tipo,
        string descricao, string? disponibilidade, string? contato, bool cienteSeguranca, DateTimeOffset agora)
    {
        if (!cienteSeguranca) throw new ErroDominio("ciencia_obrigatoria", "Confirme que leu as orientações de segurança antes de oferecer ajuda.");
        if (municipio is null) throw new ErroDominio("municipio_desconhecido", "Este pedido não tem município identificado na RMR.");
        var o = Registrar(id, usuarioId, dispositivoId, municipio, null, tipo, descricao, disponibilidade, null, string.IsNullOrWhiteSpace(contato) ? null : contato.Trim()[..Math.Min(120, contato.Trim().Length)], agora);
        o.OcorrenciaId = ocorrenciaId;
        o.Status = StatusOferta.AguardandoAvaliacao;
        o.Historico.Add(new(agora, "Oferta enviada para avaliação da equipe responsável pelo pedido."));
        return o;
    }

    public void AvaliarPelaEquipe(bool aceita, string? nota, string ator, DateTimeOffset agora)
    {
        if (OcorrenciaId is null) throw new ErroDominio("oferta_sem_pedido", "Oferta não vinculada a pedido.");
        if (Status != StatusOferta.AguardandoAvaliacao) throw new ErroDominio("estado_invalido", "A oferta não está aguardando avaliação.");
        Status = aceita ? StatusOferta.AceitaPelaEquipe : StatusOferta.RecusadaPelaEquipe;
        Historico.Add(new(agora, (aceita ? "Equipe aceitou a oferta" : "Equipe recusou a oferta") + (string.IsNullOrWhiteSpace(nota) ? "." : $": {nota.Trim()}") + $" ({ator})"));
    }

    public void Encaminhar(string instrucao, string ator, DateTimeOffset agora)
    {
        if (Status != StatusOferta.AceitaPelaEquipe) throw new ErroDominio("estado_invalido", "Aceite a oferta antes de encaminhar.");
        instrucao = (instrucao ?? "").Trim();
        if (instrucao.Length is < 5 or > 500) throw new ErroDominio("instrucao_invalida", "Escreva a orientação em 5 a 500 caracteres.");
        InstrucaoEquipe = instrucao;
        Status = StatusOferta.Encaminhada;
        Historico.Add(new(agora, $"Equipe enviou orientações ao voluntário ({ator})."));
    }

    public void Acompanhar(string acao, string? nota, DateTimeOffset agora)
    {
        if (OcorrenciaId is null) throw new ErroDominio("oferta_sem_pedido", "Oferta não vinculada a pedido.");
        nota = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim()[..Math.Min(300, nota.Trim().Length)];
        switch (acao)
        {
            case "concluida" when Status == StatusOferta.Encaminhada:
                Status = StatusOferta.Concluida; Historico.Add(new(agora, "Voluntário informou entrega concluída" + (nota is null ? "." : $": {nota}"))); break;
            case "cancelada" when Status is StatusOferta.AguardandoAvaliacao or StatusOferta.AceitaPelaEquipe or StatusOferta.Encaminhada:
                Status = StatusOferta.Cancelada; Historico.Add(new(agora, "Voluntário cancelou a oferta" + (nota is null ? "." : $": {nota}"))); break;
            case "nota" when nota is not null && Status is not (StatusOferta.Concluida or StatusOferta.Cancelada):
                Historico.Add(new(agora, $"Atualização do voluntário: {nota}")); break;
            default: throw new ErroDominio("acao_invalida", "Ação de acompanhamento não permitida no estado atual.");
        }
    }

    public bool Ativa => Status is StatusOferta.AguardandoAvaliacao or StatusOferta.AceitaPelaEquipe or StatusOferta.Encaminhada;
}
