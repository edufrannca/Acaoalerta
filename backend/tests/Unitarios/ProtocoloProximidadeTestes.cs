using System.Text.Json;
using AlertaEmAcao.Contratos;
using Xunit;

namespace AlertaEmAcao.Testes.Unitarios;

/// <summary>
/// Compatibilidade do protocolo de proximidade v2: os mesmos vetores (docs/protocolo/vetores-v2.json,
/// gerados por uma implementação independente em Python) são verificados aqui, no Android (Kotlin) e no iOS (Swift).
/// </summary>
public class ProtocoloProximidadeTestes
{
    private static JsonDocument Vetores()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "protocolo", "vetores-v2.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "docs", "protocolo", "vetores-v2.json")));
    }

    [Fact]
    public void Canonica_e_integridade_conferem_com_os_vetores_compartilhados()
    {
        using var doc = Vetores();
        var chave = ProtocoloProximidade.ChaveGrupo(doc.RootElement.GetProperty("codigoGrupo").GetString()!);
        Assert.Equal(doc.RootElement.GetProperty("chaveGrupoHex").GetString(), Convert.ToHexString(chave).ToLowerInvariant());
        foreach (var v in doc.RootElement.GetProperty("vetores").EnumerateArray())
        {
            var m = v.GetProperty("mensagem").Deserialize<MensagemProximidadeV2>(ProtocoloProximidade.Json)!;
            Assert.Empty(ProtocoloProximidade.Validar(m));
            Assert.Equal(v.GetProperty("canonica").GetString(), ProtocoloProximidade.Canonica(m));
            Assert.Equal(v.GetProperty("integridade").GetString(), ProtocoloProximidade.CalcularIntegridade(m, chave));
            Assert.True(ProtocoloProximidade.VerificarIntegridade(m, chave));
        }
        foreach (var p in doc.RootElement.GetProperty("pseudonimos").EnumerateArray())
            Assert.Equal(p.GetProperty("remetente").GetString(), ProtocoloProximidade.Pseudonimizar(p.GetProperty("instalacao").GetString()!));
    }

    [Fact]
    public void Alteracao_de_conteudo_ou_chave_errada_invalida_a_integridade_mas_tentativas_e_estado_nao()
    {
        using var doc = Vetores();
        var chave = ProtocoloProximidade.ChaveGrupo("ALERTA-DEMO-2026");
        var m = doc.RootElement.GetProperty("vetores")[0].GetProperty("mensagem").Deserialize<MensagemProximidadeV2>(ProtocoloProximidade.Json)!;
        Assert.False(ProtocoloProximidade.VerificarIntegridade(m with { Municipio = "2609600" }, chave));
        Assert.False(ProtocoloProximidade.VerificarIntegridade(m with { CoordenadasAutorizadas = m.CoordenadasAutorizadas! with { Latitude = -8.2 } }, chave));
        Assert.False(ProtocoloProximidade.VerificarIntegridade(m, ProtocoloProximidade.ChaveGrupo("OUTRO-GRUPO")));
        // Campos que mudam em trânsito não entram na integridade (reenvio e atualização de estado).
        Assert.True(ProtocoloProximidade.VerificarIntegridade(m with { NumeroTentativas = 5, EstadoTransmissao = ProtocoloProximidade.Estados.RecebidoPorProximidade }, chave));
        // Código do grupo é normalizado (maiúsculas, sem espaços nas pontas).
        Assert.True(ProtocoloProximidade.VerificarIntegridade(m, ProtocoloProximidade.ChaveGrupo("  alerta-demo-2026 ")));
    }

    [Fact]
    public void Validacao_estrutural_rejeita_campos_invalidos()
    {
        var m = new MensagemProximidadeV2
        {
            VersaoProtocolo = "v1", IdentificadorMensagem = "x", IdentificadorOcorrencia = Guid.NewGuid().ToString(), TipoMensagem = "OUTRO",
            HorarioCriacao = "13/11/2026", HorarioUltimaAtualizacao = "2026-11-13T14:00:00.000Z", RemetentePseudonimizado = "",
            CoordenadasAutorizadas = new(95, 0, null), EstadoTransmissao = "ENTREGUE_AO_ORGAO",
        };
        var e = ProtocoloProximidade.Validar(m);
        Assert.Contains(e, x => x.Contains("versaoProtocolo"));
        Assert.Contains(e, x => x.Contains("identificadorMensagem"));
        Assert.Contains(e, x => x.Contains("tipoMensagem"));
        Assert.Contains(e, x => x.Contains("horarioCriacao"));
        Assert.Contains(e, x => x.Contains("coordenadas"));
        Assert.Contains(e, x => x.Contains("estadoTransmissao"));
        Assert.Contains(e, x => x.Contains("integridadeMensagem"));
        Assert.Equal(0, ProtocoloProximidade.Estados.Nivel("ENTREGUE_AO_ORGAO")); // não existe nível 6
        Assert.Equal(5, ProtocoloProximidade.Estados.Nivel(ProtocoloProximidade.Estados.RecebidoPelaCentralSimulada));
    }
}

