using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlertaEmAcao.Modulos.Comum;

/// <summary>Relógio abstrato para permitir testes determinísticos.</summary>
public interface IRelogio
{
    DateTimeOffset Agora { get; }
}

public sealed class RelogioSistema : IRelogio
{
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;
}

/// <summary>Violação de regra de negócio. Mapeada para HTTP 400/409 pela API.</summary>
public class ErroDominio(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
}

public sealed class ErroNaoEncontrado(string mensagem) : ErroDominio("nao_encontrado", mensagem);

public sealed class ErroAcessoNegado(string mensagem) : ErroDominio("acesso_negado", mensagem);

/// <summary>
/// Porta de persistência usada pelos casos de uso. Não expõe tipos do EF Core:
/// a camada de módulos depende apenas de IQueryable (BCL).
/// </summary>
public interface IUnidadeTrabalho
{
    IQueryable<T> Consulta<T>() where T : class;
    void Adicionar<T>(T entidade) where T : class;
    void Remover<T>(T entidade) where T : class;
    Task<List<T>> ListarAsync<T>(IQueryable<T> consulta, CancellationToken ct = default);
    Task<T?> PrimeiroAsync<T>(IQueryable<T> consulta, CancellationToken ct = default);
    Task<int> ContarAsync<T>(IQueryable<T> consulta, CancellationToken ct = default);
    Task<int> SalvarAsync(CancellationToken ct = default);
}

public static class Json
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serializar<T>(T valor) => JsonSerializer.Serialize(valor, Opcoes);
    public static T? Desserializar<T>(string? json) =>
        string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Opcoes);
}

/// <summary>Referência explícita da origem de uma informação exibida ao usuário.</summary>
public sealed record ReferenciaFonte(string Nome, string? Url, DateTimeOffset? AtualizadoEm, bool Oficial, bool Demonstrativa = false);
