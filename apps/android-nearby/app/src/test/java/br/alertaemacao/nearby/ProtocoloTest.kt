package br.alertaemacao.nearby

import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

/** Mesmos vetores verificados pelo backend (C#) e pelo iOS (Swift): docs/protocolo/vetores-v2.json. */
class ProtocoloTest {
    private fun vetores(): JSONObject {
        var dir: File? = File("").absoluteFile
        while (dir != null && !File(dir, "docs/protocolo/vetores-v2.json").exists()) dir = dir.parentFile
        return JSONObject(File(dir!!, "docs/protocolo/vetores-v2.json").readText(Charsets.UTF_8))
    }

    @Test
    fun canonica_e_integridade_conferem_com_os_vetores_compartilhados() {
        val v = vetores()
        val chave = Protocolo.chaveGrupo(v.getString("codigoGrupo"))
        assertEquals(v.getString("chaveGrupoHex"), Protocolo.hex(chave))
        val lista = v.getJSONArray("vetores")
        for (i in 0 until lista.length()) {
            val caso = lista.getJSONObject(i)
            val m = caso.getJSONObject("mensagem")
            assertTrue(Protocolo.validar(m).toString(), Protocolo.validar(m).isEmpty())
            assertEquals(caso.getString("canonica"), Protocolo.canonica(m))
            assertEquals(caso.getString("integridade"), Protocolo.integridade(m, chave))
            assertTrue(Protocolo.verificar(m, chave))
        }
        val ps = v.getJSONArray("pseudonimos")
        for (i in 0 until ps.length()) assertEquals(ps.getJSONObject(i).getString("remetente"), Protocolo.pseudonimizar(ps.getJSONObject(i).getString("instalacao")))
    }

    @Test
    fun adulteracao_invalida_e_campos_de_transito_nao() {
        val v = vetores()
        val chave = Protocolo.chaveGrupo("ALERTA-DEMO-2026")
        val m = JSONObject(v.getJSONArray("vetores").getJSONObject(0).getJSONObject("mensagem").toString())
        m.put("numeroTentativas", 7).put("estadoTransmissao", Protocolo.Estado.PROXIMIDADE)
        assertTrue(Protocolo.verificar(m, chave))
        m.put("municipio", "2609600")
        assertFalse(Protocolo.verificar(m, chave))
        assertFalse(Protocolo.verificar(JSONObject(v.getJSONArray("vetores").getJSONObject(0).getJSONObject("mensagem").toString()), Protocolo.chaveGrupo("OUTRO")))
    }

    @Test
    fun mensagem_montada_no_android_e_verificavel_e_confirmacao_correlaciona() {
        val chave = Protocolo.chaveGrupo("ALERTA-DEMO-2026")
        val m = Protocolo.montar(Protocolo.Tipo.PEDIDO, Protocolo.novoId(), Protocolo.pseudonimizar("x"), chave,
            JSONObject().put("ficticio", true).put("quantidadePessoas", 2), "2611606", -8.12345678, -34.9, 12.6)
        assertTrue(Protocolo.validar(m).isEmpty())
        assertTrue(Protocolo.verificar(JSONObject(m.toString()), chave))
        assertEquals(-8.123457, m.getJSONObject("coordenadasAutorizadas").getDouble("latitude"), 0.0)
        val c = Protocolo.confirmacao(m, 2, Protocolo.pseudonimizar("y"), chave, "persistido")
        assertEquals(m.getString("identificadorMensagem"), c.getString("identificadorCorrelacao"))
        assertEquals(Protocolo.Estado.PROXIMIDADE, c.getString("estadoTransmissao"))
        assertTrue(Protocolo.verificar(c, chave))
    }
}

class ImportacaoPwaTest {
    @org.junit.Test
    fun pedidoDaPwaPreservaIdentificadorHorarioEPosicaoDoEmissor() {
        val p = org.json.JSONObject("""{"v":"aea-qr/1","id":"0193A5B0-1111-7222-8333-444455556666","em":"p:abc","criado":"2026-11-13T12:34:56Z",
            "lat":-8.0402,"lon":-34.9301,"prec":18,"lido":null,"m":{"ilhado":true,"barreiraComSinaisInstabilidade":true,"aguaSubindo":false},
            "pessoas":3,"nec":["agua"],"comp":{"autorizado":true,"incluirPessoas":false,"incluirNecessidades":true},"h":"x"}""")
        val r = ImportacaoPwa.converter(p)!!
        org.junit.Assert.assertEquals("0193a5b0-1111-7222-8333-444455556666", r.ocorrencia)
        org.junit.Assert.assertEquals("p:abc", r.remetente)
        org.junit.Assert.assertEquals("2026-11-13T12:34:56Z", r.criadoEm)
        org.junit.Assert.assertEquals(-8.0402, r.lat!!, 1e-9)
        org.junit.Assert.assertTrue(r.dados.getBoolean("barreiraInstavel"))
        org.junit.Assert.assertFalse(r.dados.has("aguaSubindo"))
        org.junit.Assert.assertEquals(3, r.dados.getInt("quantidadePessoas"))
        org.junit.Assert.assertTrue(r.dados.getJSONObject("compartilhamento").getBoolean("autorizado"))
    }

    @org.junit.Test
    fun semCoordenadasFicaSemLocalizacaoEVersaoDesconhecidaERecusada() {
        val p = org.json.JSONObject("""{"v":"aea-qr/1","id":"0193a5b0-1111-7222-8333-444455556666","em":"p:abc","criado":"2026-11-13T12:34:56Z","lat":null,"lon":null,"prec":null,"m":{},"pessoas":null,"nec":[],"comp":null,"h":"x"}""")
        org.junit.Assert.assertNull(ImportacaoPwa.converter(p)!!.lat)
        org.junit.Assert.assertNull(ImportacaoPwa.converter(p.put("v", "outra/9")))
    }
}
