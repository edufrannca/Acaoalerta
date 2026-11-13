package br.alertaemacao.nearby

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.graphics.Typeface
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.InputType
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.google.android.gms.nearby.Nearby
import com.google.android.gms.nearby.connection.AdvertisingOptions
import com.google.android.gms.nearby.connection.ConnectionInfo
import com.google.android.gms.nearby.connection.ConnectionLifecycleCallback
import com.google.android.gms.nearby.connection.ConnectionResolution
import com.google.android.gms.nearby.connection.ConnectionsClient
import com.google.android.gms.nearby.connection.ConnectionsStatusCodes
import com.google.android.gms.nearby.connection.DiscoveredEndpointInfo
import com.google.android.gms.nearby.connection.DiscoveryOptions
import com.google.android.gms.nearby.connection.EndpointDiscoveryCallback
import com.google.android.gms.nearby.connection.Payload
import com.google.android.gms.nearby.connection.PayloadCallback
import com.google.android.gms.nearby.connection.PayloadTransferUpdate
import com.google.android.gms.nearby.connection.Strategy
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID
import java.util.concurrent.Executors

/**
 * Alerta em Ação — comunicação por proximidade (Nearby Connections), AMBIENTE DE DEMONSTRAÇÃO.
 * Mesma estratégia (P2P_CLUSTER) e mesmo serviceId do app iOS. Dados sempre fictícios.
 * Fluxo: A (sem internet) → Nearby → B → [gateway] → API → centrais SIMULADAS. Confirmações de aplicação por nível.
 */
class MainActivity : AppCompatActivity() {
    private lateinit var nearby: ConnectionsClient
    private lateinit var repo: Repositorio
    private val prefs by lazy { getSharedPreferences("alerta-nearby", Context.MODE_PRIVATE) }
    private val io = Executors.newSingleThreadExecutor()
    private val ui = Handler(Looper.getMainLooper())

    private val encontrados = linkedMapOf<String, String>()      // endpointId -> nome
    private val conectados = linkedMapOf<String, String>()
    private val pedidoConexaoEm = mutableMapOf<String, Long>()
    private val envioEm = mutableMapOf<String, Long>()           // mensagemId -> ms do envio
    private val payloadParaMensagem = mutableMapOf<Long, String>()
    private var descobertaIniciadaEm = 0L
    private var anunciando = false
    private var procurando = false

    private lateinit var status: TextView
    private lateinit var listaEndpoints: LinearLayout
    private lateinit var listaMensagens: LinearLayout
    private lateinit var campoNome: EditText
    private lateinit var campoGrupo: EditText
    private lateinit var campoUrl: EditText
    private lateinit var campoReceptor: EditText
    private lateinit var campoChave: EditText
    private lateinit var caixaGateway: CheckBox

    private val instalacao: String by lazy {
        prefs.getString("instalacao", null) ?: UUID.randomUUID().toString().also { prefs.edit().putString("instalacao", it).apply() }
    }
    private val remetente: String get() = Protocolo.pseudonimizar(instalacao)
    private val chaveGrupo: ByteArray get() = Protocolo.chaveGrupo(campoGrupo.text.toString().ifBlank { "ALERTA-DEMO-2026" })
    private val nomeAparelho: String get() = campoNome.text.toString().ifBlank { "Android ${Build.MODEL}" }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        nearby = Nearby.getConnectionsClient(this)
        repo = Repositorio(this)
        montarTela()
        pedirPermissoes()
        observarRede()
        atualizar()
    }

    // ---------------- Interface ----------------
    private fun montarTela() {
        val raiz = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(32, 32, 32, 64) }
        fun titulo(t: String) = TextView(this).apply { text = t; textSize = 20f; setTypeface(typeface, Typeface.BOLD); setPadding(0, 24, 0, 8) }
        fun botao(t: String, acao: () -> Unit) = Button(this).apply { text = t; textSize = 18f; minHeight = 140; setOnClickListener { acao() } }
        fun campo(dica: String, chave: String, padrao: String, senha: Boolean = false) = EditText(this).apply {
            hint = dica; contentDescription = dica; textSize = 16f
            setText(prefs.getString(chave, padrao))
            if (senha) inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }

        raiz.addView(TextView(this).apply {
            text = "AMBIENTE DE DEMONSTRAÇÃO — dados fictícios. Nenhum órgão real é acionado. Em emergência real ligue 193, 199, 192 ou 190."
            setBackgroundColor(Color.parseColor("#A83232")); setTextColor(Color.WHITE); textSize = 15f; setPadding(24, 24, 24, 24)
        })
        raiz.addView(titulo("Alerta em Ação — Proximidade"))
        status = TextView(this).apply { textSize = 16f; contentDescription = "Estado da comunicação" }
        raiz.addView(status)

        raiz.addView(titulo("Configuração"))
        campoNome = campo("Nome deste aparelho (ex.: Android A)", "nome", "Android ${Build.MODEL}")
        campoGrupo = campo("Código do grupo de demonstração", "grupo", "ALERTA-DEMO-2026")
        campoUrl = campo("URL do servidor (https://…)", "url", BuildConfig.URL_BACKEND)
        campoReceptor = campo("Identificador do gateway", "receptor", BuildConfig.RECEPTOR)
        campoChave = campo("Chave do gateway (tela /demo do servidor)", "chave", BuildConfig.CHAVE_GATEWAY, senha = true)
        caixaGateway = CheckBox(this).apply { text = "Atuar como gateway (encaminhar ao servidor quando houver internet)"; textSize = 16f; isChecked = prefs.getBoolean("gateway", true) }
        listOf(campoNome, campoGrupo, campoUrl, campoReceptor, campoChave, caixaGateway).forEach { raiz.addView(it) }
        raiz.addView(botao("Salvar configuração") { salvarConfig() })

        raiz.addView(titulo("Comunicação"))
        raiz.addView(botao("Receber: tornar este aparelho visível") { alternarAnuncio() })
        raiz.addView(botao("Enviar: procurar aparelhos próximos") { alternarDescoberta() })
        raiz.addView(botao("Criar pedido de socorro FICTÍCIO") { criarPedidoFicticio() })
        raiz.addView(botao("Enviar aviso demonstrativo a todos os conectados") { enviarAvisoDemonstrativo() })
        raiz.addView(botao("Encaminhar pendentes ao servidor agora") { encaminharPendentes() })
        raiz.addView(botao("Exportar evidências (sem chaves)") { exportar() })
        raiz.addView(botao("Parar tudo e desconectar") { pararTudo() })

        raiz.addView(titulo("Aparelhos encontrados"))
        listaEndpoints = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        raiz.addView(listaEndpoints)
        raiz.addView(titulo("Mensagens e confirmações"))
        listaMensagens = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        raiz.addView(listaMensagens)
        setContentView(ScrollView(this).apply { addView(raiz) })
    }

    private fun salvarConfig() {
        prefs.edit().putString("nome", campoNome.text.toString()).putString("grupo", campoGrupo.text.toString())
            .putString("url", campoUrl.text.toString()).putString("receptor", campoReceptor.text.toString())
            .putString("chave", campoChave.text.toString()).putBoolean("gateway", caixaGateway.isChecked).apply()
        repo.registrarLog("configuração salva (grupo=${campoGrupo.text}, gateway=${caixaGateway.isChecked})")
        atualizar()
    }

    private fun atualizar() = ui.post {
        val internet = temInternet()
        status.text = buildString {
            append("Aparelho: $nomeAparelho ($remetente)\n")
            append("Internet: ${if (internet) "disponível" else "INDISPONÍVEL"} · Visível: ${if (anunciando) "sim" else "não"} · Procurando: ${if (procurando) "sim" else "não"}\n")
            append("Conectados: ${if (conectados.isEmpty()) "nenhum" else conectados.values.joinToString()}\n")
            append("Pendentes de envio ao servidor: ${repo.pendentesGateway().size}")
        }
        listaEndpoints.removeAllViews()
        if (encontrados.isEmpty()) listaEndpoints.addView(TextView(this).apply { text = "Nenhum aparelho encontrado ainda. Descoberta não significa entrega." })
        for ((id, nome) in encontrados) {
            val conectado = conectados.containsKey(id)
            listaEndpoints.addView(Button(this).apply {
                text = if (conectado) "$nome — conectado" else "Conectar a $nome"
                textSize = 17f; isEnabled = !conectado
                setOnClickListener { solicitarConexao(id, nome) }
            })
        }
        listaMensagens.removeAllViews()
        for (r in repo.todos().filter { it.tipo != Protocolo.Tipo.CONFIRMACAO }.take(30)) {
            val ev = (0 until r.eventos.length()).joinToString("\n") { i -> val e = r.eventos.getJSONObject(i); "  ${e.getString("em").substring(11, 23)}  ${e.getString("texto")}" }
            listaMensagens.addView(TextView(this).apply {
                textSize = 15f; setPadding(0, 16, 0, 16)
                text = "${if (r.direcao == "enviada") "ENVIADA" else "RECEBIDA"} · ${r.tipo}\nMensagem ${r.id.take(8)} · Ocorrência ${r.ocorrencia.take(8)}\n" +
                    "${Protocolo.Estado.descrever(r.nivel)}${r.protocolos?.let { "\nProtocolos (SIMULADOS): $it" } ?: ""}" +
                    "${r.ultimoErro?.let { "\nÚltimo erro: $it" } ?: ""}\n$ev"
            })
        }
    }

    // ---------------- Permissões ----------------
    private fun permissoesNecessarias(): Array<String> {
        val p = mutableListOf(Manifest.permission.ACCESS_COARSE_LOCATION, Manifest.permission.ACCESS_FINE_LOCATION)
        if (Build.VERSION.SDK_INT >= 31) p += listOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_ADVERTISE, Manifest.permission.BLUETOOTH_CONNECT)
        if (Build.VERSION.SDK_INT >= 33) p += Manifest.permission.NEARBY_WIFI_DEVICES
        return p.toTypedArray()
    }

    private fun pedirPermissoes() {
        val faltando = permissoesNecessarias().filter { ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }
        if (faltando.isNotEmpty()) ActivityCompat.requestPermissions(this, faltando.toTypedArray(), 1)
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        val negadas = permissions.filterIndexed { i, _ -> grantResults.getOrNull(i) != PackageManager.PERMISSION_GRANTED }
        if (negadas.isNotEmpty()) {
            repo.registrarLog("permissões negadas: ${negadas.joinToString { it.substringAfterLast('.') }}")
            aviso("Permissões negadas", "Sem ${negadas.joinToString { it.substringAfterLast('.') }}, a comunicação por proximidade não funciona. As mensagens continuam salvas neste aparelho.")
        }
        atualizar()
    }

    private fun permissoesOk() = permissoesNecessarias().all { ContextCompat.checkSelfPermission(this, it) == PackageManager.PERMISSION_GRANTED }

    // ---------------- Nearby ----------------
    private val ciclo = object : ConnectionLifecycleCallback() {
        override fun onConnectionInitiated(endpointId: String, info: ConnectionInfo) {
            val nome = info.endpointName
            encontrados[endpointId] = nome
            repo.registrarLog("pedido de conexão ${if (info.isIncomingConnection) "recebido de" else "enviado a"} $nome — código ${info.authenticationDigits}")
            AlertDialog.Builder(this@MainActivity)
                .setTitle("Confirme o pareamento")
                .setMessage("Aparelho: $nome\n\nCódigo de verificação: ${info.authenticationDigits}\n\nO OUTRO aparelho deve mostrar exatamente o mesmo código. Se não mostrar, ou se você não reconhece o aparelho, recuse.")
                .setCancelable(false)
                .setPositiveButton("Os códigos conferem — conectar") { _, _ -> nearby.acceptConnection(endpointId, cargas) }
                .setNegativeButton("Recusar") { _, _ ->
                    nearby.rejectConnection(endpointId)
                    repo.registrarLog("pareamento RECUSADO com $nome")
                }
                .show()
        }

        override fun onConnectionResult(endpointId: String, result: ConnectionResolution) {
            val nome = encontrados[endpointId] ?: endpointId
            when (result.status.statusCode) {
                ConnectionsStatusCodes.STATUS_OK -> {
                    conectados[endpointId] = nome
                    val ms = pedidoConexaoEm.remove(endpointId)?.let { System.currentTimeMillis() - it }
                    repo.registrarLog("conectado a $nome${ms?.let { " em $it ms" } ?: ""}")
                    enviarPendentesPara(endpointId)
                }
                ConnectionsStatusCodes.STATUS_CONNECTION_REJECTED -> repo.registrarLog("conexão com $nome rejeitada")
                else -> repo.registrarLog("falha ao conectar a $nome: código ${result.status.statusCode}")
            }
            atualizar()
        }

        override fun onDisconnected(endpointId: String) {
            repo.registrarLog("desconectado de ${conectados.remove(endpointId) ?: endpointId}")
            atualizar()
        }
    }

    private val descoberta = object : EndpointDiscoveryCallback() {
        override fun onEndpointFound(endpointId: String, info: DiscoveredEndpointInfo) {
            if (info.serviceId != Protocolo.ID_SERVICO) return
            encontrados[endpointId] = info.endpointName
            repo.registrarLog("encontrado ${info.endpointName} em ${System.currentTimeMillis() - descobertaIniciadaEm} ms após iniciar a busca")
            atualizar()
        }

        override fun onEndpointLost(endpointId: String) {
            repo.registrarLog("aparelho fora de alcance: ${encontrados.remove(endpointId) ?: endpointId}")
            atualizar()
        }
    }

    private val cargas = object : PayloadCallback() {
        override fun onPayloadReceived(endpointId: String, payload: Payload) {
            if (payload.type != Payload.Type.BYTES) return
            val bytes = payload.asBytes() ?: return
            if (bytes.size > Protocolo.TAMANHO_MAXIMO) { repo.registrarLog("carga acima do limite descartada"); return }
            val m = runCatching { JSONObject(String(bytes, Charsets.UTF_8)) }.getOrNull()
            if (m == null) { repo.registrarLog("carga não é JSON válido — descartada"); return }
            receber(endpointId, m)
        }

        override fun onPayloadTransferUpdate(endpointId: String, update: PayloadTransferUpdate) {
            val msgId = payloadParaMensagem[update.payloadId] ?: return
            when (update.status) {
                PayloadTransferUpdate.Status.SUCCESS -> {
                    payloadParaMensagem.remove(update.payloadId)
                    val r = repo.obter(msgId) ?: return
                    val ms = envioEm[msgId]?.let { System.currentTimeMillis() - it }
                    // Sucesso do SDK NÃO é entrega: aguardamos a confirmação de aplicação do receptor.
                    repo.evento(r, "bytes transferidos pelo Nearby${ms?.let { " em $it ms" } ?: ""} — aguardando confirmação do receptor")
                }
                PayloadTransferUpdate.Status.FAILURE, PayloadTransferUpdate.Status.CANCELED -> {
                    payloadParaMensagem.remove(update.payloadId)
                    val r = repo.obter(msgId) ?: return
                    r.ultimoErro = "transmissão interrompida"
                    repo.evento(r, "transmissão interrompida — mensagem preservada para nova tentativa")
                }
                else -> Unit
            }
            atualizar()
        }
    }

    private fun alternarAnuncio() {
        if (!permissoesOk()) { pedirPermissoes(); return }
        if (anunciando) { nearby.stopAdvertising(); anunciando = false; repo.registrarLog("parou de anunciar"); atualizar(); return }
        val op = AdvertisingOptions.Builder().setStrategy(Strategy.P2P_CLUSTER).build()
        nearby.startAdvertising(nomeAparelho, Protocolo.ID_SERVICO, ciclo, op)
            .addOnSuccessListener { anunciando = true; repo.registrarLog("anunciando como $nomeAparelho"); atualizar() }
            .addOnFailureListener { e -> repo.registrarLog("falha ao anunciar: ${e.message}"); aviso("Não foi possível ficar visível", explicarFalha(e)) }
    }

    private fun alternarDescoberta() {
        if (!permissoesOk()) { pedirPermissoes(); return }
        if (procurando) { nearby.stopDiscovery(); procurando = false; repo.registrarLog("parou de procurar"); atualizar(); return }
        val op = DiscoveryOptions.Builder().setStrategy(Strategy.P2P_CLUSTER).build()
        descobertaIniciadaEm = System.currentTimeMillis()
        nearby.startDiscovery(Protocolo.ID_SERVICO, descoberta, op)
            .addOnSuccessListener { procurando = true; repo.registrarLog("procurando aparelhos"); atualizar() }
            .addOnFailureListener { e -> repo.registrarLog("falha ao procurar: ${e.message}"); aviso("Não foi possível procurar", explicarFalha(e)) }
    }

    private fun explicarFalha(e: Exception): String =
        "${e.message}\n\nVerifique: Bluetooth ligado, Localização ligada, permissões concedidas. A mensagem continua salva neste aparelho."

    private fun solicitarConexao(endpointId: String, nome: String) {
        pedidoConexaoEm[endpointId] = System.currentTimeMillis()
        nearby.requestConnection(nomeAparelho, endpointId, ciclo)
            .addOnFailureListener { e -> repo.registrarLog("falha ao solicitar conexão a $nome: ${e.message}"); atualizar() }
    }

    private fun pararTudo() {
        nearby.stopAllEndpoints(); nearby.stopAdvertising(); nearby.stopDiscovery()
        anunciando = false; procurando = false; conectados.clear(); encontrados.clear()
        repo.registrarLog("parou tudo")
        atualizar()
    }

    // ---------------- Envio ----------------
    private fun criarPedidoFicticio() {
        val ocorrencia = UUID.randomUUID().toString()
        val dados = JSONObject()
            .put("ficticio", true).put("quantidadePessoas", 4).put("idades", JSONArray(listOf(3, 34, 78)))
            .put("aguaSubindo", true).put("ilhado", true).put("mobilidadeReduzida", true)
            .put("descricao", "[FICTÍCIO] Teste de demonstração enviado por $nomeAparelho")
        // Coordenada FICTÍCIA de demonstração (ponto genérico no Recife), nunca a localização real de alguém.
        val m = Protocolo.montar(Protocolo.Tipo.PEDIDO, ocorrencia, remetente, chaveGrupo, dados, "2611606", -8.1262, -34.9478, 30.0)
        val r = Repositorio.Registro(m, "enviada", 1, false, null, JSONArray(), null, null)
        repo.salvar(r)
        repo.evento(r, "Nível 1 — salvo neste aparelho (${if (temInternet()) "com" else "sem"} internet)")
        if (conectados.isEmpty()) repo.evento(r, "nenhum aparelho conectado — aguardando conexão")
        else conectados.keys.forEach { enviar(it, r) }
        atualizar()
    }

    private fun enviarAvisoDemonstrativo() {
        if (conectados.isEmpty()) { aviso("Nenhum aparelho conectado", "Conecte ao menos um aparelho antes de distribuir o aviso."); return }
        val dados = JSONObject().put("ficticio", true).put("titulo", "[DEMONSTRAÇÃO] Aviso fictício de chuva intensa")
            .put("orientacao", "Teste de distribuição local. Não é Cell Broadcast nem aviso oficial.")
        val m = Protocolo.montar(Protocolo.Tipo.AVISO, UUID.randomUUID().toString(), remetente, chaveGrupo, dados)
        val r = Repositorio.Registro(m, "enviada", 1, false, null, JSONArray(), null, null)
        repo.salvar(r)
        repo.evento(r, "aviso demonstrativo criado; enviando a ${conectados.size} aparelho(s)")
        conectados.keys.forEach { enviar(it, r) }
        atualizar()
    }

    private fun enviarPendentesPara(endpointId: String) {
        repo.enviadasSemConfirmacao().forEach { enviar(endpointId, it) }
    }

    private fun enviar(endpointId: String, r: Repositorio.Registro) {
        val tentativas = r.mensagem.optInt("numeroTentativas", 0) + 1
        r.mensagem.put("numeroTentativas", tentativas) // não entra na integridade
        repo.salvar(r)
        val payload = Payload.fromBytes(r.mensagem.toString().toByteArray(Charsets.UTF_8))
        payloadParaMensagem[payload.id] = r.id
        envioEm[r.id] = System.currentTimeMillis()
        nearby.sendPayload(endpointId, payload)
            .addOnFailureListener { e -> r.ultimoErro = e.message; repo.evento(r, "falha ao enviar para ${conectados[endpointId]}: ${e.message}"); atualizar() }
        repo.evento(r, "tentativa $tentativas: enviando para ${conectados[endpointId] ?: endpointId}")
        val id = r.id
        ui.postDelayed({
            val atual = repo.obter(id)
            if (atual != null && atual.nivel < 2) { repo.evento(atual, "sem confirmação de aplicação em 15 s — mensagem preservada; será reenviada na próxima conexão"); atualizar() }
        }, 15_000)
    }

    // ---------------- Recebimento ----------------
    private fun receber(endpointId: String, m: JSONObject) {
        val origem = conectados[endpointId] ?: endpointId
        val erros = Protocolo.validar(m)
        if (erros.isNotEmpty()) { repo.registrarLog("mensagem inválida de $origem: ${erros.joinToString()}"); return }
        if (!Protocolo.verificar(m, chaveGrupo)) { repo.registrarLog("integridade NÃO confere (grupo diferente ou conteúdo alterado) — descartada; origem $origem"); return }

        if (m.getString("tipoMensagem") == Protocolo.Tipo.CONFIRMACAO) { receberConfirmacao(origem, m); return }

        val id = m.getString("identificadorMensagem")
        val existente = repo.obter(id)
        val r = existente ?: Repositorio.Registro(m, "recebida", 2, caixaGateway.isChecked && m.getString("tipoMensagem") != Protocolo.Tipo.AVISO,
            null, JSONArray(), null, origem)
        if (existente == null) {
            r.mensagem.put("estadoTransmissao", Protocolo.Estado.PROXIMIDADE)
            r.eventos.put(JSONObject().put("em", Protocolo.agoraIso()).put("texto", "recebida de $origem e salva (nível 2)"))
            repo.salvar(r) // persistência ANTES da confirmação
        } else {
            repo.evento(r, "duplicata recebida de $origem — já estava salva; confirmando novamente sem duplicar")
        }
        enviarConfirmacao(endpointId, m, 2, "persistido:${id.take(8)}.json")
        if (r.pendenteGateway) encaminhar(r, endpointId)
        if (m.getString("tipoMensagem") == Protocolo.Tipo.AVISO)
            ui.post { aviso("Aviso DEMONSTRATIVO recebido", m.getJSONObject("dadosEstruturados").optString("titulo") + "\n\nTeste local — não é aviso oficial.") }
        atualizar()
    }

    private fun receberConfirmacao(origem: String, c: JSONObject) {
        val alvo = c.getString("identificadorCorrelacao")
        val r = repo.obter(alvo) ?: run { repo.registrarLog("confirmação para mensagem desconhecida $alvo"); return }
        val nivel = c.getJSONObject("dadosEstruturados").optInt("nivel", 0)
        val ms = envioEm[alvo]?.let { System.currentTimeMillis() - it }
        if (nivel > r.nivel) {
            r.nivel = nivel
            r.mensagem.put("estadoTransmissao", Protocolo.Estado.porNivel(nivel))
            c.getJSONObject("dadosEstruturados").optString("protocolos").takeIf { it.isNotBlank() }?.let { r.protocolos = it }
            r.ultimoErro = null
        }
        repo.evento(r, "confirmação de $origem: ${Protocolo.Estado.descrever(nivel)}${if (nivel == 2 && ms != null) " · ${ms} ms desde o envio" else ""}" +
            (c.getJSONObject("dadosEstruturados").optString("comprovante").takeIf { it.isNotBlank() }?.let { " · comprovante $it" } ?: ""))
        atualizar()
    }

    private fun enviarConfirmacao(endpointId: String, original: JSONObject, nivel: Int, comprovante: String?, protocolos: String? = null) {
        if (!conectados.containsKey(endpointId)) return
        val extra = JSONObject()
        if (protocolos != null) extra.put("protocolos", protocolos)
        val c = Protocolo.confirmacao(original, nivel, remetente, chaveGrupo, comprovante, extra)
        nearby.sendPayload(endpointId, Payload.fromBytes(c.toString().toByteArray(Charsets.UTF_8)))
    }

    // ---------------- Gateway ----------------
    private fun gateway(): Gateway? {
        val url = campoUrl.text.toString(); val chave = campoChave.text.toString()
        if (url.isBlank() || chave.isBlank()) return null
        return Gateway(url, campoReceptor.text.toString().ifBlank { "gateway-poc" }, chave)
    }

    private fun encaminharPendentes() {
        val p = repo.pendentesGateway()
        if (p.isEmpty()) { aviso("Nada pendente", "Não há mensagens aguardando envio ao servidor."); return }
        p.forEach { encaminhar(it, null) }
    }

    private fun encaminhar(r: Repositorio.Registro, endpointOrigem: String?) {
        val g = gateway() ?: run { repo.evento(r, "gateway não configurado (URL/chave) — mensagem preservada"); return }
        if (!temInternet()) { repo.evento(r, "sem internet — aguardando conexão para encaminhar ao servidor (o servidor ainda NÃO recebeu)"); atualizar(); return }
        val recebidoEm = (0 until r.eventos.length()).map { r.eventos.getJSONObject(it) }.firstOrNull { it.getString("texto").startsWith("recebida de") }?.getString("em")
            ?: Protocolo.agoraIso()
        io.execute {
            val inicio = System.currentTimeMillis()
            val resp = g.entregar(r.mensagem, remetente, recebidoEm, "nearby", Build.MODEL)
            val ms = System.currentTimeMillis() - inicio
            ui.post {
                if (resp.status == 200 && resp.corpo != null) {
                    r.pendenteGateway = false
                    r.nivel = maxOf(r.nivel, resp.corpo.optInt("nivelComprovado", 4))
                    r.ultimoErro = null
                    repo.evento(r, "servidor confirmou persistência em $ms ms (${Protocolo.Estado.descrever(r.nivel)})${if (resp.corpo.optBoolean("duplicado")) " — já existia, sem duplicar" else ""}")
                    val alvo = endpointOrigem?.takeIf { conectados.containsKey(it) }
                    if (alvo != null) enviarConfirmacao(alvo, r.mensagem, 4, r.ocorrencia)
                    acompanharCentral(r, alvo, 0)
                } else {
                    r.ultimoErro = resp.erro
                    repo.evento(r, "falha ao encaminhar ao servidor: ${resp.erro} — mensagem preservada")
                }
                atualizar()
            }
        }
    }

    /** Consulta o servidor até a central SIMULADA devolver protocolo (nível 5), por até ~60 s. */
    private fun acompanharCentral(r: Repositorio.Registro, endpointOrigem: String?, tentativa: Int) {
        val g = gateway() ?: return
        if (tentativa > 12) { repo.evento(r, "central simulada ainda sem protocolo após 60 s — consulte o painel"); return }
        io.execute {
            val resp = g.confirmacoes(r.ocorrencia)
            ui.post {
                val nivel = resp.corpo?.optInt("nivelComprovado", 0) ?: 0
                if (nivel >= 5) {
                    val conf = resp.corpo!!.getJSONArray("confirmacoes")
                    val protocolos = (0 until conf.length()).map { conf.getJSONObject(it) }
                        .filter { it.optInt("nivel") == 5 }.joinToString(", ") { "${it.optString("destino")}: ${it.optString("comprovante")}" }
                    r.nivel = 5; r.protocolos = protocolos
                    repo.evento(r, "Nível 5 — central(is) SIMULADA(S) devolveram protocolo: $protocolos")
                    if (endpointOrigem != null && conectados.containsKey(endpointOrigem)) enviarConfirmacao(endpointOrigem, r.mensagem, 5, null, protocolos)
                    atualizar()
                } else ui.postDelayed({ acompanharCentral(r, endpointOrigem, tentativa + 1) }, 5_000)
            }
        }
    }

    // ---------------- Rede ----------------
    private fun temInternet(): Boolean {
        val cm = getSystemService(ConnectivityManager::class.java) ?: return false
        val caps = cm.getNetworkCapabilities(cm.activeNetwork) ?: return false
        return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) && caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
    }

    private fun observarRede() {
        val cm = getSystemService(ConnectivityManager::class.java) ?: return
        cm.registerDefaultNetworkCallback(object : ConnectivityManager.NetworkCallback() {
            override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
                if (caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)) ui.post {
                    val p = repo.pendentesGateway()
                    if (p.isNotEmpty() && caixaGateway.isChecked) { repo.registrarLog("internet voltou — encaminhando ${p.size} pendente(s)"); p.forEach { encaminhar(it, null) } }
                    atualizar()
                }
            }
            override fun onLost(network: Network) { atualizar() }
        })
    }

    // ---------------- Utilidades ----------------
    private fun exportar() {
        val texto = repo.exportarEvidencias()
        startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).apply { type = "application/json"; putExtra(Intent.EXTRA_TEXT, texto) }, "Exportar evidências"))
    }

    private fun aviso(titulo: String, texto: String) {
        AlertDialog.Builder(this).setTitle(titulo).setMessage(texto).setPositiveButton("Entendi", null).show()
    }

    override fun onDestroy() {
        nearby.stopAllEndpoints()
        io.shutdown()
        super.onDestroy()
    }
}
