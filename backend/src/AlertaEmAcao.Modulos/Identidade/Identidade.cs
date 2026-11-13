using System.Net.Mail;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Municipios;

namespace AlertaEmAcao.Modulos.Identidade;

public enum Papel
{
    Cidadao,
    Voluntario,
    /// <summary>Operador de uma central institucional SIMULADA (demonstração).</summary>
    OperadorCentral,
    /// <summary>Representante de organização de apoio verificada (fictícia na demonstração).</summary>
    Organizacao,
    Administrador,
}

public sealed class Usuario
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Telefone { get; private set; }
    public string SenhaHash { get; private set; } = default!;
    public Papel Papel { get; private set; }
    /// <summary>Para operadores/organizações: código da central simulada vinculada.</summary>
    public string? CentralCodigo { get; private set; }
    public string? MunicipioCodigo { get; private set; }
    /// <summary>Endereço de referência (residência). NÃO é a localização atual.</summary>
    public string? EnderecoReferencia { get; private set; }
    public bool ConsenteAlertas { get; private set; }
    public bool ConsenteLocalizacao { get; private set; }
    public bool ReceberPush { get; private set; }
    public bool ContaDemonstracao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    private Usuario() { }

    public static Usuario Registrar(string nome, string email, string? telefone, string senhaHash, string? municipioCodigo,
        DateTimeOffset agora, Papel papel = Papel.Cidadao, string? centralCodigo = null, bool demonstracao = false)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length is < 2 or > 120) throw new ErroDominio("nome_invalido", "Informe um nome entre 2 e 120 caracteres.");
        email = NormalizarEmail(email);
        if (municipioCodigo is not null && !MunicipiosRmr.Pertence(municipioCodigo))
            throw new ErroDominio("municipio_invalido", "Município não pertence à RMR.");
        if (telefone is not null && telefone.Count(char.IsDigit) is < 10 or > 13)
            throw new ErroDominio("telefone_invalido", "Telefone deve ter DDD e número.");
        if (papel is Papel.OperadorCentral or Papel.Organizacao && string.IsNullOrWhiteSpace(centralCodigo))
            throw new ErroDominio("central_obrigatoria", "Operadores precisam estar vinculados a uma central.");
        return new Usuario
        {
            Id = Guid.CreateVersion7(), Nome = nome, Email = email, Telefone = telefone, SenhaHash = senhaHash,
            MunicipioCodigo = municipioCodigo, Papel = papel, CentralCodigo = centralCodigo,
            CriadoEm = agora, ContaDemonstracao = demonstracao,
        };
    }

    public static string NormalizarEmail(string email)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (email.Length > 200 || !MailAddress.TryCreate(email, out var m) || m.Address != email || !email.Contains('.'))
            throw new ErroDominio("email_invalido", "E-mail inválido.");
        return email;
    }

    public void AtualizarPerfil(string? nome, string? telefone, string? municipioCodigo, string? enderecoReferencia,
        bool consenteAlertas, bool consenteLocalizacao, bool receberPush)
    {
        if (!string.IsNullOrWhiteSpace(nome)) Nome = nome.Trim();
        if (municipioCodigo is not null && !MunicipiosRmr.Pertence(municipioCodigo))
            throw new ErroDominio("municipio_invalido", "Município não pertence à RMR.");
        Telefone = string.IsNullOrWhiteSpace(telefone) ? null : telefone.Trim();
        MunicipioCodigo = municipioCodigo;
        EnderecoReferencia = string.IsNullOrWhiteSpace(enderecoReferencia) ? null : enderecoReferencia.Trim()[..Math.Min(250, enderecoReferencia.Trim().Length)];
        ConsenteAlertas = consenteAlertas;
        ConsenteLocalizacao = consenteLocalizacao;
        ReceberPush = receberPush && consenteAlertas;
    }

    /// <summary>Espelho do consentimento A (o histórico fica em RegistroConsentimento).</summary>
    public void DefinirConsentimentoAlertas(bool concedido)
    {
        ConsenteAlertas = concedido;
        if (!concedido) ReceberPush = false;
    }
}

public interface IHasherSenha
{
    string Gerar(string senha);
    bool Verificar(string senha, string hash);
}

public static class PoliticaSenha
{
    public static void Validar(string senha)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < 8 || senha.Length > 128)
            throw new ErroDominio("senha_fraca", "A senha deve ter entre 8 e 128 caracteres.");
    }
}
