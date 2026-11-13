using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlertaEmAcao.Modulos.Notificacoes;
using Microsoft.Extensions.Options;

namespace AlertaEmAcao.Infraestrutura.Adaptadores;

public sealed class OpcoesWebPush
{
    /// <summary>Chave pública VAPID (P-256, ponto não comprimido, base64url).</summary>
    public string? ChavePublica { get; set; }
    /// <summary>Chave privada VAPID (escalar d de 32 bytes, base64url).</summary>
    public string? ChavePrivada { get; set; }
    public string Assunto { get; set; } = "mailto:contato@alerta-em-acao.invalid";
}

/// <summary>
/// Web Push (RFC 8030) com criptografia de conteúdo aes128gcm (RFC 8291) e autenticação VAPID (RFC 8292),
/// implementado com System.Security.Cryptography (sem dependências externas).
/// Sucesso significa "aceito pelo serviço de push" — não comprova exibição no aparelho.
/// </summary>
public sealed class EnviadorWebPush(HttpClient http, IOptions<OpcoesWebPush> op) : IEnviadorPush
{
    public bool Configurado => !string.IsNullOrWhiteSpace(op.Value.ChavePublica) && !string.IsNullOrWhiteSpace(op.Value.ChavePrivada);
    public string? ChavePublicaVapid => op.Value.ChavePublica;

    public async Task<(bool Sucesso, bool Expirada, string Detalhe)> EnviarAsync(AssinaturaPush a, ConteudoPush c, CancellationToken ct)
    {
        if (!Configurado) return (false, false, "VAPID não configurado");
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { titulo = c.Titulo, corpo = c.Corpo, url = c.Url, tag = c.Tag }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var corpo = Cifrar(payload, B64u.Decodificar(a.P256dh), B64u.Decodificar(a.Auth));
        var endpoint = new Uri(a.Endpoint);
        var jwt = GerarJwtVapid($"{endpoint.Scheme}://{endpoint.Host}", op.Value.Assunto, op.Value.ChavePublica!, op.Value.ChavePrivada!);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(corpo) };
        req.Content.Headers.ContentType = new("application/octet-stream");
        req.Content.Headers.ContentEncoding.Add("aes128gcm");
        req.Headers.TryAddWithoutValidation("TTL", "3600");
        req.Headers.TryAddWithoutValidation("Urgency", "high");
        req.Headers.TryAddWithoutValidation("Topic", c.Tag.Length > 32 ? c.Tag[..32] : c.Tag);
        req.Headers.TryAddWithoutValidation("Authorization", $"vapid t={jwt}, k={op.Value.ChavePublica}");
        try
        {
            using var resp = await http.SendAsync(req, ct);
            var expirada = resp.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound;
            return (resp.IsSuccessStatusCode, expirada, $"HTTP {(int)resp.StatusCode}");
        }
        catch (HttpRequestException e) { return (false, false, e.Message); }
    }

    /// <summary>RFC 8291: ECDH(P-256) + HKDF-SHA256 + AES-128-GCM, registro único.</summary>
    public static byte[] Cifrar(byte[] conteudo, byte[] chavePublicaCliente, byte[] segredoAuth, byte[]? salt = null, ECDiffieHellman? efemera = null)
    {
        salt ??= RandomNumberGenerator.GetBytes(16);
        using var ecdh = efemera ?? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pubServidor = ExportarPontoNaoComprimido(ecdh);
        using var clienteKey = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = chavePublicaCliente[1..33], Y = chavePublicaCliente[33..65] },
        });
        var segredoEcdh = ecdh.DeriveRawSecretAgreement(clienteKey.PublicKey);

        var infoChave = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), chavePublicaCliente, pubServidor);
        var prkChave = HKDF.Extract(HashAlgorithmName.SHA256, segredoEcdh, segredoAuth);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkChave, 32, infoChave);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var texto = Concat(conteudo, [0x02]); // delimitador do último registro
        var cifrado = new byte[texto.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16)) aes.Encrypt(nonce, texto, cifrado, tag);

        var rs = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(rs, 4096);
        var cabecalho = Concat(salt, rs, [(byte)pubServidor.Length], pubServidor);
        return Concat(cabecalho, cifrado, tag);
    }

    public static string GerarJwtVapid(string audiencia, string assunto, string chavePublica, string chavePrivada)
    {
        var pub = B64u.Decodificar(chavePublica);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256, D = B64u.Decodificar(chavePrivada),
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
        });
        var header = B64u.Codificar(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds();
        var claims = B64u.Codificar(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { aud = audiencia, exp, sub = assunto })));
        var assinatura = ecdsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{claims}.{B64u.Codificar(assinatura)}";
    }

    public static (string Publica, string Privada) GerarParVapid()
    {
        using var e = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = e.ExportParameters(true);
        return (B64u.Codificar(Concat([0x04], p.Q.X!, p.Q.Y!)), B64u.Codificar(p.D!));
    }

    private static byte[] ExportarPontoNaoComprimido(ECDiffieHellman e)
    {
        var p = e.ExportParameters(false);
        return Concat([0x04], p.Q.X!, p.Q.Y!);
    }

    private static byte[] Concat(params byte[][] partes)
    {
        var r = new byte[partes.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in partes) { Buffer.BlockCopy(p, 0, r, o, p.Length); o += p.Length; }
        return r;
    }
}

public static class B64u
{
    public static string Codificar(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decodificar(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
