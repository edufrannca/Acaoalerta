using System.Text.Json;
using System.Text.RegularExpressions;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Modulos.Alertas;

public enum OrigemAviso
{
    /// <summary>Aviso meteorológico oficial obtido da API pública do INMET (Alert-AS).</summary>
    Inmet,
    /// <summary>Aviso fictício criado no modo demonstração. NUNCA é apresentado como oficial.</summary>
    Demonstrativo,
}

public sealed class Aviso
{
    public Guid Id { get; private set; }
    public OrigemAviso Origem { get; private set; }
    public string CodigoExterno { get; private set; } = default!;
    public string Titulo { get; private set; } = default!;
    public string Severidade { get; private set; } = default!;
    public string? Cor { get; private set; }
    public DateTimeOffset InicioEm { get; private set; }
    public DateTimeOffset FimEm { get; private set; }
    public string? PoligonoGeoJson { get; private set; }
    public List<string> MunicipiosCodigos { get; private set; } = [];
    public List<string> Riscos { get; private set; } = [];
    public List<string> Instrucoes { get; private set; } = [];
    public string FonteNome { get; private set; } = default!;
    public string? FonteUrl { get; private set; }
    public DateTimeOffset ObtidoEm { get; private set; }
    public bool Processado { get; private set; }
    public DateTimeOffset? ProcessadoEm { get; private set; }

    private Aviso() { }

    public static Aviso Criar(OrigemAviso origem, string codigoExterno, string titulo, string severidade, string? cor,
        DateTimeOffset inicio, DateTimeOffset fim, string? poligonoGeoJson, IEnumerable<string> municipios,
        IEnumerable<string> riscos, IEnumerable<string> instrucoes, string fonteNome, string? fonteUrl, DateTimeOffset agora)
    {
        if (fim <= inicio) throw new ErroDominio("vigencia_invalida", "Fim da vigência deve ser posterior ao início.");
        var codigos = municipios.Where(MunicipiosRmr.Pertence).Distinct().ToList();
        if (codigos.Count == 0 && poligonoGeoJson is null)
            throw new ErroDominio("aviso_sem_area", "Aviso precisa de área (polígono) ou municípios da RMR.");
        if (poligonoGeoJson is not null) LeitorGeoJson.LerGeometria(poligonoGeoJson); // valida
        if (origem == OrigemAviso.Demonstrativo && !titulo.StartsWith("[DEMONSTRAÇÃO]"))
            titulo = "[DEMONSTRAÇÃO] " + titulo;
        return new Aviso
        {
            Id = Guid.CreateVersion7(), Origem = origem, CodigoExterno = codigoExterno, Titulo = titulo.Trim(),
            Severidade = severidade, Cor = cor, InicioEm = inicio, FimEm = fim, PoligonoGeoJson = poligonoGeoJson,
            MunicipiosCodigos = codigos, Riscos = riscos.ToList(), Instrucoes = instrucoes.ToList(),
            FonteNome = fonteNome, FonteUrl = fonteUrl, ObtidoEm = agora,
        };
    }

    public bool Vigente(DateTimeOffset agora) => agora >= InicioEm && agora <= FimEm;
    public bool Oficial => Origem == OrigemAviso.Inmet;

    public bool Abrange(PontoGeo ponto, string? municipioCodigo)
    {
        if (PoligonoGeoJson is not null)
            return LeitorGeoJson.LerGeometria(PoligonoGeoJson).Contem(ponto);
        return municipioCodigo is not null && MunicipiosCodigos.Contains(municipioCodigo);
    }

    public void MarcarProcessado(DateTimeOffset agora) { Processado = true; ProcessadoEm = agora; }
}

/// <summary>
/// Converte o JSON público de https://apiprevmet3.inmet.gov.br/avisos/ativos em avisos de domínio,
/// mantendo somente os que citam municípios da RMR. Formato conferido em 12/11/2026.
/// </summary>
public static partial class ParserAvisosInmet
{
    public const string Url = "https://apiprevmet3.inmet.gov.br/avisos/ativos";
    private static readonly TimeSpan Fuso = TimeSpan.FromHours(-3); // horários do INMET em hora de Brasília

    [GeneratedRegex(@"\((\d{7})\)")]
    private static partial Regex CodigoIbge();

    public static List<Aviso> Interpretar(string json, DateTimeOffset agora)
    {
        using var doc = JsonDocument.Parse(json);
        var resultado = new List<Aviso>();
        foreach (var grupo in new[] { "hoje", "futuro" })
        {
            if (!doc.RootElement.TryGetProperty(grupo, out var lista) || lista.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in lista.EnumerateArray())
            {
                var municipiosTexto = item.TryGetProperty("municipios", out var m) ? m.GetString() ?? "" : "";
                var codigos = CodigoIbge().Matches(municipiosTexto).Select(x => x.Groups[1].Value)
                    .Where(MunicipiosRmr.Pertence).Distinct().ToList();
                if (codigos.Count == 0) continue;

                var inicio = Combinar(Texto(item, "data_inicio"), Texto(item, "hora_inicio"));
                var fim = Combinar(Texto(item, "data_fim"), Texto(item, "hora_fim"));
                if (inicio is null || fim is null || fim <= inicio) continue;

                string? poligono = Texto(item, "poligono");
                try { if (poligono is not null) LeitorGeoJson.LerGeometria(poligono); }
                catch { poligono = null; }

                var codigo = Texto(item, "codigo") ?? $"inmet-{Texto(item, "id")}";
                resultado.Add(Aviso.Criar(OrigemAviso.Inmet, codigo,
                    Texto(item, "descricao") ?? "Aviso meteorológico",
                    Texto(item, "severidade") ?? "Não informada",
                    Texto(item, "aviso_cor"),
                    inicio.Value, fim.Value, poligono, codigos,
                    Lista(item, "riscos"), Lista(item, "instrucoes"),
                    "INMET — Avisos Meteorológicos (Alert-AS)", "https://alertas2.inmet.gov.br/", agora));
            }
        }
        return resultado;
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static List<string> Lista(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).ToList()
            : [];

    private static DateTimeOffset? Combinar(string? data, string? hora)
    {
        if (data is null || hora is null) return null;
        if (!DateTime.TryParse(data[..Math.Min(10, data.Length)], System.Globalization.CultureInfo.InvariantCulture, out var d)) return null;
        if (!TimeSpan.TryParse(hora, System.Globalization.CultureInfo.InvariantCulture, out var h)) return null;
        var segundos = h == new TimeSpan(23, 59, 0) ? 59 : 0;
        return new DateTimeOffset(d.Date + h + TimeSpan.FromSeconds(segundos), Fuso);
    }
}

public interface IFonteAvisosOficiais
{
    Task<(List<Aviso> Avisos, bool Disponivel, string? Erro)> ObterAsync(CancellationToken ct);
}
