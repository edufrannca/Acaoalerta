using System.Globalization;
using System.Text;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Modulos.AnaliseSituacional;

/// <summary>Categorias de indício usadas pelo roteamento e pela análise.</summary>
public enum Indicio
{
    RiscoHidrologico,
    RiscoGeologico,
    PessoaFerida,
    NecessidadeMedica,
    SegurancaPublica,
    Vulnerabilidade,
    PessoaIsolada,
    AcessoBloqueado,
}

public sealed record Evidencia(string Campo, string Trecho);

public sealed record IndicioDetectado(Indicio Tipo, IReadOnlyList<Evidencia> Evidencias);

/// <summary>
/// Detecção determinística de indícios a partir de campos estruturados e palavras-chave do relato.
/// Transparente e auditável: toda detecção carrega a evidência que a originou.
/// </summary>
public static class DetectorIndicios
{
    private static readonly (Indicio Tipo, string[] Termos)[] Lexico =
    [
        (Indicio.RiscoHidrologico, ["agua subindo", "agua esta subindo", "alagad", "alagand", "alagou", "alagament", "enchendo de agua", "inundad", "inundac", "correnteza", "enchente", "transbord", "ilhad", "agua na cintura", "agua no peito", "agua entrando"]),
        (Indicio.RiscoGeologico, ["desliz", "barreira", "barranco", "encosta", "desmoron", "desab", "rachadura", "trinca", "terra descendo", "muro caiu", "muro caindo"]),
        (Indicio.PessoaFerida, ["ferid", "sangr", "machucad", "inconscient", "desmaiad", "fratura", "quebrou a perna", "nao respira", "soterrad"]),
        (Indicio.NecessidadeMedica, ["oxigenio", "dialise", "insulina", "aparelho respirat", "cadeira de rodas", "acamad", "gravida", "gestante", "remedio controlado", "marcapasso"]),
        (Indicio.SegurancaPublica, ["assalt", "arma", "tiro", "violenc", "saque", "ameaca", "agressao"]),
        (Indicio.PessoaIsolada, ["sozinh", "isolad", "presa em casa", "preso em casa", "nao consigo sair", "sem saida", "no telhado", "na laje"]),
        (Indicio.Vulnerabilidade, ["nao anda", "cadeirante", "acamad", "um bebe", "meu bebe", "o bebe", "bebe de colo", "recem-nascid", "recem nascid", "deficiencia", "deficiente"]),
        (Indicio.AcessoBloqueado, ["rua bloqueada", "via bloqueada", "acesso bloqueado", "arvore caiu", "arvore caida", "sem acesso", "ponte caiu", "estrada interditada", "rua interditada"]),
    ];

    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var decomposto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<IndicioDetectado> Detectar(ComplementoOcorrencia c)
    {
        var mapa = new Dictionary<Indicio, List<Evidencia>>();
        void Add(Indicio i, string campo, string trecho)
        {
            if (!mapa.TryGetValue(i, out var l)) mapa[i] = l = [];
            l.Add(new Evidencia(campo, trecho));
        }

        if (c.AguaSubindo == true) Add(Indicio.RiscoHidrologico, "aguaSubindo", "Água subindo: sim");
        if (c.Correnteza == true) Add(Indicio.RiscoHidrologico, "correnteza", "Correnteza: sim");
        if (c.Ilhado == true) { Add(Indicio.RiscoHidrologico, "ilhado", "Situação de ilhamento: sim"); Add(Indicio.PessoaIsolada, "ilhado", "Situação de ilhamento: sim"); }
        if (c.RiscoDeslizamento == true) Add(Indicio.RiscoGeologico, "riscoDeslizamento", "Risco de deslizamento: sim");
        if (c.Desmoronamento == true) Add(Indicio.RiscoGeologico, "desmoronamento", "Desmoronamento: sim");
        if (c.BarreiraComSinaisInstabilidade == true) Add(Indicio.RiscoGeologico, "barreiraComSinaisInstabilidade", "Barreira com sinais de instabilidade: sim");
        if (c.PessoasFeridas is > 0) Add(Indicio.PessoaFerida, "pessoasFeridas", $"Pessoas feridas: {c.PessoasFeridas}");
        if (c.Pessoas?.Any(p => p.Ferida) == true) Add(Indicio.PessoaFerida, "pessoas", "Pessoa detalhada marcada como ferida");
        if (c.EquipamentoMedicoEssencial == true) Add(Indicio.NecessidadeMedica, "equipamentoMedicoEssencial", "Equipamento médico essencial: sim");
        if (c.RiscoSegurancaPublica == true) Add(Indicio.SegurancaPublica, "riscoSegurancaPublica", "Risco à segurança pública: sim");
        if (c.Sozinho == true) Add(Indicio.PessoaIsolada, "sozinho", "Pessoa sozinha: sim");
        if (c.AcessoBloqueado == true) Add(Indicio.AcessoBloqueado, "acessoBloqueado", "Acesso bloqueado: sim");

        var vulnerabilidades = new List<Evidencia>();
        if (c.Criancas is > 0) vulnerabilidades.Add(new("criancas", $"Crianças: {c.Criancas}"));
        if (c.Idosos is > 0) vulnerabilidades.Add(new("idosos", $"Pessoas idosas: {c.Idosos}"));
        if (c.MobilidadeReduzida == true) vulnerabilidades.Add(new("mobilidadeReduzida", "Mobilidade reduzida: sim"));
        if (c.Pessoas is { } pessoas)
        {
            if (pessoas.Any(p => p.Idade is < 12)) vulnerabilidades.Add(new("pessoas", "Criança entre as pessoas detalhadas"));
            if (pessoas.Any(p => p.Idade is >= 60)) vulnerabilidades.Add(new("pessoas", "Pessoa idosa entre as pessoas detalhadas"));
            if (pessoas.Any(p => p.MobilidadeReduzida)) vulnerabilidades.Add(new("pessoas", "Pessoa com mobilidade reduzida"));
            if (pessoas.Any(p => p.DependeAssistencia)) vulnerabilidades.Add(new("pessoas", "Pessoa que depende de assistência"));
        }
        foreach (var v in vulnerabilidades) Add(Indicio.Vulnerabilidade, v.Campo, v.Trecho);

        foreach (var (campo, texto) in new[] { ("descricao", c.Descricao), ("outrosRiscos", c.OutrosRiscos), ("referenciaLocal", c.ReferenciaLocal) })
        {
            var norm = Normalizar(texto);
            if (norm.Length == 0) continue;
            foreach (var (tipo, termos) in Lexico)
                foreach (var termo in termos)
                {
                    var idx = norm.IndexOf(termo, StringComparison.Ordinal);
                    if (idx < 0) continue;
                    Add(tipo, campo, Recortar(texto!, idx, termo.Length));
                    break;
                }
        }

        return mapa.Select(kv => new IndicioDetectado(kv.Key, kv.Value)).OrderBy(i => i.Tipo).ToList();
    }

    private static string Recortar(string original, int idx, int len)
    {
        var ini = Math.Max(0, idx - 20);
        var fim = Math.Min(original.Length, idx + len + 30);
        return (ini > 0 ? "…" : "") + original[ini..fim].Trim() + (fim < original.Length ? "…" : "");
    }
}

public enum NivelSinalizacao
{
    /// <summary>Há indício de ameaça imediata à vida — exige avaliação humana prioritária.</summary>
    IndicioAmeacaImediata,
    Atencao,
    Informativo,
}

public sealed record Sinalizacao(string Codigo, NivelSinalizacao Nivel, string Titulo, string Explicacao, IReadOnlyList<Evidencia> Evidencias);

public sealed record ContextoTerritorialResumo(
    bool AvisoOficialVigente,
    string? DescricaoAviso,
    bool SuscetibilidadeConhecida,
    string? DescricaoSuscetibilidade,
    double? ChuvaUltimas24hMm,
    DateTimeOffset? ChuvaLidaEm,
    bool DadosMeteorologicosDesatualizados,
    int OcorrenciasRelatadasProximas);

/// <summary>
/// Regras determinísticas e transparentes. Funcionam sem IA. NÃO ordenam vidas nem decidem
/// atendimento: produzem sinalizações explicadas para avaliação humana. Idade isolada nunca
/// eleva sinalização a "ameaça imediata".
/// </summary>
public static class MotorRegrasUrgencia
{
    public const string Versao = "regras-2026.11.1";

    public static IReadOnlyList<Sinalizacao> Avaliar(Ocorrencia o, ContextoTerritorialResumo? contexto, DateTimeOffset agora)
    {
        var c = o.Complemento;
        var indicios = DetectorIndicios.Detectar(c).ToDictionary(i => i.Tipo, i => i.Evidencias);
        var s = new List<Sinalizacao>();
        IReadOnlyList<Evidencia> Ev(Indicio i) => indicios.TryGetValue(i, out var e) ? e : [];

        var hidro = Ev(Indicio.RiscoHidrologico);
        var aguaAtiva = c.AguaSubindo == true || c.Correnteza == true || hidro.Any(e => e.Campo != "ilhado" && DetectorIndicios.Normalizar(e.Trecho).Contains("subindo"));
        if (c.Ilhado == true && aguaAtiva)
            s.Add(new("AGUA_SUBINDO_COM_ILHAMENTO", NivelSinalizacao.IndicioAmeacaImediata, "Ilhamento com água subindo",
                "Pessoas ilhadas com elevação ou correnteza de água: indício de ameaça imediata à vida.", hidro));
        else if (aguaAtiva)
            s.Add(new("AGUA_SUBINDO", NivelSinalizacao.IndicioAmeacaImediata, "Água subindo ou correnteza",
                "Relato de elevação rápida do nível da água ou correnteza.", hidro));
        else if (hidro.Count > 0)
            s.Add(new("RISCO_HIDROLOGICO", NivelSinalizacao.Atencao, "Alagamento ou inundação relatada", "Relato de alagamento/inundação sem indicação de elevação rápida.", hidro));

        var geo = Ev(Indicio.RiscoGeologico);
        if (c.Desmoronamento == true || geo.Any(e => DetectorIndicios.Normalizar(e.Trecho) is var t && (t.Contains("desab") || t.Contains("desmoron") || t.Contains("soterr"))))
            s.Add(new("DESABAMENTO", NivelSinalizacao.IndicioAmeacaImediata, "Desabamento/desmoronamento relatado", "Relato de estrutura ou encosta que já cedeu.", geo));
        else if (c.RiscoDeslizamento == true || c.BarreiraComSinaisInstabilidade == true || geo.Count > 0)
            s.Add(new("RISCO_GEOLOGICO", NivelSinalizacao.Atencao, "Risco de deslizamento", "Relato de barreira/encosta com sinais de instabilidade ou deslizamento.", geo));

        var ferida = Ev(Indicio.PessoaFerida);
        if (ferida.Count > 0)
            s.Add(new("PESSOA_FERIDA", NivelSinalizacao.IndicioAmeacaImediata, "Pessoa ferida", "Relato de pessoa ferida, inconsciente ou soterrada.", ferida));

        var medica = Ev(Indicio.NecessidadeMedica);
        if (medica.Count > 0)
            s.Add(new("NECESSIDADE_MEDICA", NivelSinalizacao.Atencao, "Dependência de equipamento ou cuidado médico", "Pode exigir equipe de saúde ou energia elétrica.", medica));

        var vuln = Ev(Indicio.Vulnerabilidade);
        if (vuln.Count > 0)
            s.Add(new("NECESSIDADE_ASSISTENCIA", NivelSinalizacao.Atencao, "Pessoas que podem precisar de assistência na retirada",
                "Crianças, pessoas idosas, com mobilidade reduzida ou dependentes de assistência. Informação para planejamento, não critério isolado de prioridade.", vuln));

        if (Ev(Indicio.PessoaIsolada) is { Count: > 0 } iso)
            s.Add(new("PESSOA_ISOLADA", NivelSinalizacao.Atencao, "Pessoa sozinha ou isolada", "Pode não conseguir sair sem ajuda.", iso));
        if (Ev(Indicio.AcessoBloqueado) is { Count: > 0 } ac)
            s.Add(new("ACESSO_BLOQUEADO", NivelSinalizacao.Atencao, "Acesso bloqueado", "Equipes podem precisar de rota alternativa.", ac));
        if (Ev(Indicio.SegurancaPublica) is { Count: > 0 } seg)
            s.Add(new("SEGURANCA_PUBLICA", NivelSinalizacao.Atencao, "Risco à segurança pública relatado", "Pode exigir apoio policial.", seg));

        if (o.Ponto is null)
            s.Add(new("LOCALIZACAO_DESCONHECIDA", NivelSinalizacao.Atencao, "Localização desconhecida",
                "O aparelho não enviou coordenadas. Confirmar referência por telefone.",
                c.ReferenciaLocal is null ? [] : [new("referenciaLocal", c.ReferenciaLocal)]));
        else if (o.PrecisaoMetros is null or > 500)
            s.Add(new("LOCALIZACAO_IMPRECISA", NivelSinalizacao.Informativo, "Localização imprecisa",
                $"Precisão informada: {(o.PrecisaoMetros is null ? "desconhecida" : $"{o.PrecisaoMetros:0} m")}.", []));
        if (o.LocalizacaoLidaEm is { } lida && agora - lida > TimeSpan.FromMinutes(30))
            s.Add(new("LOCALIZACAO_ANTIGA", NivelSinalizacao.Informativo, "Última posição tem mais de 30 minutos",
                $"Última leitura em {lida:dd/MM HH:mm} (UTC). A pessoa pode ter se deslocado.", []));

        // Contradições
        var contradicoes = new List<Evidencia>();
        if (c.QuantidadePessoas is { } qtd && c.Pessoas is { Count: > 0 } lista && lista.Count > qtd)
            contradicoes.Add(new("quantidadePessoas", $"Informou {qtd} pessoa(s), mas detalhou {lista.Count}."));
        if (c.Sozinho == true && c.QuantidadePessoas is > 1)
            contradicoes.Add(new("sozinho", $"Marcou 'sozinho' e informou {c.QuantidadePessoas} pessoas."));
        if (c.QuantidadePessoas is { } q2 && (c.PessoasFeridas ?? 0) > q2)
            contradicoes.Add(new("pessoasFeridas", $"Feridos ({c.PessoasFeridas}) acima do total de pessoas ({q2})."));
        if (c.QuantidadePessoas is { } q3 && (c.Criancas ?? 0) + (c.Idosos ?? 0) > q3)
            contradicoes.Add(new("criancas", $"Crianças + idosos ({(c.Criancas ?? 0) + (c.Idosos ?? 0)}) acima do total ({q3})."));
        if (contradicoes.Count > 0)
            s.Add(new("INFORMACOES_CONTRADITORIAS", NivelSinalizacao.Atencao, "Informações contraditórias", "Confirmar com o solicitante.", contradicoes));

        var ausentes = new List<string>();
        if (c.QuantidadePessoas is null && c.Pessoas is null) ausentes.Add("quantidade de pessoas");
        if (string.IsNullOrWhiteSpace(c.Descricao) && indicios.Count == 0) ausentes.Add("descrição do que está acontecendo");
        if (ausentes.Count > 0)
            s.Add(new("RELATO_INCOMPLETO", NivelSinalizacao.Informativo, "Relato incompleto", "Ausente: " + string.Join(", ", ausentes) + ".", []));

        if (contexto is not null)
        {
            if (contexto.AvisoOficialVigente)
                s.Add(new("AVISO_OFICIAL_VIGENTE", NivelSinalizacao.Informativo, "Aviso vigente na área", contexto.DescricaoAviso ?? "", []));
            if (contexto.SuscetibilidadeConhecida)
                s.Add(new("SUSCETIBILIDADE_CONHECIDA", NivelSinalizacao.Informativo, "Área com suscetibilidade documentada", contexto.DescricaoSuscetibilidade ?? "", []));
            if (contexto.DadosMeteorologicosDesatualizados)
                s.Add(new("DADOS_METEOROLOGICOS_DESATUALIZADOS", NivelSinalizacao.Informativo, "Dados meteorológicos desatualizados",
                    contexto.ChuvaLidaEm is null ? "Sem leitura recente de estação próxima." : $"Leitura mais recente em {contexto.ChuvaLidaEm:dd/MM HH:mm}.", []));
            if (contexto.OcorrenciasRelatadasProximas > 0)
                s.Add(new("OCORRENCIAS_PROXIMAS", NivelSinalizacao.Informativo, "Outros relatos próximos",
                    $"{contexto.OcorrenciasRelatadasProximas} relato(s) num raio de 1 km nas últimas 6 h (não verificados).", []));
        }
        return s;
    }

    public static NivelSinalizacao? NivelMaximo(IEnumerable<Sinalizacao> s) =>
        s.Select(x => (NivelSinalizacao?)x.Nivel).OrderBy(n => n).FirstOrDefault();
}
