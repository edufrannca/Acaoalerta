using AlertaEmAcao.Modulos.Comum;

namespace AlertaEmAcao.Modulos.Monitoramento;

public enum EstadoLeitura { Atual, Desatualizada, SemLeitura }

/// <summary>Leitura de uma estação pluviométrica (APAC/SIRH, inclui estações CEMADEN e INMET agregadas pela APAC).</summary>
public sealed record LeituraChuva(
    string EstacaoId,
    string Nome,
    double Latitude,
    double Longitude,
    string? Municipio,
    string? CodigoMunicipio,
    DateTimeOffset? UltimaLeituraEm,
    double? Mm1h,
    double? Mm3h,
    double? Mm6h,
    double? Mm12h,
    double? Mm24h,
    double? Mm48h,
    double? Mm72h,
    string Fonte)
{
    public static readonly TimeSpan LimiteAtualidade = TimeSpan.FromHours(2);

    public EstadoLeitura Estado(DateTimeOffset agora) =>
        UltimaLeituraEm is null ? EstadoLeitura.SemLeitura
        : agora - UltimaLeituraEm.Value > LimiteAtualidade ? EstadoLeitura.Desatualizada
        : EstadoLeitura.Atual;
}

public sealed record ResultadoFonteChuva(IReadOnlyList<LeituraChuva> Leituras, DateTimeOffset ConsultadoEm, bool Disponivel, string? Erro, string Fonte, string Url);

public interface IFonteChuva
{
    Task<ResultadoFonteChuva> ObterRmrAsync(CancellationToken ct);
}

/// <summary>
/// Interpreta valores da APAC: -1 significa "sem dado"; data 1900-01-01 significa "nunca leu".
/// A APAC publica horário local de Recife (UTC-3) sem fuso.
/// </summary>
public static class InterpretadorApac
{
    public static readonly TimeSpan FusoRecife = TimeSpan.FromHours(-3);

    public static double? Milimetros(double? valor) => valor is null or < 0 ? null : Math.Round(valor.Value, 2);

    public static DateTimeOffset? DataHora(string? campo)
    {
        if (string.IsNullOrWhiteSpace(campo)) return null;
        // formato observado: "2026-11-12 21:10:00 21:10:00" (hora repetida)
        var partes = campo.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length < 2) return null;
        if (!DateTime.TryParseExact($"{partes[0]} {partes[1]}", "yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
            return null;
        if (dt.Year < 2000) return null;
        return new DateTimeOffset(dt, FusoRecife);
    }
}
