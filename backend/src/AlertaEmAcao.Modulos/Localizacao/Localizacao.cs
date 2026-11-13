using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Modulos.Localizacao;

public enum TipoLocal { Residencia, Trabalho, Outro }

/// <summary>Como a posição do local foi obtida. Nunca inventada: CEP sem geocodificação exige escolha no mapa.</summary>
public enum OrigemPosicaoLocal { GpsAtual, CepGeocodificado, EnderecoGeocodificado, EscolhidoNoMapa }

/// <summary>Endereço informado pelo usuário (ou retornado pelo CEP). Cifrado em repouso.</summary>
public sealed record EnderecoLocal(string? Cep, string? Logradouro, string? Numero, string? Complemento, string? Bairro, string? Municipio, string? Uf)
{
    public EnderecoLocal() : this(null, null, null, null, null, null, null) { }
    public static EnderecoLocal Vazio { get; } = new();
    public EnderecoLocal Normalizado() => new(
        Cep is null ? null : SoDigitos(Cep) is { Length: 8 } c ? c : throw new ErroDominio("cep_invalido", "O CEP deve ter 8 dígitos."),
        Curto(Logradouro, 150), Curto(Numero, 20), Curto(Complemento, 80), Curto(Bairro, 100), Curto(Municipio, 100),
        Uf is null ? null : Uf.Trim().ToUpperInvariant() is { Length: 2 } u ? u : throw new ErroDominio("uf_invalida", "UF inválida."));
    public static string SoDigitos(string s) => new(s.Where(char.IsDigit).ToArray());
    private static string? Curto(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(max, s.Trim().Length)];
}

/// <summary>
/// Moradores em números agregados (sem nomes). Dados de crianças e mobilidade: cifrados, nunca públicos,
/// visíveis só ao dono, a familiar com vínculo aprovado e permissão, e a operador do pedido.
/// </summary>
public sealed record MoradoresResidencia(int? Total, int? Criancas, int? Idosos, int? MobilidadeReduzida, int? DependemAssistencia,
    string? ContatoReferencia, string? Observacoes, int? Animais = null)
{
    public MoradoresResidencia() : this(null, null, null, null, null, null, null, null) { }
    public static MoradoresResidencia Vazio { get; } = new();
    public MoradoresResidencia Validado()
    {
        int? N(int? v, string campo) => v is null ? null : v is >= 0 and <= 100 ? v : throw new ErroDominio("moradores_invalidos", $"{campo}: informe de 0 a 100.");
        var t = N(Total, "Moradores");
        var r = new MoradoresResidencia(t, N(Criancas, "Crianças"), N(Idosos, "Idosos"), N(MobilidadeReduzida, "Mobilidade reduzida"),
            N(DependemAssistencia, "Dependem de assistência"),
            string.IsNullOrWhiteSpace(ContatoReferencia) ? null : ContatoReferencia.Trim()[..Math.Min(120, ContatoReferencia.Trim().Length)],
            string.IsNullOrWhiteSpace(Observacoes) ? null : Observacoes.Trim()[..Math.Min(500, Observacoes.Trim().Length)],
            N(Animais, "Animais de estimação"));
        if (t is { } tot && new[] { r.Criancas, r.Idosos, r.MobilidadeReduzida, r.DependemAssistencia }.Any(x => x > tot))
            throw new ErroDominio("moradores_invalidos", "Nenhum grupo pode ser maior que o total de moradores.");
        return r;
    }
}

/// <summary>
/// Local que o usuário pediu para monitorar (ex.: casa, trabalho, casa da mãe) — pode estar longe de onde ele está.
/// Usado para cruzar avisos com quem consentiu e, se for a residência onde ele mora, para vincular familiares autorizados.
/// </summary>
public sealed class LocalMonitorado
{
    public const int LimitePorUsuario = 5;

    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Apelido { get; private set; } = default!;
    public TipoLocal Tipo { get; private set; }
    /// <summary>O dono declara que mora neste local. Só uma residência de moradia por usuário.</summary>
    public bool MoroAqui { get; private set; }
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public string? MunicipioCodigo { get; private set; }
    public OrigemPosicaoLocal Origem { get; private set; }
    public string? DescricaoPrecisao { get; private set; }
    public EnderecoLocal Endereco { get; private set; } = EnderecoLocal.Vazio;
    public MoradoresResidencia Moradores { get; private set; } = MoradoresResidencia.Vazio;
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    private LocalMonitorado() { }

    public static LocalMonitorado Criar(Guid usuarioId, string apelido, PontoGeo ponto, string? municipioCodigo, DateTimeOffset agora,
        TipoLocal tipo = TipoLocal.Outro, OrigemPosicaoLocal origem = OrigemPosicaoLocal.GpsAtual, string? precisao = null,
        EnderecoLocal? endereco = null, bool moroAqui = false)
    {
        var l = new LocalMonitorado { Id = Guid.CreateVersion7(), UsuarioId = usuarioId, Ativo = true, CriadoEm = agora };
        l.Definir(apelido, tipo, moroAqui, ponto, municipioCodigo, origem, precisao, endereco ?? EnderecoLocal.Vazio, agora);
        return l;
    }

    public void Definir(string apelido, TipoLocal tipo, bool moroAqui, PontoGeo ponto, string? municipioCodigo, OrigemPosicaoLocal origem,
        string? precisao, EnderecoLocal endereco, DateTimeOffset agora)
    {
        apelido = (apelido ?? "").Trim();
        if (apelido.Length is < 1 or > 60) throw new ErroDominio("apelido_invalido", "Dê um nome curto ao local (até 60 caracteres).");
        if (municipioCodigo is null)
            throw new ErroDominio("fora_rmr", "Este local não foi identificado em nenhum dos 14 municípios da RMR.");
        if (moroAqui && tipo != TipoLocal.Residencia) throw new ErroDominio("moradia_invalida", "Só uma residência pode ser marcada como onde você mora.");
        Apelido = apelido; Tipo = tipo; MoroAqui = moroAqui;
        Latitude = ponto.Latitude; Longitude = ponto.Longitude; MunicipioCodigo = municipioCodigo;
        Origem = origem;
        DescricaoPrecisao = string.IsNullOrWhiteSpace(precisao) ? null : precisao.Trim()[..Math.Min(120, precisao.Trim().Length)];
        Endereco = endereco.Normalizado();
        AtualizadoEm = agora;
    }

    public void DefinirMoradores(MoradoresResidencia m, DateTimeOffset agora)
    {
        if (Tipo != TipoLocal.Residencia) throw new ErroDominio("nao_residencia", "Moradores só podem ser informados para residências.");
        Moradores = m.Validado();
        AtualizadoEm = agora;
    }

    public void DeixarDeMorar(DateTimeOffset agora) { MoroAqui = false; AtualizadoEm = agora; }

    public PontoGeo Ponto => new(Latitude, Longitude);
    public void Desativar() { Ativo = false; MoroAqui = false; }
}

/// <summary>Endereço retornado por CEP. ViaCEP não traz coordenadas.</summary>
public sealed record EnderecoCep(string Cep, string? Logradouro, string? Bairro, string Municipio, string Uf);

/// <summary>Resultado de geocodificação de fonte real (OpenStreetMap/Nominatim). Precisão descreve o nível encontrado.</summary>
public sealed record ResultadoGeocodificacao(double Latitude, double Longitude, string Descricao, string Precisao, bool ExigeConfirmacaoNoMapa);

/// <summary>Porta para CEP e geocodificação. Implementações na infraestrutura (ViaCEP, Nominatim); o domínio não conhece os serviços.</summary>
public interface IGeocodificador
{
    /// <summary>Null quando o CEP não existe.</summary>
    Task<EnderecoCep?> BuscarCepAsync(string cep, CancellationToken ct);
    /// <summary>Busca explícita (nunca autocompletar). Lista vazia quando não há correspondência confiável.</summary>
    Task<IReadOnlyList<ResultadoGeocodificacao>> BuscarEnderecoAsync(string? logradouro, string? numero, string? bairro, string municipio, string uf, string? cep, CancellationToken ct);
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
