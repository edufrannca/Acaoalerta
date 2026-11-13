// PROVA DE CONCEITO — NÃO COMPILADA. Ver README.md desta pasta.
// Swift Package: https://github.com/google/nearby (produto NearbyConnections)
import Foundation
import NearbyConnections

/// Mesmo contrato do Android: pacote JSON "alerta-em-acao/gateway/v1", serviceId e estratégia de cluster.
final class NearbyPoC: NSObject, ObservableObject {
    @Published var registro: [String] = []
    private let servico = "br.alertaemacao.nearby.SOCORRO"
    private let apelido = "iphone-" + UUID().uuidString.prefix(6)
    private lazy var gerenciador = ConnectionManager(serviceID: servico, strategy: .cluster)
    private var anunciante: Advertiser?
    private var descobridor: Discoverer?

    private func log(_ s: String) { DispatchQueue.main.async { self.registro.append("\(Date()) \(s)") } }

    /// Modo A: anuncia e envia pedido FICTÍCIO quando conectado.
    func anunciar() {
        gerenciador.delegate = self
        anunciante = Advertiser(connectionManager: gerenciador)
        anunciante?.delegate = self
        anunciante?.startAdvertising(using: Data(apelido.utf8))
        log("Anunciando como \(apelido)")
    }

    /// Modo B: descobre aparelhos e recebe pedidos (gateway).
    func descobrir() {
        gerenciador.delegate = self
        descobridor = Discoverer(connectionManager: gerenciador)
        descobridor?.delegate = self
        descobridor?.startDiscovery()
        log("Procurando aparelhos próximos…")
    }

    func pacoteFicticio() -> Data {
        let p: [String: Any] = [
            "versao": "alerta-em-acao/gateway/v1", "id": UUID().uuidString, "dispositivoOrigem": apelido, "receptor": "",
            "saltos": 1, "criadoEm": ISO8601DateFormatter().string(from: Date()), "latitude": -8.0631, "longitude": -34.8711,
            "precisaoMetros": 50, "pessoas": 2, "aguaSubindo": true, "feridos": false,
            "texto": "PEDIDO FICTÍCIO DE TESTE (PoC Nearby iOS)", "canal": "nearby",
        ]
        return try! JSONSerialization.data(withJSONObject: p)
    }
}

extension NearbyPoC: AdvertiserDelegate, DiscovererDelegate, ConnectionManagerDelegate {
    func advertiser(_ advertiser: Advertiser, didReceiveConnectionRequestFrom endpointID: EndpointID, with context: Data, connectionRequestHandler: @escaping (Bool) -> Void) {
        connectionRequestHandler(true) // PoC: aceita; produção: confirmar código de verificação
    }

    func discoverer(_ discoverer: Discoverer, didFind endpointID: EndpointID, with context: Data) {
        log("Encontrado \(String(decoding: context, as: UTF8.self)); solicitando conexão")
        discoverer.requestConnection(to: endpointID, using: Data(apelido.utf8))
    }

    func discoverer(_ discoverer: Discoverer, didLose endpointID: EndpointID) { log("Perdido \(endpointID)") }

    func connectionManager(_ connectionManager: ConnectionManager, didReceive verificationCode: String, from endpointID: EndpointID, verificationHandler: @escaping (Bool) -> Void) {
        log("Código de verificação \(verificationCode)")
        verificationHandler(true)
    }

    func connectionManager(_ connectionManager: ConnectionManager, didReceive data: Data, withID payloadID: PayloadID, from endpointID: EndpointID) {
        let texto = String(decoding: data, as: UTF8.self)
        if texto.contains("\"tipo\":\"ack\"") { log("ACK recebido do gateway"); return }
        log("Pedido recebido de \(endpointID); armazenando e confirmando")
        let ack = try! JSONSerialization.data(withJSONObject: ["tipo": "ack"])
        _ = connectionManager.send(ack, to: [endpointID])
        // Encaminhamento ao backend: mesmo POST /api/gateway/pacotes do Android (cabeçalho X-Gateway-Chave).
    }

    func connectionManager(_ connectionManager: ConnectionManager, didChangeTo state: ConnectionState, for endpointID: EndpointID) {
        log("Estado da conexão \(endpointID): \(state)")
        if state == .connected && anunciante != nil { _ = connectionManager.send(pacoteFicticio(), to: [endpointID]) }
    }

    func connectionManager(_ connectionManager: ConnectionManager, didReceive stream: InputStream, withID payloadID: PayloadID, from endpointID: EndpointID, cancellationToken token: CancellationToken) {}
    func connectionManager(_ connectionManager: ConnectionManager, didStartReceivingResourceWithID payloadID: PayloadID, from endpointID: EndpointID, at localURL: URL, withName name: String, cancellationToken token: CancellationToken) {}
    func connectionManager(_ connectionManager: ConnectionManager, didReceiveTransferUpdate update: TransferUpdate, from endpointID: EndpointID, forPayload payloadID: PayloadID) {}
}
