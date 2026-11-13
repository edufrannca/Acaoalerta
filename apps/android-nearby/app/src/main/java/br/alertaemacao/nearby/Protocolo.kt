package br.alertaemacao.nearby

import org.json.JSONArray
import org.json.JSONObject
import java.security.MessageDigest
import java.text.SimpleDateFormat
import java.util.Base64
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Protocolo alerta-em-acao/proximidade/v2 — mesma especificação do backend (C#) e do iOS (Swift).
 * Especificação: docs/PROTOCOLO_PROXIMIDADE.md · vetores: docs/protocolo/vetores-v2.json.
 */
object Protocolo {
    const val VERSAO = "alerta-em-acao/proximidade/v2"
    const val ID_SERVICO = "br.alertaemacao.nearby"
    const val TAMANHO_MAXIMO = 32 * 1024
    private const val ROTULO = "alerta-em-acao/proximidade/v2"

    object Tipo {
        const val PEDIDO = "PEDIDO_SOCORRO"
        const val ATUALIZACAO = "ATUALIZACAO"
        const val AVISO = "AVISO_DEMONSTRATIVO"
        const val CONFIRMACAO = "CONFIRMACAO"
        val TODOS = setOf(PEDIDO, ATUALIZACAO, AVISO, CONFIRMACAO)
    }

    object Estado {
        const val SALVO = "SALVO_NO_APARELHO"
        const val PROXIMIDADE = "RECEBIDO_POR_PROXIMIDADE"
        const val GATEWAY = "RECEBIDO_PELO_GATEWAY"
        const val SERVIDOR = "RECEBIDO_PELO_SERVIDOR"
        const val CENTRAL = "RECEBIDO_PELA_CENTRAL_SIMULADA"
        const val FALHOU = "FALHOU"
        val TODOS = setOf(SALVO, PROXIMIDADE, GATEWAY, SERVIDOR, CENTRAL, FALHOU)
        fun nivel(e: String?): Int = when (e) {
            SALVO -> 1; PROXIMIDADE -> 2; GATEWAY -> 3; SERVIDOR -> 4; CENTRAL -> 5; else -> 0
        }
        fun porNivel(n: Int): String = when (n) {
            1 -> SALVO; 2 -> PROXIMIDADE; 3 -> GATEWAY; 4 -> SERVIDOR; 5 -> CENTRAL; else -> FALHOU
        }
        fun descrever(n: Int): String = when (n) {
            1 -> "Nível 1 — salvo neste aparelho"
            2 -> "Nível 2 — recebido e salvo por aparelho próximo"
            3 -> "Nível 3 — aceito pelo gateway para encaminhamento"
            4 -> "Nível 4 — recebido pelo servidor do Alerta em Ação"
            5 -> "Nível 5 — recebido pela central SIMULADA (protocolo de demonstração)"
            else -> "Sem confirmação"
        }
    }

    fun chaveGrupo(codigo: String): ByteArray = hmac(ROTULO.toByteArray(Charsets.UTF_8), codigo.trim().uppercase(Locale.ROOT).toByteArray(Charsets.UTF_8))

    fun pseudonimizar(instalacao: String): String =
        "ap-" + hex(MessageDigest.getInstance("SHA-256").digest(instalacao.toByteArray(Charsets.UTF_8))).substring(0, 16)

    fun agoraIso(): String = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT).apply { timeZone = TimeZone.getTimeZone("UTC") }.format(Date())

    fun novoId(): String = UUID.randomUUID().toString()

    /** Forma canônica: ver especificação. numeroTentativas e estadoTransmissao ficam de fora. */
    fun canonica(m: JSONObject): String {
        val sb = StringBuilder()
        fun l(v: String?) { sb.append(v ?: "").append('\n') }
        fun texto(k: String): String? = if (m.has(k) && !m.isNull(k)) m.getString(k) else null
        l(texto("versaoProtocolo")); l(texto("identificadorMensagem")); l(texto("identificadorOcorrencia")); l(texto("tipoMensagem")); l(texto("municipio"))
        val c = if (m.has("coordenadasAutorizadas") && !m.isNull("coordenadasAutorizadas")) m.getJSONObject("coordenadasAutorizadas") else null
        l(c?.let { String.format(Locale.ROOT, "%.6f", it.getDouble("latitude")) })
        l(c?.let { String.format(Locale.ROOT, "%.6f", it.getDouble("longitude")) })
        l(c?.let { if (it.has("precisaoMetros") && !it.isNull("precisaoMetros")) Math.round(it.getDouble("precisaoMetros")).toString() else null })
        l(texto("horarioCriacao")); l(texto("horarioUltimaAtualizacao")); l(texto("remetentePseudonimizado")); l(texto("identificadorCorrelacao"))
        val d = if (m.has("dadosEstruturados") && !m.isNull("dadosEstruturados")) m.getJSONObject("dadosEstruturados") else null
        if (d != null) {
            val chaves = d.keys().asSequence().toList().sorted() // ordenação ordinal (UTF-16), igual ao C# Ordinal para ASCII
            for (k in chaves) {
                val v = valor(d.opt(k)) ?: continue
                sb.append(k).append('=').append(v).append('\n')
            }
        }
        return sb.toString()
    }

    private fun valor(v: Any?): String? = when (v) {
        null, JSONObject.NULL -> null
        is Boolean -> if (v) "true" else "false"
        is Int, is Long -> v.toString()
        is Double -> if (v % 1.0 == 0.0 && Math.abs(v) < 1e15) v.toLong().toString() else String.format(Locale.ROOT, "%.6f", v)
        is Number -> v.toLong().toString()
        is String -> escapar(v)
        is JSONArray -> (0 until v.length()).joinToString(",") { i ->
            when (val x = v.opt(i)) {
                is Boolean -> if (x) "true" else "false"
                is Int, is Long -> x.toString()
                is Double -> if (x % 1.0 == 0.0) x.toLong().toString() else ""
                is String -> escapar(x).replace(",", "\\,")
                else -> ""
            }
        }
        else -> null
    }

    private fun escapar(s: String) = s.replace("\\", "\\\\").replace("\n", "\\n").replace("\r", "\\r")

    fun integridade(m: JSONObject, chave: ByteArray): String =
        "hmac-sha256:" + Base64.getUrlEncoder().withoutPadding().encodeToString(hmac(chave, canonica(m).toByteArray(Charsets.UTF_8)))

    fun verificar(m: JSONObject, chave: ByteArray): Boolean {
        val recebida = m.optString("integridadeMensagem", "")
        val esperada = integridade(m, chave)
        return MessageDigest.isEqual(recebida.toByteArray(), esperada.toByteArray())
    }

    /** Erros estruturais (independentes da chave). Lista vazia = válida. */
    fun validar(m: JSONObject): List<String> {
        val e = mutableListOf<String>()
        if (m.optString("versaoProtocolo") != VERSAO) e += "versaoProtocolo não suportada"
        if (!uuid(m.optString("identificadorMensagem"))) e += "identificadorMensagem deve ser UUID"
        if (!uuid(m.optString("identificadorOcorrencia"))) e += "identificadorOcorrencia deve ser UUID"
        if (m.optString("tipoMensagem") !in Tipo.TODOS) e += "tipoMensagem inválido"
        val est = if (m.has("estadoTransmissao") && !m.isNull("estadoTransmissao")) m.getString("estadoTransmissao") else null
        if (est != null && est !in Estado.TODOS) e += "estadoTransmissao inválido"
        if (!m.optString("horarioCriacao").endsWith("Z")) e += "horarioCriacao deve terminar em Z"
        if (!m.optString("horarioUltimaAtualizacao").endsWith("Z")) e += "horarioUltimaAtualizacao deve terminar em Z"
        val rem = m.optString("remetentePseudonimizado")
        if (rem.isBlank() || rem.length > 64) e += "remetentePseudonimizado inválido"
        if (m.optString("tipoMensagem") == Tipo.CONFIRMACAO && !uuid(m.optString("identificadorCorrelacao"))) e += "CONFIRMACAO exige identificadorCorrelacao"
        if (m.optString("integridadeMensagem").isBlank()) e += "integridadeMensagem ausente"
        return e
    }

    private fun uuid(s: String) = runCatching { UUID.fromString(s); s.length == 36 }.getOrDefault(false)

    /** Monta e assina uma mensagem. Coordenadas são arredondadas para 6 casas antes de assinar. */
    fun montar(
        tipo: String, ocorrenciaId: String, remetente: String, chave: ByteArray, dados: JSONObject?,
        municipio: String? = null, latitude: Double? = null, longitude: Double? = null, precisao: Double? = null,
        correlacao: String? = null, estado: String = Estado.SALVO, criadoEm: String = agoraIso(),
    ): JSONObject {
        val m = JSONObject()
        m.put("versaoProtocolo", VERSAO)
        m.put("identificadorMensagem", novoId())
        m.put("identificadorOcorrencia", ocorrenciaId)
        m.put("tipoMensagem", tipo)
        if (municipio != null) m.put("municipio", municipio)
        if (latitude != null && longitude != null) {
            val c = JSONObject()
            c.put("latitude", Math.round(latitude * 1e6) / 1e6)
            c.put("longitude", Math.round(longitude * 1e6) / 1e6)
            if (precisao != null) c.put("precisaoMetros", Math.round(precisao).toDouble())
            m.put("coordenadasAutorizadas", c)
        }
        m.put("horarioCriacao", criadoEm)
        m.put("horarioUltimaAtualizacao", agoraIso())
        m.put("remetentePseudonimizado", remetente)
        if (dados != null) m.put("dadosEstruturados", dados)
        m.put("numeroTentativas", 0)
        if (correlacao != null) m.put("identificadorCorrelacao", correlacao)
        m.put("estadoTransmissao", estado)
        m.put("integridadeMensagem", integridade(m, chave))
        return m
    }

    fun confirmacao(original: JSONObject, nivel: Int, remetente: String, chave: ByteArray, comprovante: String?, extra: JSONObject? = null): JSONObject {
        val d = extra ?: JSONObject()
        d.put("nivel", nivel)
        if (comprovante != null) d.put("comprovante", comprovante)
        return montar(Tipo.CONFIRMACAO, original.getString("identificadorOcorrencia"), remetente, chave, d,
            correlacao = original.getString("identificadorMensagem"), estado = Estado.porNivel(nivel))
    }

    private fun hmac(chave: ByteArray, dados: ByteArray): ByteArray =
        Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(chave, "HmacSHA256")) }.doFinal(dados)

    fun hex(b: ByteArray) = b.joinToString("") { "%02x".format(it) }
}
