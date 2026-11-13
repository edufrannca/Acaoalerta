package br.alertaemacao.nearby

import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject

/**
 * Converte o pedido criado na PWA (link alertaemacao://importar#p=…) em mensagem do protocolo v2.
 * O app nativo NÃO lê o IndexedDB do navegador: a PWA entrega o pedido por este link.
 * Preserva o identificador da ocorrência, o horário e a posição do EMISSOR; o remetente é o pseudônimo
 * calculado pela PWA, para que o servidor reconheça o dono quando a PWA sincronizar (sem duplicar).
 */
object ImportacaoPwa {
    const val VERSAO = "aea-qr/1"

    // Nomes da PWA → nomes de dadosEstruturados do protocolo v2.
    private val MARCAS = mapOf(
        "ilhado" to "ilhado", "aguaSubindo" to "aguaSubindo", "correnteza" to "correnteza", "riscoDeslizamento" to "riscoDeslizamento",
        "desmoronamento" to "desmoronamento", "barreiraComSinaisInstabilidade" to "barreiraInstavel", "acessoBloqueado" to "acessoBloqueado",
        "equipamentoMedicoEssencial" to "equipamentoMedicoEssencial", "mobilidadeReduzida" to "mobilidadeReduzida", "sozinho" to "sozinho",
        "riscoSegurancaPublica" to "riscoSegurancaPublica",
    )

    data class Pedido(val ocorrencia: String, val remetente: String, val criadoEm: String, val lat: Double?, val lon: Double?, val precisao: Double?, val dados: JSONObject)

    fun decodificar(fragmento: String?): Pedido? = runCatching {
        val b64 = (fragmento ?: return null).removePrefix("#").removePrefix("p=")
        val json = JSONObject(String(Base64.decode(b64, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING), Charsets.UTF_8))
        converter(json)
    }.getOrNull()

    fun converter(p: JSONObject): Pedido? {
        if (p.optString("v") != VERSAO) return null
        val id = p.getString("id").lowercase()
        val em = p.getString("em")
        if (!em.startsWith("p:") || em.length > 40) return null
        val dados = JSONObject().put("origem", "pwa")
        val m = p.optJSONObject("m")
        if (m != null) for (k in m.keys()) if (m.optBoolean(k)) MARCAS[k]?.let { dados.put(it, true) }
        if (!p.isNull("pessoas")) dados.put("quantidadePessoas", p.getInt("pessoas"))
        p.optJSONArray("nec")?.let { if (it.length() > 0) dados.put("necessidades", JSONArray((0 until it.length()).map { i -> it.getString(i) })) }
        p.optJSONObject("comp")?.let { if (it.optBoolean("autorizado")) dados.put("compartilhamento", it) }
        if (p.optBoolean("d")) dados.put("ficticio", true)
        fun d(k: String) = if (p.has(k) && !p.isNull(k)) p.getDouble(k) else null
        return Pedido(id, em, p.getString("criado"), d("lat"), d("lon"), d("prec"), dados)
    }
}
