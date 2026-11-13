using AlertaEmAcao.Modulos.Comum;

namespace AlertaEmAcao.Modulos.Ocorrencias;

/// <summary>
/// Representação pública de um pedido: área aproximada que CONTÉM a posição registrada.
/// O centro é o centro de uma célula de grade (~220 m) — fica a no máximo ~160 m do ponto, mas não é a posição da pessoa.
/// </summary>
public readonly record struct AreaAproximada(double CentroLatitude, double CentroLongitude, int RaioMetros)
{
    public const double PassoGraus = 0.002;
    /// <summary>Meia diagonal da célula na RMR (~8° S): a maior distância possível entre o centro e um ponto dentro dela.</summary>
    public const double MeiaDiagonalMetros = 160;
    public const int RaioMinimoMetros = 400;

    public static AreaAproximada De(PontoGeo real, double? precisaoMetros)
    {
        var c = real.Aproximar(PassoGraus);
        var precisao = Math.Clamp(precisaoMetros ?? 0, 0, 3000);
        var raio = Math.Max(RaioMinimoMetros, MeiaDiagonalMetros + precisao);
        raio = Math.Ceiling(raio / 100) * 100;
        return new AreaAproximada(Math.Round(c.Latitude, 4), Math.Round(c.Longitude, 4), (int)raio);
    }

    public PontoGeo Centro => new(CentroLatitude, CentroLongitude);
    public bool Contem(PontoGeo p) => Centro.DistanciaMetros(p) <= RaioMetros;
}

/// <summary>Comparação de duas leituras de GPS considerando as margens de precisão de cada uma.</summary>
public sealed record ComparacaoPosicoes(int DistanciaMetros, int MargemMetros, bool Compativeis, string Descricao)
{
    /// <summary>Folga fixa para erros de arredondamento e deslocamento entre as leituras.</summary>
    public const double FolgaMetros = 10;

    public static ComparacaoPosicoes Comparar(PontoGeo a, double? precisaoA, PontoGeo b, double? precisaoB)
    {
        var d = a.DistanciaMetros(b);
        // Sem precisão informada, assume 50 m (valor conservador para GPS de celular em área urbana).
        var margem = (precisaoA ?? 50) + (precisaoB ?? 50) + FolgaMetros;
        var ok = d <= margem;
        return new ComparacaoPosicoes((int)Math.Round(d), (int)Math.Round(margem), ok, ok
            ? $"Leituras compatíveis: {Math.Round(d)} m de distância, dentro da margem combinada de {Math.Round(margem)} m."
            : $"Leituras distantes: {Math.Round(d)} m, acima da margem combinada de {Math.Round(margem)} m. Não trate como o mesmo local.");
    }
}

public static class ResumoComunitario
{
    public static IReadOnlyList<string> TiposEmergencia(ComplementoOcorrencia c)
    {
        var t = new List<string>();
        if (c.Ilhado == true || c.AguaSubindo == true || c.Correnteza == true) t.Add("Alagamento ou pessoas ilhadas");
        if (c.RiscoDeslizamento == true || c.BarreiraComSinaisInstabilidade == true) t.Add("Deslizamento ou risco de deslizamento");
        if (c.Desmoronamento == true) t.Add("Desabamento");
        if (c.AcessoBloqueado == true) t.Add("Acesso bloqueado");
        // Risco à segurança pública e dados médicos não são detalhados publicamente.
        if (t.Count == 0) t.Add("Pedido de socorro (tipo não informado)");
        return t;
    }

    public static int? Criancas(ComplementoOcorrencia c) => c.Criancas ?? Contar(c, p => p.Idade is < 13);
    public static int? Idosos(ComplementoOcorrencia c) => c.Idosos ?? Contar(c, p => p.Idade is >= 60);
    public static int? Mobilidade(ComplementoOcorrencia c) =>
        Contar(c, p => p.MobilidadeReduzida || p.DependeAssistencia) is > 0 and var n ? n : c.MobilidadeReduzida == true ? 1 : null;

    private static int? Contar(ComplementoOcorrencia c, Func<PessoaEnvolvida, bool> f) => c.Pessoas is { Count: > 0 } ps ? ps.Count(f) : null;

    /// <summary>Faixas em vez de números exatos para grupos vulneráveis.</summary>
    public static string? Faixa(int? n) => n switch
    {
        null or <= 0 => null,
        1 => "1",
        <= 4 => "2 a 4",
        _ => "5 ou mais",
    };

    public static string Situacao(StatusOcorrencia s) => s switch
    {
        StatusOcorrencia.ConfirmadaParcialmente or StatusOcorrencia.ConfirmadaPorTodas => "A equipe de teste (simulada) recebeu",
        StatusOcorrencia.EmAtendimentoSimulado => "Em atendimento (simulado)",
        StatusOcorrencia.Encerrada => "Encerrado",
        _ => "Pedido registrado, aguardando a confirmação da equipe",
    };

    public static string Verificacao(VerificacaoOcorrencia v) => v switch
    {
        VerificacaoOcorrencia.VerificadaPorOperador => "Verificado pela equipe",
        VerificacaoOcorrencia.DescartadaPorOperador => "Descartado pela equipe",
        _ => "Não verificado",
    };

    /// <summary>Horário arredondado para baixo em blocos de 15 minutos.</summary>
    public static DateTimeOffset HorarioAproximado(DateTimeOffset t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute / 15 * 15, 0, t.Offset);

    /// <summary>Mínimo de pedidos para exibir totais regionais (proteção contra reidentificação).</summary>
    public const int MinimoParaAgregar = 3;
}
