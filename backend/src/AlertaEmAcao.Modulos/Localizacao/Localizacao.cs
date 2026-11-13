using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Modulos.Localizacao;

/// <summary>
/// Local que o usuário pediu para monitorar (ex.: casa, trabalho, casa da mãe).
/// Usado para cruzar avisos oficiais com quem consentiu em recebê-los.
/// </summary>
public sealed class LocalMonitorado
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Apelido { get; private set; } = default!;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string? MunicipioCodigo { get; private set; }
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    private LocalMonitorado() { }

    public static LocalMonitorado Criar(Guid usuarioId, string apelido, PontoGeo ponto, string? municipioCodigo, DateTimeOffset agora)
    {
        apelido = (apelido ?? "").Trim();
        if (apelido.Length is < 1 or > 60) throw new ErroDominio("apelido_invalido", "Dê um nome curto ao local (até 60 caracteres).");
        if (municipioCodigo is null)
            throw new ErroDominio("fora_rmr", "Este local não foi identificado em nenhum dos 14 municípios da RMR.");
        return new LocalMonitorado
        {
            Id = Guid.CreateVersion7(), UsuarioId = usuarioId, Apelido = apelido,
            Latitude = ponto.Latitude, Longitude = ponto.Longitude, MunicipioCodigo = municipioCodigo,
            Ativo = true, CriadoEm = agora,
        };
    }

    public PontoGeo Ponto => new(Latitude, Longitude);
    public void Desativar() => Ativo = false;
}

/// <summary>Posição informada pelo dispositivo, com precisão e horário reais da leitura.</summary>
public sealed record PosicaoDispositivo(double Latitude, double Longitude, double? PrecisaoMetros, DateTimeOffset LidaEm, string Origem)
{
    public PontoGeo Ponto => new(Latitude, Longitude);
    public bool Imprecisa => PrecisaoMetros is null or > 500;
}

/// <summary>
/// Controle de frequência das atualizações de localização de uma ocorrência
/// (proteção de bateria e de banda). Regra aplicada também no cliente.
/// </summary>
public static class PoliticaAtualizacaoLocalizacao
{
    public static readonly TimeSpan IntervaloMinimo = TimeSpan.FromSeconds(15);
    public const double DeslocamentoMinimoMetros = 15;

    public static bool DeveAceitar(PosicaoDispositivo? ultima, PosicaoDispositivo nova)
    {
        if (ultima is null) return true;
        if (nova.LidaEm <= ultima.LidaEm) return false;
        var tempo = nova.LidaEm - ultima.LidaEm;
        var distancia = ultima.Ponto.DistanciaMetros(nova.Ponto);
        if (tempo < IntervaloMinimo) return false;
        // Mesmo parado, aceita um "batimento" a cada 2 minutos para registrar a última posição conhecida.
        return distancia >= DeslocamentoMinimoMetros || tempo >= TimeSpan.FromMinutes(2)
            || (nova.PrecisaoMetros ?? double.MaxValue) < (ultima.PrecisaoMetros ?? double.MaxValue) / 2;
    }
}
