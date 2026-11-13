package br.alertaemacao.nearby

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream

/**
 * Armazenamento local durável (um arquivo JSON por mensagem, gravação atômica com fsync).
 * A confirmação de recebimento (nível 2) só é enviada DEPOIS que salvar() retorna.
 * Não é um segundo banco de ocorrências: o identificadorOcorrencia é o mesmo do servidor e tudo é sincronizado pelo gateway.
 */
class Repositorio(contexto: Context) {
    private val dir = File(contexto.filesDir, "mensagens").apply { mkdirs() }
    private val log = File(contexto.filesDir, "historico.log")

    data class Registro(
        val mensagem: JSONObject,
        val direcao: String,            // "enviada" | "recebida"
        var nivel: Int,
        var pendenteGateway: Boolean,
        var ultimoErro: String?,
        val eventos: JSONArray,
        var protocolos: String?,
        var origemEndpoint: String?,
    ) {
        val id: String get() = mensagem.getString("identificadorMensagem")
        val ocorrencia: String get() = mensagem.getString("identificadorOcorrencia")
        val tipo: String get() = mensagem.getString("tipoMensagem")
        fun json() = JSONObject().put("mensagem", mensagem).put("direcao", direcao).put("nivel", nivel)
            .put("pendenteGateway", pendenteGateway).put("ultimoErro", ultimoErro ?: JSONObject.NULL)
            .put("eventos", eventos).put("protocolos", protocolos ?: JSONObject.NULL).put("origemEndpoint", origemEndpoint ?: JSONObject.NULL)
    }

    @Synchronized
    fun salvar(r: Registro) {
        val tmp = File(dir, r.id + ".tmp")
        FileOutputStream(tmp).use { out ->
            out.write(r.json().toString().toByteArray(Charsets.UTF_8))
            out.fd.sync()
        }
        if (!tmp.renameTo(File(dir, r.id + ".json"))) throw IllegalStateException("Falha ao gravar mensagem ${r.id}")
    }

    @Synchronized
    fun obter(id: String): Registro? {
        val f = File(dir, "$id.json")
        if (!f.exists()) return null
        val j = JSONObject(f.readText(Charsets.UTF_8))
        return Registro(j.getJSONObject("mensagem"), j.getString("direcao"), j.getInt("nivel"), j.optBoolean("pendenteGateway"),
            if (j.isNull("ultimoErro")) null else j.optString("ultimoErro"), j.optJSONArray("eventos") ?: JSONArray(),
            if (j.isNull("protocolos")) null else j.optString("protocolos"), if (j.isNull("origemEndpoint")) null else j.optString("origemEndpoint"))
    }

    @Synchronized
    fun todos(): List<Registro> = (dir.listFiles { f -> f.name.endsWith(".json") } ?: emptyArray())
        .mapNotNull { runCatching { obter(it.name.removeSuffix(".json")) }.getOrNull() }
        .sortedByDescending { it.mensagem.optString("horarioUltimaAtualizacao") }

    fun pendentesGateway() = todos().filter { it.pendenteGateway && it.tipo != Protocolo.Tipo.CONFIRMACAO }

    fun enviadasSemConfirmacao() = todos().filter { it.direcao == "enviada" && it.nivel < 2 && it.tipo != Protocolo.Tipo.CONFIRMACAO }

    fun evento(r: Registro, texto: String) {
        val em = Protocolo.agoraIso()
        r.eventos.put(JSONObject().put("em", em).put("texto", texto))
        salvar(r)
        registrarLog("${r.id.take(8)} $texto")
    }

    @Synchronized
    fun registrarLog(texto: String) {
        log.appendText("${Protocolo.agoraIso()} $texto\n", Charsets.UTF_8)
    }

    fun ultimasLinhasLog(n: Int): List<String> = if (!log.exists()) emptyList() else log.readLines(Charsets.UTF_8).takeLast(n).reversed()

    /** Exporta evidências (sem chaves) para anexar à matriz de testes físicos. */
    fun exportarEvidencias(): String = JSONObject()
        .put("exportadoEm", Protocolo.agoraIso())
        .put("mensagens", JSONArray(todos().map { it.json() }))
        .put("log", JSONArray(ultimasLinhasLog(500)))
        .toString(2)
}
