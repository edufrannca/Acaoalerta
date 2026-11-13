using Xunit;

namespace AlertaEmAcao.Testes.Integracao;

public class NomesEstacaoTestes
{
    [Theory]
    [InlineData("[APAC] Janga 2", "APAC", "Janga 2")]
    [InlineData("[CEMADEN] Janga", "CEMADEN", "Janga")]
    [InlineData("Recife (Várzea)", null, "Recife (Várzea)")]
    public void Nome_tecnico_da_estacao_e_separado_da_rede(string nome, string? rede, string estacao)
    {
        Assert.Equal(rede, AlertaEmAcao.Api.Endpoints.Endpoints.RedeEstacao(nome));
        Assert.Equal(estacao, AlertaEmAcao.Api.Endpoints.Endpoints.NomeEstacao(nome));
    }

    [Theory]
    [InlineData("Paulista", "paulista")]
    [InlineData("Jaboatão dos Guararapes", "jaboatao dos guararapes")]
    public void Normalizacao_de_municipio_para_conferir_o_bairro(string entrada, string esperado) =>
        Assert.Equal(esperado, AlertaEmAcao.Api.Endpoints.VozLocalidadeEndpoints.Normalizar(entrada));
}
