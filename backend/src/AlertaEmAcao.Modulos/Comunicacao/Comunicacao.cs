using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Modulos.Comunicacao;

/// <summary>Publicação de eventos em tempo (próximo do) real para painéis e aplicativos conectados.</summary>
public interface INotificadorTempoReal
{
    Task OcorrenciaAtualizadaAsync(Guid ocorrenciaId, string? municipioCodigo, IReadOnlyCollection<string> centraisCodigos, string motivo);
    Task AvisoPublicadoAsync(Guid avisoId, bool oficial, IReadOnlyCollection<string> municipios);
    Task NotificacaoUsuarioAsync(Guid usuarioId, object conteudo);
    Task OfertaAtualizadaAsync(Guid ofertaId, string organizacaoCodigo);
    /// <summary>Novas leituras de chuva processadas: telas abertas recarregam o indicador (sem dados pessoais).</summary>
    Task CondicoesAtualizadasAsync(object resumo) => Task.CompletedTask;
}

public sealed class NotificadorNulo : INotificadorTempoReal
{
    public Task OcorrenciaAtualizadaAsync(Guid ocorrenciaId, string? municipioCodigo, IReadOnlyCollection<string> centraisCodigos, string motivo) => Task.CompletedTask;
    public Task AvisoPublicadoAsync(Guid avisoId, bool oficial, IReadOnlyCollection<string> municipios) => Task.CompletedTask;
    public Task NotificacaoUsuarioAsync(Guid usuarioId, object conteudo) => Task.CompletedTask;
    public Task OfertaAtualizadaAsync(Guid ofertaId, string organizacaoCodigo) => Task.CompletedTask;
}

/// <summary>Catálogo em memória dos limites municipais (carregado do banco; atualizado a partir da APAC).</summary>
public interface ICatalogoMunicipios
{
    IReadOnlyList<Municipio> Listar();
    MunicipioIdentificado? Identificar(PontoGeo ponto);
    Task RecarregarAsync(CancellationToken ct);
}

/// <summary>Consultas espaciais executadas no PostGIS.</summary>
public interface IConsultaEspacial
{
    /// <summary>Locais monitorados ativos, de usuários que consentiram, abrangidos pelo aviso.</summary>
    Task<IReadOnlyList<(Guid LocalId, Guid UsuarioId, string Apelido)>> LocaisAbrangidosPorAvisoAsync(Guid avisoId, CancellationToken ct);
    Task<string?> MunicipioPorPontoAsync(PontoGeo ponto, CancellationToken ct);
}
