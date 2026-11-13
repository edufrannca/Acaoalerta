using AlertaEmAcao.Modulos.AnaliseSituacional;
using AlertaEmAcao.Modulos.Alertas;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.Monitoramento;
using AlertaEmAcao.Modulos.Ocorrencias;

namespace AlertaEmAcao.Modulos.InteligenciaTerritorial;

public sealed record EstadoFonte(string Nome, bool Disponivel, DateTimeOffset? UltimaConsultaEm, DateTimeOffset? UltimoSucessoEm, string? UltimoErro, int? Registros, string? Url);

/// <summary>Registro do estado de cada fonte externa (disponibilidade, último sucesso, erro).</summary>
public interface IRegistroFontes
{
    void Registrar(string nome, bool sucesso, string? erro, int? registros, string? url, DateTimeOffset em);
    IReadOnlyList<EstadoFonte> Listar();
    EstadoFonte? Obter(string nome);
}

public static class NomesFontes
{
    public const string ApacChuva = "APAC — Monitoramento de chuvas (SIRH)";
    public const string ApacLimites = "APAC — Limite municipal";
    public const string InmetAvisos = "INMET — Avisos meteorológicos";
    public const string RecifeAreasRisco = "Prefeitura do Recife — Áreas de risco (Dados Abertos)";
    public const string CopRecifeAbrigos = "COP Recife — Abrigos";
}

public sealed class ServicoTerritorio(IUnidadeTrabalho uow, ICatalogoMunicipios catalogo, IFonteChuva fonteChuva, IRegistroFontes fontes, IRelogio relogio)
{
    public async Task<ContextoTerritorial> AvaliarAsync(PontoGeo ponto, CancellationToken ct, Guid? ignorarOcorrencia = null)
    {
        var agora = relogio.Agora;
        var mun = catalogo.Identificar(ponto);
        const double d = 0.004; // ~440 m — pré-filtro por envelope; distância exata no domínio
        var areas = mun is null ? [] : await uow.ListarAsync(uow.Consulta<AreaRiscoDocumentada>()
            .Where(a => a.MunicipioCodigo == mun.Codigo && a.Latitude > ponto.Latitude - d && a.Latitude < ponto.Latitude + d
                        && a.Longitude > ponto.Longitude - d && a.Longitude < ponto.Longitude + d), ct);
        var avisos = await uow.ListarAsync(uow.Consulta<Aviso>().Where(a => a.FimEm >= agora && a.InicioEm <= agora), ct);
        var chuva = await fonteChuva.ObterRmrAsync(ct);
        var desde = agora.AddHours(-6);
        const double r = 0.012;
        var ocorrencias = await uow.ListarAsync(uow.Consulta<Ocorrencia>()
            .Where(o => o.RecebidaNoServidorEm >= desde && o.Latitude != null && o.Id != ignorarOcorrencia
                        && o.Latitude > ponto.Latitude - r && o.Latitude < ponto.Latitude + r
                        && o.Longitude > ponto.Longitude - r && o.Longitude < ponto.Longitude + r
                        && o.Verificacao != VerificacaoOcorrencia.DescartadaPorOperador)
            .Select(o => new OcorrenciaProxima(o.Latitude!.Value, o.Longitude!.Value, o.Verificacao == VerificacaoOcorrencia.VerificadaPorOperador, o.RecebidaNoServidorEm)), ct);
        var fonteAreas = fontes.Obter(NomesFontes.RecifeAreasRisco);
        return AvaliadorContextoTerritorial.Avaliar(ponto, mun?.Codigo, mun?.Nome, areas, avisos, chuva.Leituras, ocorrencias,
            fonteAreas?.UltimoSucessoEm is not null, chuva.Disponivel, agora);
    }

    public static ContextoTerritorialResumo Resumir(ContextoTerritorial c, DateTimeOffset agora)
    {
        var aviso = c.Estados.FirstOrDefault(e => e.Tipo is TipoEstadoInformativo.AvisoOficialVigente or TipoEstadoInformativo.AvisoDemonstrativoVigente);
        var susc = c.Estados.FirstOrDefault(e => e.Tipo == TipoEstadoInformativo.SuscetibilidadeConhecida);
        var desatualizado = c.EstacaoMaisProxima is null || c.EstacaoMaisProxima.Estado(agora) != EstadoLeitura.Atual;
        var relatos = c.Estados.Where(e => e.Tipo is TipoEstadoInformativo.OcorrenciaRelatada or TipoEstadoInformativo.OcorrenciaVerificada)
            .Sum(e => int.TryParse(e.Detalhe.Split(' ')[0], out var n) ? n : 0);
        return new ContextoTerritorialResumo(aviso is not null, aviso?.Detalhe, susc is not null, susc?.Detalhe,
            c.EstacaoMaisProxima?.Mm24h, c.EstacaoMaisProxima?.UltimaLeituraEm, desatualizado, relatos);
    }

    public static IEnumerable<ReferenciaFonte> Fontes(ContextoTerritorial c) =>
        c.Estados.Where(e => e.Fonte is not null).Select(e => e.Fonte!)
            .Concat(c.EstacaoMaisProxima is { } l ? [new ReferenciaFonte(l.Fonte, null, l.UltimaLeituraEm, true)] : [])
            .DistinctBy(f => f.Nome);
}
