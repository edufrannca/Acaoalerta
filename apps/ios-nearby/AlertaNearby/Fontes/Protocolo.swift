import CryptoKit
import Foundation

/// Protocolo alerta-em-acao/proximidade/v2 — mesma especificação do backend (C#) e do Android (Kotlin).
/// Especificação: docs/PROTOCOLO_PROXIMIDADE.md · vetores: docs/protocolo/vetores-v2.json.
/// Mensagens são dicionários JSON ([String: Any]) para preservar exatamente os campos recebidos.
enum Protocolo {
    static let versao = "alerta-em-acao/proximidade/v2"
    /// Igual ao Android: Protocolo.ID_SERVICO. NSBonjourServices = "_6D2B796E03F0._tcp" (SHA-256 deste valor).
    static let idServico = "br.alertaemacao.nearby"
    static let tamanhoMaximo = 32 * 1024
    private static let rotulo = "alerta-em-acao/proximidade/v2"

    enum Tipo {
        static let pedido = "PEDIDO_SOCORRO"
        static let atualizacao = "ATUALIZACAO"
        static let aviso = "AVISO_DEMONSTRATIVO"
        static let confirmacao = "CONFIRMACAO"
        static let todos: Set<String> = [pedido, atualizacao, aviso, confirmacao]
    }

    enum Estado {
        static let salvo = "SALVO_NO_APARELHO"
        static let proximidade = "RECEBIDO_POR_PROXIMIDADE"
        static let gateway = "RECEBIDO_PELO_GATEWAY"
        static let servidor = "RECEBIDO_PELO_SERVIDOR"
        static let central = "RECEBIDO_PELA_CENTRAL_SIMULADA"
        static let falhou = "FALHOU"
        static let todos: Set<String> = [salvo, proximidade, gateway, servidor, central, falhou]

        static func nivel(_ e: String?) -> Int {
            switch e {
            case salvo: return 1
            case proximidade: return 2
            case gateway: return 3
            case servidor: return 4
            case central: return 5
            default: return 0
            }
        }

        static func porNivel(_ n: Int) -> String {
            switch n {
            case 1: return salvo
            case 2: return proximidade
            case 3: return gateway
            case 4: return servidor
            case 5: return central
            default: return falhou
            }
        }

        static func descrever(_ n: Int) -> String {
            switch n {
            case 1: return "Nível 1 — salvo neste aparelho"
            case 2: return "Nível 2 — recebido e salvo por aparelho próximo"
            case 3: return "Nível 3 — aceito pelo gateway para encaminhamento"
            case 4: return "Nível 4 — recebido pelo servidor do Alerta em Ação"
            case 5: return "Nível 5 — recebido pela central SIMULADA (protocolo de demonstração)"
            default: return "Sem confirmação"
            }
        }
    }

    static func chaveGrupo(_ codigo: String) -> Data {
        let normalizado = codigo.trimmingCharacters(in: .whitespacesAndNewlines).uppercased(with: Locale(identifier: "en_US_POSIX"))
        return hmac(chave: Data(rotulo.utf8), dados: Data(normalizado.utf8))
    }

    static func pseudonimizar(_ instalacao: String) -> String {
        let h = SHA256.hash(data: Data(instalacao.utf8)).map { String(format: "%02x", $0) }.joined()
        return "ap-" + String(h.prefix(16))
    }

    static func agoraISO() -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(identifier: "UTC")
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'"
        return f.string(from: Date())
    }

    // MARK: - Forma canônica (numeroTentativas e estadoTransmissao ficam de fora)

    static func canonica(_ m: [String: Any]) -> String {
        var s = ""
        func linha(_ v: String?) { s += (v ?? "") + "\n" }
        func texto(_ k: String) -> String? { m[k] as? String }
        linha(texto("versaoProtocolo")); linha(texto("identificadorMensagem")); linha(texto("identificadorOcorrencia"))
        linha(texto("tipoMensagem")); linha(texto("municipio"))
        let c = m["coordenadasAutorizadas"] as? [String: Any]
        linha(c.flatMap { numero($0["latitude"]) }.map { String(format: "%.6f", $0) })
        linha(c.flatMap { numero($0["longitude"]) }.map { String(format: "%.6f", $0) })
        linha(c.flatMap { numero($0["precisaoMetros"]) }.map { String(Int64(($0).rounded(.toNearestOrAwayFromZero))) })
        linha(texto("horarioCriacao")); linha(texto("horarioUltimaAtualizacao")); linha(texto("remetentePseudonimizado"))
        linha(texto("identificadorCorrelacao"))
        if let d = m["dadosEstruturados"] as? [String: Any] {
            // Chaves ASCII: ordenação igual à ordinal do C#/Kotlin.
            for k in d.keys.sorted(by: { Array($0.utf16).lexicographicallyPrecedes(Array($1.utf16)) }) {
                if let v = valor(d[k]) { s += "\(k)=\(v)\n" }
            }
        }
        return s
    }

    private static func ehBooleano(_ n: NSNumber) -> Bool { CFGetTypeID(n) == CFBooleanGetTypeID() }

    private static func ehInteiro(_ n: NSNumber) -> Bool {
        let t = String(cString: n.objCType)
        if ["c", "C", "s", "S", "i", "I", "l", "L", "q", "Q"].contains(t) { return true }
        return n.doubleValue.rounded() == n.doubleValue && abs(n.doubleValue) < 1e15
    }

    private static func numero(_ v: Any?) -> Double? {
        guard let n = v as? NSNumber, !ehBooleano(n) else { return nil }
        return n.doubleValue
    }

    private static func valor(_ v: Any?) -> String? {
        guard let v = v, !(v is NSNull) else { return nil }
        if let n = v as? NSNumber {
            if ehBooleano(n) { return n.boolValue ? "true" : "false" }
            if ehInteiro(n) { return String(n.int64Value) }
            return String(format: "%.6f", n.doubleValue)
        }
        if let t = v as? String { return escapar(t) }
        if let a = v as? [Any] {
            return a.map { x -> String in
                if let n = x as? NSNumber {
                    if ehBooleano(n) { return n.boolValue ? "true" : "false" }
                    return ehInteiro(n) ? String(n.int64Value) : ""
                }
                if let t = x as? String { return escapar(t).replacingOccurrences(of: ",", with: "\\,") }
                return ""
            }.joined(separator: ",")
        }
        return nil
    }

    private static func escapar(_ s: String) -> String {
        s.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\n", with: "\\n").replacingOccurrences(of: "\r", with: "\\r")
    }

    static func integridade(_ m: [String: Any], chave: Data) -> String {
        let mac = hmac(chave: chave, dados: Data(canonica(m).utf8))
        let b64 = mac.base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
        return "hmac-sha256:" + b64
    }

    static func verificar(_ m: [String: Any], chave: Data) -> Bool {
        guard let recebida = m["integridadeMensagem"] as? String else { return false }
        let esperada = integridade(m, chave: chave)
        // Comparação em tempo constante.
        let a = Array(recebida.utf8), b = Array(esperada.utf8)
        guard a.count == b.count else { return false }
        var dif: UInt8 = 0
        for i in 0..<a.count { dif |= a[i] ^ b[i] }
        return dif == 0
    }

    static func validar(_ m: [String: Any]) -> [String] {
        var e: [String] = []
        func t(_ k: String) -> String { (m[k] as? String) ?? "" }
        if t("versaoProtocolo") != versao { e.append("versaoProtocolo não suportada") }
        if UUID(uuidString: t("identificadorMensagem")) == nil { e.append("identificadorMensagem deve ser UUID") }
        if UUID(uuidString: t("identificadorOcorrencia")) == nil { e.append("identificadorOcorrencia deve ser UUID") }
        if !Tipo.todos.contains(t("tipoMensagem")) { e.append("tipoMensagem inválido") }
        if let est = m["estadoTransmissao"] as? String, !Estado.todos.contains(est) { e.append("estadoTransmissao inválido") }
        if !t("horarioCriacao").hasSuffix("Z") { e.append("horarioCriacao deve terminar em Z") }
        if !t("horarioUltimaAtualizacao").hasSuffix("Z") { e.append("horarioUltimaAtualizacao deve terminar em Z") }
        let rem = t("remetentePseudonimizado")
        if rem.isEmpty || rem.count > 64 { e.append("remetentePseudonimizado inválido") }
        if t("tipoMensagem") == Tipo.confirmacao && UUID(uuidString: t("identificadorCorrelacao")) == nil {
            e.append("CONFIRMACAO exige identificadorCorrelacao")
        }
        if t("integridadeMensagem").isEmpty { e.append("integridadeMensagem ausente") }
        return e
    }

    /// Monta e assina. Coordenadas arredondadas a 6 casas antes da assinatura.
    static func montar(tipo: String, ocorrencia: String, remetente: String, chave: Data, dados: [String: Any]?,
                       municipio: String? = nil, latitude: Double? = nil, longitude: Double? = nil, precisao: Double? = nil,
                       correlacao: String? = nil, estado: String = Estado.salvo) -> [String: Any] {
        var m: [String: Any] = [
            "versaoProtocolo": versao,
            "identificadorMensagem": UUID().uuidString.lowercased(),
            "identificadorOcorrencia": ocorrencia,
            "tipoMensagem": tipo,
            "horarioCriacao": agoraISO(),
            "horarioUltimaAtualizacao": agoraISO(),
            "remetentePseudonimizado": remetente,
            "numeroTentativas": 0,
            "estadoTransmissao": estado,
        ]
        if let municipio = municipio { m["municipio"] = municipio }
        if let la = latitude, let lo = longitude {
            var c: [String: Any] = ["latitude": (la * 1e6).rounded() / 1e6, "longitude": (lo * 1e6).rounded() / 1e6]
            if let p = precisao { c["precisaoMetros"] = p.rounded() }
            m["coordenadasAutorizadas"] = c
        }
        if let dados = dados { m["dadosEstruturados"] = dados }
        if let correlacao = correlacao { m["identificadorCorrelacao"] = correlacao }
        m["integridadeMensagem"] = integridade(m, chave: chave)
        return m
    }

    static func confirmacao(de original: [String: Any], nivel: Int, remetente: String, chave: Data,
                            comprovante: String?, protocolos: String? = nil) -> [String: Any] {
        var d: [String: Any] = ["nivel": nivel]
        if let c = comprovante { d["comprovante"] = c }
        if let p = protocolos { d["protocolos"] = p }
        return montar(tipo: Tipo.confirmacao, ocorrencia: (original["identificadorOcorrencia"] as? String) ?? "",
                      remetente: remetente, chave: chave, dados: d,
                      correlacao: original["identificadorMensagem"] as? String, estado: Estado.porNivel(nivel))
    }

    static func codificar(_ m: [String: Any]) -> Data? { try? JSONSerialization.data(withJSONObject: m, options: []) }

    static func decodificar(_ d: Data) -> [String: Any]? {
        guard d.count <= tamanhoMaximo else { return nil }
        return (try? JSONSerialization.jsonObject(with: d, options: [])) as? [String: Any]
    }

    static func hmac(chave: Data, dados: Data) -> Data {
        Data(HMAC<SHA256>.authenticationCode(for: dados, using: SymmetricKey(data: chave)))
    }

    static func hex(_ d: Data) -> String { d.map { String(format: "%02x", $0) }.joined() }
}
