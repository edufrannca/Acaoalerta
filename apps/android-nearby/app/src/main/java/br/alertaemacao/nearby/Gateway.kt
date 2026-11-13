package br.alertaemacao.nearby

import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/** Cliente HTTP do gateway v2 do backend. A chave do gateway é digitada no aparelho (nunca embutida no APK). */
class Gateway(private val urlBase: String, private val receptor: String, private val chave: String) {
    data class Resposta(val status: Int, val corpo: JSONObject?, val erro: String?)

    fun entregar(mensagem: JSONObject, receptorPseudonimo: String, recebidoEm: String, canal: String, modelo: String): Resposta {
        val corpo = JSONObject()
            .put("mensagem", mensagem)
            .put("receptorPseudonimizado", receptorPseudonimo)
            .put("recebidoLocalmenteEm", recebidoEm)
            .put("canal", canal)
            .put("modeloAparelhoReceptor", modelo)
            .put("plataformaReceptor", "android")
        return chamar("POST", "/api/gateway/v2/mensagens", corpo)
    }

    fun confirmacoes(ocorrenciaId: String): Resposta = chamar("GET", "/api/gateway/v2/ocorrencias/$ocorrenciaId/confirmacoes", null)

    private fun chamar(metodo: String, caminho: String, corpo: JSONObject?): Resposta = try {
        val c = URL(urlBase.trimEnd('/') + caminho).openConnection() as HttpURLConnection
        c.requestMethod = metodo
        c.connectTimeout = 10_000
        c.readTimeout = 20_000
        c.setRequestProperty("X-Gateway-Receptor", receptor)
        c.setRequestProperty("X-Gateway-Chave", chave)
        c.setRequestProperty("Accept", "application/json")
        if (corpo != null) {
            c.doOutput = true
            c.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            c.outputStream.use { it.write(corpo.toString().toByteArray(Charsets.UTF_8)) }
        }
        val status = c.responseCode
        val texto = (if (status in 200..299) c.inputStream else c.errorStream)?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }
        c.disconnect()
        Resposta(status, texto?.let { runCatching { JSONObject(it) }.getOrNull() }, if (status in 200..299) null else "HTTP $status ${texto?.take(200) ?: ""}")
    } catch (e: Exception) {
        Resposta(0, null, "${e.javaClass.simpleName}: ${e.message}")
    }
}
