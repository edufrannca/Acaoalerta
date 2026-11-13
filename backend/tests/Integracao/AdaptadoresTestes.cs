using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlertaEmAcao.Infraestrutura.Adaptadores;
using AlertaEmAcao.Infraestrutura.Persistencia;

namespace AlertaEmAcao.Testes.Integracao;

/// <summary>Adaptadores de fontes públicas testados com amostras do formato real (conferido em 12/11/2026).</summary>
[Collection("api")]
public class AdaptadoresTestes
{
    [Fact]
    public void Apac_chuva_interpreta_amostra_real_e_filtra_rmr()
    {
        var json = """
        {"features":[
          {"attributes":{"objectid":682,"nome":"[INMET] Recife","latitude":-8.01888888,"longitude":-34.94416666,"ultima_leitura_data_hora":"1900-01-01 21:31:59","hora_1":-1,"horas_24":-1,"municipio":"Recife","cod_municipio":"2611606","codigo_gmmc":"261160602M"}},
          {"attributes":{"objectid":710,"nome":"[CEMADEN 01] Várzea","latitude":-8.036,"longitude":-34.98,"ultima_leitura_data_hora":"2026-11-12 21:10:00 21:10:00","hora_1":0,"horas_3":0,"horas_24":0,"horas_48":0.4196,"municipio":"Recife","cod_municipio":"2611606","codigo_gmmc":"261160601A"}},
          {"attributes":{"objectid":1,"nome":"Caruaru","latitude":-8.28,"longitude":-35.97,"cod_municipio":"2604106"}}
        ]}
        """;
        using var doc = JsonDocument.Parse(json);
        var l = FonteChuvaApac.Interpretar(doc.RootElement);
        Assert.Equal(2, l.Count);
        Assert.Null(l[0].UltimaLeituraEm);
        Assert.Null(l[0].Mm24h);
        Assert.Equal(0.42, l[1].Mm48h);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 21, 10, 0, TimeSpan.FromHours(-3)), l[1].UltimaLeituraEm);
    }

    [Fact]
    public void Areas_de_risco_recife_interpreta_ckan()
    {
        var json = """{"success":true,"result":{"records":[{"_id":1,"regional":"Sul","endereco":"Rua X 46","bairro":"Ibura","localidade":"Lagoa Encantada","descricao":"Área de Risco Nível 3","latitude":-8.128689,"longitude":-34.950349},{"_id":2,"latitude":null,"longitude":null}],"total":2}}""";
        using var doc = JsonDocument.Parse(json);
        var a = Assert.Single(FonteAreasRiscoRecife.Interpretar(doc.RootElement, DateTimeOffset.UtcNow));
        Assert.Equal("2611606", a.MunicipioCodigo);
        Assert.Equal("ODbL", a.Licenca);
    }

    [Fact]
    public void Abrigos_cop_nao_importa_contatos_pessoais()
    {
        var json = """{"features":[{"attributes":{"objectid":5,"no_entidade":"Escola X","tipo":"Escola","endereco":"Rua Y","latitude":-8.05,"longitude":-34.9,"status":"ATIVO","in_acessibilidade_inexistente":1,"contato_coordenador_local":"(81) 9999-9999","coordenador_local":"Fulano","last_edited_date":1767225600000}}]}""";
        using var doc = JsonDocument.Parse(json);
        var a = Assert.Single(FonteAbrigosCopRecife.Interpretar(doc.RootElement, DateTimeOffset.UtcNow));
        Assert.Equal("Disponibilidade não verificada", a.DescricaoDisponibilidade);
        Assert.Equal(AlertaEmAcao.Modulos.Abrigos.Acessibilidade.Inexistente, a.Acessibilidade);
        Assert.DoesNotContain("Fulano", JsonSerializer.Serialize(a));
    }

    [Fact]
    public void Copia_local_dos_limites_apac_tem_os_14_municipios()
    {
        var l = FonteLimitesApac.CarregarCopiaLocal();
        Assert.Equal(14, l.Count);
    }

    [Fact]
    public void Cifrador_de_campos_ida_e_volta_e_nonce_aleatorio()
    {
        CifradorCampos.Configurar(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var a = CifradorCampos.Cifrar("dado sensível");
        var b = CifradorCampos.Cifrar("dado sensível");
        Assert.NotEqual(a, b);
        Assert.Equal("dado sensível", CifradorCampos.Decifrar(a));
        CifradorCampos.Configurar(null);
    }

    [Fact]
    public void Hasher_de_senha_verifica_e_rejeita()
    {
        var h = new AlertaEmAcao.Infraestrutura.Seguranca.HasherSenhaPbkdf2();
        var hash = h.Gerar("senha-correta");
        Assert.True(h.Verificar("senha-correta", hash));
        Assert.False(h.Verificar("senha-errada", hash));
        Assert.NotEqual(hash, h.Gerar("senha-correta"));
    }

    /// <summary>Vetor de teste do RFC 8291, seção 5 (criptografia de mensagens Web Push).</summary>
    [Fact]
    public void WebPush_aes128gcm_confere_com_vetor_do_rfc_8291()
    {
        var conteudo = Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon");
        var asPriv = B64u.Decodificar("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        var asPub = B64u.Decodificar("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var uaPub = B64u.Decodificar("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4");
        var auth = B64u.Decodificar("BTBZMqHH6r4Tts7J_aSIgg");
        var salt = B64u.Decodificar("DGv6ra1nlYgDCS1FRnbzlw");
        var efemera = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256, D = asPriv, Q = new ECPoint { X = asPub[1..33], Y = asPub[33..65] },
        });
        var corpo = EnviadorWebPush.Cifrar(conteudo, uaPub, auth, salt, efemera);
        Assert.Equal("DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            B64u.Codificar(corpo));
    }

    [Fact]
    public void Jwt_vapid_e_assinado_e_verificavel()
    {
        var (pub, priv) = EnviadorWebPush.GerarParVapid();
        var jwt = EnviadorWebPush.GerarJwtVapid("https://fcm.googleapis.com", "mailto:teste@exemplo.com", pub, priv);
        var partes = jwt.Split('.');
        Assert.Equal(3, partes.Length);
        var p = B64u.Decodificar(pub);
        using var ecdsa = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = p[1..33], Y = p[33..65] } });
        Assert.True(ecdsa.VerifyData(Encoding.ASCII.GetBytes($"{partes[0]}.{partes[1]}"), B64u.Decodificar(partes[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
