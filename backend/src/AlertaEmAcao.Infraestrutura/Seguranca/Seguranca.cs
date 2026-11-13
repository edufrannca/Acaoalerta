using System.Security.Cryptography;
using AlertaEmAcao.Modulos.Identidade;

namespace AlertaEmAcao.Infraestrutura.Seguranca;

/// <summary>PBKDF2-SHA256, 210.000 iterações (recomendação OWASP 2023+), sal aleatório de 16 bytes.</summary>
public sealed class HasherSenhaPbkdf2 : IHasherSenha
{
    private const int Iteracoes = 210_000;

    public string Gerar(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, Iteracoes, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iteracoes}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string senha, string armazenado)
    {
        var p = armazenado.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2-sha256" || !int.TryParse(p[1], out var it)) return false;
        var sal = Convert.FromBase64String(p[2]);
        var esperado = Convert.FromBase64String(p[3]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, it, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(hash, esperado);
    }
}
