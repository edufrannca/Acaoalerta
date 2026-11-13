using AlertaEmAcao.Modulos.Comum;

namespace AlertaEmAcao.Modulos.Abrigos;

public enum Acessibilidade { NaoInformada, Possui, Inexistente }

public sealed class Abrigo
{
    public Guid Id { get; private set; }
    public string IdExterno { get; private set; } = default!;
    public string Nome { get; private set; } = default!;
    public string? Tipo { get; private set; }
    public string? Endereco { get; private set; }
    public string MunicipioCodigo { get; private set; } = default!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public Acessibilidade Acessibilidade { get; private set; }
    public string Fonte { get; private set; } = default!;
    public string? FonteUrl { get; private set; }
    /// <summary>Status conforme a fonte (ex.: "ATIVO" na base do COP Recife). Não equivale a ter vagas.</summary>
    public string? StatusFonte { get; private set; }
    public string? EstagioOperacional { get; private set; }
    public DateTimeOffset? AtualizadoNaFonteEm { get; private set; }
    public DateTimeOffset ObtidoEm { get; private set; }
    public bool DisponibilidadeVerificada { get; private set; }
    public bool Ficticio { get; private set; }

    private Abrigo() { }

    public static Abrigo Criar(string idExterno, string nome, string? tipo, string? endereco, string municipio, PontoGeo p,
        Acessibilidade acess, string fonte, string? url, string? statusFonte, string? estagio, DateTimeOffset? atualizadoFonte,
        DateTimeOffset agora, bool ficticio)
    {
        if (ficticio && !nome.StartsWith("[FICTÍCIO]")) nome = "[FICTÍCIO] " + nome;
        return new Abrigo
        {
            Id = Guid.CreateVersion7(), IdExterno = idExterno, Nome = nome, Tipo = tipo, Endereco = endereco, MunicipioCodigo = municipio,
            Latitude = p.Latitude, Longitude = p.Longitude, Acessibilidade = acess, Fonte = fonte, FonteUrl = url, StatusFonte = statusFonte,
            EstagioOperacional = estagio, AtualizadoNaFonteEm = atualizadoFonte, ObtidoEm = agora, Ficticio = ficticio,
            DisponibilidadeVerificada = false,
        };
    }

    /// <summary>Texto honesto sobre disponibilidade. Constar em base histórica não significa ter vagas.</summary>
    public string DescricaoDisponibilidade =>
        DisponibilidadeVerificada ? "Disponibilidade confirmada pela fonte" : "Disponibilidade não verificada";
}

public interface IFonteAbrigos
{
    Task<(List<Abrigo> Abrigos, bool Disponivel, string? Erro)> ObterAsync(CancellationToken ct);
}
