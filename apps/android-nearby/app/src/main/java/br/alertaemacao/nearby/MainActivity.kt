package br.alertaemacao.nearby

import android.Manifest
import android.os.Build
import android.os.Bundle
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import com.google.android.gms.nearby.Nearby
import com.google.android.gms.nearby.connection.AdvertisingOptions
import com.google.android.gms.nearby.connection.ConnectionInfo
import com.google.android.gms.nearby.connection.ConnectionLifecycleCallback
import com.google.android.gms.nearby.connection.ConnectionResolution
import com.google.android.gms.nearby.connection.ConnectionsStatusCodes
import com.google.android.gms.nearby.connection.DiscoveredEndpointInfo
import com.google.android.gms.nearby.connection.DiscoveryOptions
import com.google.android.gms.nearby.connection.EndpointDiscoveryCallback
import com.google.android.gms.nearby.connection.Payload
import com.google.android.gms.nearby.connection.PayloadCallback
import com.google.android.gms.nearby.connection.PayloadTransferUpdate
import com.google.android.gms.nearby.connection.Strategy
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.time.Instant
import java.util.UUID

/**
 * PROVA DE CONCEITO — Nearby Connections (1 salto).
 * Celular A (sem internet) gera um pedido FICTÍCIO e o envia ao celular B por proximidade (Bluetooth/Wi-Fi).
 * B confirma o recebimento (ACK), armazena localmente e, se tiver internet, encaminha ao backend demonstrativo
 * (POST /api/gateway/pacotes). Não há multissalto: retransmissão em cadeia exigiria implementação específica.
 */
class MainActivity : AppCompatActivity() {
    private val servico = "br.alertaemacao.nearby.SOCORRO"
    private val estrategia = Strategy.P2P_CLUSTER
    private val apelido = "aparelho-" + UUID.randomUUID().toString().take(6)
    private val escopo = CoroutineScope(Dispatchers.Main)
    private lateinit var log: TextView
    private val conectados = mutableSetOf<String>()
    private var modoOrigem = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        log = TextView(this).apply { setPadding(24, 24, 24, 24) }
        val raiz = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            addView(TextView(this@MainActivity).apply { text = "Alerta em Ação — PoC Nearby ($apelido)\nDados FICTÍCIOS. Não é canal oficial de emergência."; setPadding(24, 24, 24, 8) })
            addView(Button(this@MainActivity).apply { text = "Modo A: anunciar e enviar pedido fictício"; setOnClickListener { anunciar() } })
            addView(Button(this@MainActivity).apply { text = "Modo B: procurar e receber (gateway)"; setOnClickListener { descobrir() } })
            addView(ScrollView(this@MainActivity).apply { addView(log) })
        }
        setContentView(raiz)
        pedirPermissoes()
    }

    private fun pedirPermissoes() {
        val p = mutableListOf(Manifest.permission.ACCESS_FINE_LOCATION)
        if (Build.VERSION.SDK_INT >= 31) p += listOf(Manifest.permission.BLUETOOTH_ADVERTISE, Manifest.permission.BLUETOOTH_CONNECT, Manifest.permission.BLUETOOTH_SCAN)
        if (Build.VERSION.SDK_INT >= 33) p += Manifest.permission.NEARBY_WIFI_DEVICES
        requestPermissions(p.toTypedArray(), 1)
    }

    private fun registrar(msg: String) = runOnUiThread { log.append("${Instant.now()}  $msg\n") }

    // ----- Modo A: origem do pedido -----
    private fun anunciar() {
        modoOrigem = true
        Nearby.getConnectionsClient(this)
            .startAdvertising(apelido, servico, ciclo, AdvertisingOptions.Builder().setStrategy(estrategia).build())
            .addOnSuccessListener { registrar("Anunciando. Aproxime o aparelho B.") }
            .addOnFailureListener { registrar("Falha ao anunciar: ${it.message}") }
    }

    private fun pacoteFicticio(): JSONObject = JSONObject()
        .put("versao", "alerta-em-acao/gateway/v1")
        .put("id", UUID.randomUUID().toString())
        .put("dispositivoOrigem", apelido)
        .put("receptor", "")
        .put("saltos", 1)
        .put("criadoEm", Instant.now().toString())
        .put("latitude", -8.0631).put("longitude", -34.8711).put("precisaoMetros", 50)
        .put("pessoas", 2).put("aguaSubindo", true).put("feridos", false)
        .put("texto", "PEDIDO FICTÍCIO DE TESTE (PoC Nearby)")
        .put("canal", "nearby")

    // ----- Modo B: receptor/gateway -----
    private fun descobrir() {
        Nearby.getConnectionsClient(this)
            .startDiscovery(servico, object : EndpointDiscoveryCallback() {
                override fun onEndpointFound(id: String, info: DiscoveredEndpointInfo) {
                    registrar("Encontrado ${info.endpointName}; solicitando conexão")
                    Nearby.getConnectionsClient(this@MainActivity).requestConnection(apelido, id, ciclo)
                }
                override fun onEndpointLost(id: String) = registrar("Perdido $id")
            }, DiscoveryOptions.Builder().setStrategy(estrategia).build())
            .addOnSuccessListener { registrar("Procurando aparelhos próximos…") }
            .addOnFailureListener { registrar("Falha na descoberta: ${it.message}") }
    }

    private val ciclo = object : ConnectionLifecycleCallback() {
        override fun onConnectionInitiated(id: String, info: ConnectionInfo) {
            // PoC: aceita automaticamente. Produção: verificar token de autenticação exibido nos dois aparelhos.
            registrar("Conexão iniciada com ${info.endpointName} (token ${info.authenticationDigits})")
            Nearby.getConnectionsClient(this@MainActivity).acceptConnection(id, recebimento)
        }
        override fun onConnectionResult(id: String, r: ConnectionResolution) {
            if (r.status.statusCode == ConnectionsStatusCodes.STATUS_OK) {
                conectados += id
                registrar("Conectado a $id")
                if (modoOrigem) enviar(id)
            } else registrar("Conexão recusada/falhou: ${r.status.statusCode}")
        }
        override fun onDisconnected(id: String) { conectados -= id; registrar("Desconectado de $id") }
    }


    private fun enviar(id: String) {
        val p = pacoteFicticio()
        File(filesDir, "enviados.jsonl").appendText(p.toString() + "\n")
        Nearby.getConnectionsClient(this).sendPayload(id, Payload.fromBytes(p.toString().toByteArray()))
        registrar("Pedido ${p.getString("id").take(8)} enviado por proximidade; aguardando confirmação de B")
    }

    private val recebimento = object : PayloadCallback() {
        override fun onPayloadReceived(id: String, payload: Payload) {
            val texto = payload.asBytes()?.toString(Charsets.UTF_8) ?: return
            val json = JSONObject(texto)
            if (json.optString("tipo") == "ack") { registrar("ACK de B: pedido ${json.optString("id").take(8)} recebido e armazenado em B"); return }
            json.put("receptor", BuildConfig.RECEPTOR) // a chave do gateway é emitida por receptor
            File(filesDir, "recebidos.jsonl").appendText(json.toString() + "\n")
            registrar("Recebido pedido ${json.getString("id").take(8)} de ${json.getString("dispositivoOrigem")}; armazenado")
            Nearby.getConnectionsClient(this@MainActivity).sendPayload(id, Payload.fromBytes(JSONObject().put("tipo", "ack").put("id", json.getString("id")).toString().toByteArray()))
            escopo.launch { encaminhar(json) }
        }
        override fun onPayloadTransferUpdate(id: String, u: PayloadTransferUpdate) {}
    }

    /** B encaminha ao backend demonstrativo, se houver internet. Sem rede, permanece em recebidos.jsonl. */
    private suspend fun encaminhar(json: JSONObject) {
        val resultado = withContext(Dispatchers.IO) {
            runCatching {
                val c = URL("${BuildConfig.URL_BACKEND}/api/gateway/pacotes").openConnection() as HttpURLConnection
                c.requestMethod = "POST"; c.doOutput = true; c.connectTimeout = 8000; c.readTimeout = 8000
                c.setRequestProperty("Content-Type", "application/json")
                c.setRequestProperty("X-Gateway-Chave", BuildConfig.CHAVE_GATEWAY)
                c.outputStream.use { it.write(json.toString().toByteArray()) }
                c.responseCode to (c.errorStream ?: c.inputStream).bufferedReader().readText()
            }
        }
        resultado.onSuccess { (codigo, corpo) -> registrar("Backend respondeu HTTP $codigo: ${corpo.take(160)}") }
            .onFailure { registrar("Sem conexão com o backend; pedido mantido em B para reenvio: ${it.message}") }
    }
}
