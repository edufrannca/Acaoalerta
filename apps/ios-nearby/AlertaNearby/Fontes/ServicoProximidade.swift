import Foundation
import Network
import NearbyConnections
import UIKit

/// Comunicação por proximidade com o SDK oficial google/nearby (NearbyConnections), AMBIENTE DE DEMONSTRAÇÃO.
/// Mesmo serviceID e estratégia (.cluster ↔ P2P_CLUSTER) do Android. Dados sempre fictícios.
final class ServicoProximidade: NSObject, ObservableObject {
    struct Verificacao: Identifiable {
        let id = UUID()
        let endpoint: EndpointID
        let nome: String
        let codigo: String
        let responder: (Bool) -> Void
    }

    @Published var encontrados: [EndpointID: String] = [:]
    @Published var conectados: [EndpointID: String] = [:]
    @Published var anunciando = false
    @Published var procurando = false
    @Published var internet = false
    @Published var verificacao: Verificacao?
    @Published var alerta: String?
    @Published var registros: [Repositorio.Registro] = []
    @Published var log: [String] = []

    let repo = Repositorio()
    private var gerenciador: ConnectionManager!
    private var anunciante: Advertiser!
    private var descobridor: Discoverer!
    private let monitor = NWPathMonitor()
    private var nomesPendentes: [EndpointID: String] = [:]
    private var pedidoConexaoEm: [EndpointID: Date] = [:]
    private var envioEm: [String: Date] = [:]
    private var cargaParaMensagem: [PayloadID: String] = [:]
    private var descobertaIniciadaEm = Date()

    var configuracao = Configuracao.carregar()

    private var remetente: String { Protocolo.pseudonimizar(configuracao.instalacao) }
    private var chave: Data { Protocolo.chaveGrupo(configuracao.codigoGrupo.isEmpty ? "ALERTA-DEMO-2026" : configuracao.codigoGrupo) }
    var identificacao: String { "\(configuracao.nome) (\(remetente))" }

    override init() {
        super.init()
        gerenciador = ConnectionManager(serviceID: Protocolo.idServico, strategy: .cluster)
        gerenciador.delegate = self
        anunciante = Advertiser(connectionManager: gerenciador)
        anunciante.delegate = self
        descobridor = Discoverer(connectionManager: gerenciador)
        descobridor.delegate = self
        monitor.pathUpdateHandler = { [weak self] caminho in
            DispatchQueue.main.async {
                guard let self = self else { return }
                let antes = self.internet
                self.internet = caminho.status == .satisfied
                if !antes && self.internet && self.configuracao.atuarComoGateway {
                    let p = self.repo.pendentesGateway()
                    if !p.isEmpty { self.registrarLog("internet voltou — encaminhando \(p.count) pendente(s)"); p.forEach { self.encaminhar($0, origem: nil) } }
                }
            }
        }
        monitor.start(queue: DispatchQueue.global(qos: .utility))
        atualizar()
    }

    func atualizar() {
        registros = repo.todos().filter { $0.tipo != Protocolo.Tipo.confirmacao }
        log = repo.ultimasLinhasLog(40)
    }

    private func registrarLog(_ t: String) { repo.registrarLog(t); atualizar() }

    // MARK: - Ações

    func alternarAnuncio() {
        if anunciando { anunciante.stopAdvertising(); anunciando = false; registrarLog("parou de anunciar"); return }
        anunciante.startAdvertising(using: Data(configuracao.nome.utf8)) { [weak self] erro in
            DispatchQueue.main.async {
                if let erro = erro { self?.registrarLog("falha ao anunciar: \(erro.localizedDescription)"); self?.alerta = "Não foi possível ficar visível: \(erro.localizedDescription). Verifique Bluetooth e a permissão de Rede Local." }
                else { self?.anunciando = true; self?.registrarLog("anunciando como \(self?.configuracao.nome ?? "")") }
            }
        }
    }

    func alternarDescoberta() {
        if procurando { descobridor.stopDiscovery(); procurando = false; registrarLog("parou de procurar"); return }
        descobertaIniciadaEm = Date()
        descobridor.startDiscovery { [weak self] erro in
            DispatchQueue.main.async {
                if let erro = erro { self?.registrarLog("falha ao procurar: \(erro.localizedDescription)"); self?.alerta = "Não foi possível procurar: \(erro.localizedDescription)" }
                else { self?.procurando = true; self?.registrarLog("procurando aparelhos") }
            }
        }
    }

    func conectar(_ endpoint: EndpointID) {
        pedidoConexaoEm[endpoint] = Date()
        nomesPendentes[endpoint] = encontrados[endpoint]
        descobridor.requestConnection(to: endpoint, using: Data(configuracao.nome.utf8)) { [weak self] erro in
            if let erro = erro { DispatchQueue.main.async { self?.registrarLog("falha ao solicitar conexão: \(erro.localizedDescription)") } }
        }
    }

    func pararTudo() {
        anunciante.stopAdvertising(); descobridor.stopDiscovery()
        conectados.keys.forEach { gerenciador.disconnect(from: $0) }
        anunciando = false; procurando = false; conectados = [:]; encontrados = [:]
        registrarLog("parou tudo")
    }

    func criarPedidoFicticio() {
        let dados: [String: Any] = [
            "ficticio": true, "quantidadePessoas": 3, "idades": [6, 41, 80], "aguaSubindo": true, "acessoBloqueado": true,
            "descricao": "[FICTÍCIO] Teste de demonstração enviado por \(configuracao.nome)",
        ]
        // Coordenada FICTÍCIA de demonstração (ponto genérico em Olinda), nunca a localização real de alguém.
        let m = Protocolo.montar(tipo: Protocolo.Tipo.pedido, ocorrencia: UUID().uuidString.lowercased(), remetente: remetente, chave: chave,
                                 dados: dados, municipio: "2609600", latitude: -8.0089, longitude: -34.8553, precisao: 30)
        var r = Repositorio.Registro(mensagem: m, direcao: "enviada", nivel: 1, pendenteGateway: false, ultimoErro: nil, eventos: [], protocolos: nil, origem: nil)
        do { try repo.salvar(r) } catch { alerta = "Falha ao salvar no aparelho: \(error.localizedDescription)"; return }
        repo.evento(&r, "Nível 1 — salvo neste aparelho (\(internet ? "com" : "sem") internet)")
        if conectados.isEmpty { repo.evento(&r, "nenhum aparelho conectado — aguardando conexão") }
        else { conectados.keys.forEach { enviar(r, para: $0) } }
        atualizar()
    }

    /// Pedido vindo da PWA deste aparelho. Mantém id, horário e GPS do emissor; não cria um segundo pedido.
    func importarDaPwa(_ url: URL) {
        guard let p = ImportacaoPwa.decodificar(url) else { alerta = "O link não contém um pedido válido da PWA Alerta em Ação."; return }
        if repo.todos().contains(where: { $0.ocorrencia == p.ocorrencia && $0.direcao == "enviada" }) {
            alerta = "Este pedido já foi importado. Conecte-se a um aparelho próximo para transmitir."; return
        }
        let m = Protocolo.montar(tipo: Protocolo.Tipo.pedido, ocorrencia: p.ocorrencia, remetente: p.remetente, chave: chave, dados: p.dados,
                                 latitude: p.lat, longitude: p.lon, precisao: p.precisao, criadoEm: p.criadoEm)
        var r = Repositorio.Registro(mensagem: m, direcao: "enviada", nivel: 1, pendenteGateway: false, ultimoErro: nil, eventos: [], protocolos: nil, origem: nil)
        do { try repo.salvar(r) } catch { alerta = "Falha ao salvar no aparelho: \(error.localizedDescription)"; return }
        let local = p.lat == nil ? "localização não informada" : "posição do emissor ±\(Int(p.precisao ?? 0)) m"
        repo.evento(&r, "Nível 1 — pedido da PWA importado neste aparelho (mesmo identificador; \(local))")
        if conectados.isEmpty { repo.evento(&r, "procurando aparelho — toque em Procurar aparelhos próximos") }
        else { conectados.keys.forEach { enviar(r, para: $0) } }
        atualizar()
    }

    /// Última posição conhecida DESTE aparelho (gateway), se o usuário autorizou. Vai separada da do emissor.
    var posicaoDesteAparelho: (() -> [String: Any]?)?

    func enviarAvisoDemonstrativo() {
        guard !conectados.isEmpty else { alerta = "Conecte ao menos um aparelho antes de distribuir o aviso."; return }
        let dados: [String: Any] = ["ficticio": true, "titulo": "[DEMONSTRAÇÃO] Aviso fictício de chuva intensa",
                                    "orientacao": "Teste de distribuição local. Não é Cell Broadcast nem aviso oficial."]
        let m = Protocolo.montar(tipo: Protocolo.Tipo.aviso, ocorrencia: UUID().uuidString.lowercased(), remetente: remetente, chave: chave, dados: dados)
        var r = Repositorio.Registro(mensagem: m, direcao: "enviada", nivel: 1, pendenteGateway: false, ultimoErro: nil, eventos: [], protocolos: nil, origem: nil)
        try? repo.salvar(r)
        repo.evento(&r, "aviso demonstrativo criado; enviando a \(conectados.count) aparelho(s)")
        conectados.keys.forEach { enviar(r, para: $0) }
        atualizar()
    }

    func encaminharPendentes() {
        let p = repo.pendentesGateway()
        if p.isEmpty { alerta = "Não há mensagens aguardando envio ao servidor."; return }
        p.forEach { encaminhar($0, origem: nil) }
    }

    // MARK: - Envio

    private func enviar(_ registro: Repositorio.Registro, para endpoint: EndpointID) {
        var r = registro
        let tentativas = ((r.mensagem["numeroTentativas"] as? Int) ?? 0) + 1
        r.mensagem["numeroTentativas"] = tentativas // fora da integridade
        try? repo.salvar(r)
        guard let dados = Protocolo.codificar(r.mensagem) else { return }
        let pid = PayloadID.random(in: 1...Int64.max)
        cargaParaMensagem[pid] = r.id
        envioEm[r.id] = Date()
        _ = gerenciador.send(dados, to: [endpoint], id: pid) { [weak self] erro in
            guard let self = self, let erro = erro else { return }
            DispatchQueue.main.async {
                if var atual = self.repo.obter(r.id) { atual.ultimoErro = erro.localizedDescription; self.repo.evento(&atual, "falha ao enviar: \(erro.localizedDescription)"); self.atualizar() }
            }
        }
        repo.evento(&r, "tentativa \(tentativas): enviando para \(conectados[endpoint] ?? endpoint)")
        let id = r.id
        DispatchQueue.main.asyncAfter(deadline: .now() + 15) { [weak self] in
            guard let self = self, var atual = self.repo.obter(id), atual.nivel < 2 else { return }
            self.repo.evento(&atual, "sem confirmação de aplicação em 15 s — mensagem preservada; será reenviada na próxima conexão")
            self.atualizar()
        }
    }

    private func enviarConfirmacao(_ original: [String: Any], nivel: Int, para endpoint: EndpointID, comprovante: String?, protocolos: String? = nil) {
        guard conectados[endpoint] != nil else { return }
        let c = Protocolo.confirmacao(de: original, nivel: nivel, remetente: remetente, chave: chave, comprovante: comprovante, protocolos: protocolos)
        if let d = Protocolo.codificar(c) { _ = gerenciador.send(d, to: [endpoint]) }
    }

    // MARK: - Recebimento

    private func receber(_ dados: Data, de endpoint: EndpointID) {
        let origem = conectados[endpoint] ?? endpoint
        guard let m = Protocolo.decodificar(dados) else { registrarLog("carga inválida ou acima do limite — descartada"); return }
        let erros = Protocolo.validar(m)
        guard erros.isEmpty else { registrarLog("mensagem inválida de \(origem): \(erros.joined(separator: ", "))"); return }
        guard Protocolo.verificar(m, chave: chave) else { registrarLog("integridade NÃO confere (grupo diferente ou conteúdo alterado) — descartada; origem \(origem)"); return }

        let tipo = (m["tipoMensagem"] as? String) ?? ""
        if tipo == Protocolo.Tipo.confirmacao { receberConfirmacao(m, de: origem); return }

        let id = (m["identificadorMensagem"] as? String) ?? ""
        var r: Repositorio.Registro
        if let existente = repo.obter(id) {
            r = existente
            repo.evento(&r, "duplicata recebida de \(origem) — já estava salva; confirmando novamente sem duplicar")
        } else {
            var msg = m
            msg["estadoTransmissao"] = Protocolo.Estado.proximidade
            r = Repositorio.Registro(mensagem: msg, direcao: "recebida", nivel: 2,
                                     pendenteGateway: configuracao.atuarComoGateway && tipo != Protocolo.Tipo.aviso,
                                     ultimoErro: nil, eventos: [["em": Protocolo.agoraISO(), "texto": "recebida de \(origem) e salva (nível 2)"]],
                                     protocolos: nil, origem: origem)
            do { try repo.salvar(r) } catch { registrarLog("falha ao salvar mensagem recebida: \(error.localizedDescription) — sem confirmação"); return }
        }
        enviarConfirmacao(m, nivel: 2, para: endpoint, comprovante: "persistido:\(id.prefix(8)).json")
        if r.pendenteGateway { encaminhar(r, origem: endpoint) }
        if tipo == Protocolo.Tipo.aviso {
            let titulo = ((m["dadosEstruturados"] as? [String: Any])?["titulo"] as? String) ?? ""
            alerta = "Aviso DEMONSTRATIVO recebido: \(titulo)\n\nTeste local — não é aviso oficial."
        }
        atualizar()
    }

    private func receberConfirmacao(_ c: [String: Any], de origem: String) {
        guard let alvo = c["identificadorCorrelacao"] as? String, var r = repo.obter(alvo) else { registrarLog("confirmação para mensagem desconhecida"); return }
        let d = (c["dadosEstruturados"] as? [String: Any]) ?? [:]
        let nivel = (d["nivel"] as? Int) ?? 0
        let ms = envioEm[alvo].map { Int(Date().timeIntervalSince($0) * 1000) }
        if nivel > r.nivel {
            r.nivel = nivel
            r.mensagem["estadoTransmissao"] = Protocolo.Estado.porNivel(nivel)
            if let p = d["protocolos"] as? String, !p.isEmpty { r.protocolos = p }
            r.ultimoErro = nil
        }
        var texto = "confirmação de \(origem): \(Protocolo.Estado.descrever(nivel))"
        if nivel == 2, let ms = ms { texto += " · \(ms) ms desde o envio" }
        if let comp = d["comprovante"] as? String { texto += " · comprovante \(comp)" }
        repo.evento(&r, texto)
        atualizar()
    }

    // MARK: - Gateway

    private func gateway() -> Gateway? {
        guard !configuracao.urlServidor.isEmpty, !configuracao.chaveGateway.isEmpty else { return nil }
        return Gateway(urlBase: configuracao.urlServidor, receptor: configuracao.receptor.isEmpty ? "gateway-ios" : configuracao.receptor, chave: configuracao.chaveGateway)
    }

    private func encaminhar(_ registro: Repositorio.Registro, origem: EndpointID?) {
        var r = registro
        guard let g = gateway() else { repo.evento(&r, "gateway não configurado (URL/chave) — mensagem preservada"); atualizar(); return }
        guard internet else { repo.evento(&r, "sem internet — aguardando conexão para encaminhar (o servidor ainda NÃO recebeu)"); atualizar(); return }
        let recebidoEm = r.eventos.first(where: { ($0["texto"] ?? "").hasPrefix("recebida de") })?["em"] ?? Protocolo.agoraISO()
        let inicio = Date()
        g.entregar(r.mensagem, receptorPseudonimo: remetente, recebidoEm: recebidoEm, modelo: UIDevice.current.model, posicaoReceptor: posicaoDesteAparelho?()) { [weak self] resp in
            guard let self = self, var atual = self.repo.obter(r.id) else { return }
            let ms = Int(Date().timeIntervalSince(inicio) * 1000)
            if resp.status == 200, let corpo = resp.corpo {
                atual.pendenteGateway = false
                atual.nivel = max(atual.nivel, (corpo["nivelComprovado"] as? Int) ?? 4)
                atual.ultimoErro = nil
                let dup = (corpo["duplicado"] as? Bool) == true ? " — já existia, sem duplicar" : ""
                self.repo.evento(&atual, "servidor confirmou persistência em \(ms) ms (\(Protocolo.Estado.descrever(atual.nivel)))\(dup)")
                if let o = origem { self.enviarConfirmacao(atual.mensagem, nivel: 4, para: o, comprovante: atual.ocorrencia) }
                self.acompanharCentral(atual.id, origem: origem, tentativa: 0)
            } else {
                atual.ultimoErro = resp.erro
                self.repo.evento(&atual, "falha ao encaminhar ao servidor: \(resp.erro ?? "erro") — mensagem preservada")
            }
            self.atualizar()
        }
    }

    /// Consulta o servidor até a central SIMULADA devolver protocolo (nível 5), por até ~60 s.
    private func acompanharCentral(_ id: String, origem: EndpointID?, tentativa: Int) {
        guard let g = gateway(), let r = repo.obter(id) else { return }
        if tentativa > 12 { var a = r; repo.evento(&a, "central simulada ainda sem protocolo após 60 s — consulte o painel"); atualizar(); return }
        g.confirmacoes(r.ocorrencia) { [weak self] resp in
            guard let self = self, var atual = self.repo.obter(id) else { return }
            let nivel = (resp.corpo?["nivelComprovado"] as? Int) ?? 0
            if nivel >= 5, let conf = resp.corpo?["confirmacoes"] as? [[String: Any]] {
                let protocolos = conf.filter { ($0["nivel"] as? Int) == 5 }
                    .map { "\(($0["destino"] as? String) ?? ""): \(($0["comprovante"] as? String) ?? "")" }.joined(separator: ", ")
                atual.nivel = 5; atual.protocolos = protocolos
                self.repo.evento(&atual, "Nível 5 — central(is) SIMULADA(S) devolveram protocolo: \(protocolos)")
                if let o = origem { self.enviarConfirmacao(atual.mensagem, nivel: 5, para: o, comprovante: nil, protocolos: protocolos) }
                self.atualizar()
            } else {
                DispatchQueue.main.asyncAfter(deadline: .now() + 5) { self.acompanharCentral(id, origem: origem, tentativa: tentativa + 1) }
            }
        }
    }
}

// MARK: - Delegados do Nearby

extension ServicoProximidade: AdvertiserDelegate {
    func advertiser(_ advertiser: Advertiser, didReceiveConnectionRequestFrom endpointID: EndpointID, with context: Data,
                    connectionRequestHandler: @escaping (Bool) -> Void) {
        let nome = String(data: context, encoding: .utf8) ?? endpointID
        nomesPendentes[endpointID] = nome
        registrarLog("pedido de conexão recebido de \(nome)")
        // A confiança é decidida na verificação do código (abaixo), nos dois aparelhos.
        connectionRequestHandler(true)
    }
}

extension ServicoProximidade: DiscovererDelegate {
    func discoverer(_ discoverer: Discoverer, didFind endpointID: EndpointID, with context: Data) {
        let nome = String(data: context, encoding: .utf8) ?? endpointID
        encontrados[endpointID] = nome
        registrarLog("encontrado \(nome) em \(Int(Date().timeIntervalSince(descobertaIniciadaEm) * 1000)) ms após iniciar a busca")
    }

    func discoverer(_ discoverer: Discoverer, didLose endpointID: EndpointID) {
        registrarLog("aparelho fora de alcance: \(encontrados[endpointID] ?? endpointID)")
        encontrados[endpointID] = nil
    }
}

extension ServicoProximidade: ConnectionManagerDelegate {
    func connectionManager(_ connectionManager: ConnectionManager, didReceive verificationCode: String, from endpointID: EndpointID,
                           verificationHandler: @escaping (Bool) -> Void) {
        let nome = nomesPendentes[endpointID] ?? encontrados[endpointID] ?? endpointID
        registrarLog("código de verificação com \(nome): \(verificationCode)")
        verificacao = Verificacao(endpoint: endpointID, nome: nome, codigo: verificationCode) { [weak self] aceito in
            verificationHandler(aceito)
            if !aceito { self?.registrarLog("pareamento RECUSADO com \(nome)") }
        }
    }

    func connectionManager(_ connectionManager: ConnectionManager, didReceive data: Data, withID payloadID: PayloadID, from endpointID: EndpointID) {
        receber(data, de: endpointID)
    }

    func connectionManager(_ connectionManager: ConnectionManager, didReceive stream: InputStream, withID payloadID: PayloadID,
                           from endpointID: EndpointID, cancellationToken token: CancellationToken) {
        token.cancel() // o protocolo usa apenas cargas de bytes
    }

    func connectionManager(_ connectionManager: ConnectionManager, didStartReceivingResourceWithID payloadID: PayloadID, from endpointID: EndpointID,
                           at localURL: URL, withName name: String, cancellationToken token: CancellationToken) {
        token.cancel()
    }

    func connectionManager(_ connectionManager: ConnectionManager, didReceiveTransferUpdate update: TransferUpdate, from endpointID: EndpointID,
                           forPayload payloadID: PayloadID) {
        guard let id = cargaParaMensagem[payloadID], var r = repo.obter(id) else { return }
        switch update {
        case .success:
            cargaParaMensagem[payloadID] = nil
            let ms = envioEm[id].map { Int(Date().timeIntervalSince($0) * 1000) }
            // Sucesso do SDK NÃO é entrega: aguardamos a confirmação de aplicação do receptor.
            repo.evento(&r, "bytes transferidos pelo Nearby\(ms.map { " em \($0) ms" } ?? "") — aguardando confirmação do receptor")
        case .failure, .canceled:
            cargaParaMensagem[payloadID] = nil
            r.ultimoErro = "transmissão interrompida"
            repo.evento(&r, "transmissão interrompida — mensagem preservada para nova tentativa")
        case .progress:
            break
        }
        atualizar()
    }

    func connectionManager(_ connectionManager: ConnectionManager, didChangeTo state: ConnectionState, for endpointID: EndpointID) {
        let nome = nomesPendentes[endpointID] ?? encontrados[endpointID] ?? endpointID
        switch state {
        case .connecting:
            registrarLog("conectando a \(nome)")
        case .connected:
            conectados[endpointID] = nome
            let ms = pedidoConexaoEm.removeValue(forKey: endpointID).map { Int(Date().timeIntervalSince($0) * 1000) }
            registrarLog("conectado a \(nome)\(ms.map { " em \($0) ms" } ?? "")")
            repo.enviadasSemConfirmacao().forEach { enviar($0, para: endpointID) }
        case .disconnected:
            conectados[endpointID] = nil
            registrarLog("desconectado de \(nome)")
        case .rejected:
            conectados[endpointID] = nil
            registrarLog("conexão com \(nome) rejeitada")
        }
    }
}

/// Configuração digitada no aparelho (UserDefaults). A chave do gateway nunca é embutida no app.
struct Configuracao {
    var nome: String
    var codigoGrupo: String
    var urlServidor: String
    var receptor: String
    var chaveGateway: String
    var atuarComoGateway: Bool
    let instalacao: String

    static func carregar() -> Configuracao {
        let d = UserDefaults.standard
        let inst = d.string(forKey: "instalacao") ?? {
            let novo = UUID().uuidString
            d.set(novo, forKey: "instalacao")
            return novo
        }()
        return Configuracao(nome: d.string(forKey: "nome") ?? "iPhone \(UIDevice.current.name.prefix(12))",
                            codigoGrupo: d.string(forKey: "grupo") ?? "ALERTA-DEMO-2026",
                            urlServidor: d.string(forKey: "url") ?? "", receptor: d.string(forKey: "receptor") ?? "gateway-ios",
                            chaveGateway: d.string(forKey: "chave") ?? "", atuarComoGateway: d.object(forKey: "gateway") as? Bool ?? true,
                            instalacao: inst)
    }

    func salvar() {
        let d = UserDefaults.standard
        d.set(nome, forKey: "nome"); d.set(codigoGrupo, forKey: "grupo"); d.set(urlServidor, forKey: "url")
        d.set(receptor, forKey: "receptor"); d.set(chaveGateway, forKey: "chave"); d.set(atuarComoGateway, forKey: "gateway")
    }
}
