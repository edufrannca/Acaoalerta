import Foundation

/// Converte o pedido criado na PWA (link alertaemacao://importar#p=…) em mensagem do protocolo v2.
/// O app nativo NÃO lê o IndexedDB do Safari: a PWA entrega o pedido por este link.
/// Preserva identificador, horário e posição do EMISSOR; o remetente é o pseudônimo calculado pela PWA,
/// para que o servidor reconheça o dono quando a PWA sincronizar (sem duplicar).
enum ImportacaoPwa {
    static let versao = "aea-qr/1"

    // Nomes da PWA → nomes de dadosEstruturados do protocolo v2.
    static let marcas: [String: String] = [
        "ilhado": "ilhado", "aguaSubindo": "aguaSubindo", "correnteza": "correnteza", "riscoDeslizamento": "riscoDeslizamento",
        "desmoronamento": "desmoronamento", "barreiraComSinaisInstabilidade": "barreiraInstavel", "acessoBloqueado": "acessoBloqueado",
        "equipamentoMedicoEssencial": "equipamentoMedicoEssencial", "mobilidadeReduzida": "mobilidadeReduzida", "sozinho": "sozinho",
        "riscoSegurancaPublica": "riscoSegurancaPublica",
    ]

    struct Pedido { let ocorrencia: String; let remetente: String; let criadoEm: String; let lat: Double?; let lon: Double?; let precisao: Double?; let dados: [String: Any] }

    static func decodificar(_ url: URL) -> Pedido? {
        guard url.scheme == "alertaemacao", url.host == "importar", var frag = url.fragment else { return nil }
        if frag.hasPrefix("p=") { frag.removeFirst(2) }
        var b64 = frag.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while b64.count % 4 != 0 { b64 += "=" }
        guard let dados = Data(base64Encoded: b64), let json = (try? JSONSerialization.jsonObject(with: dados)) as? [String: Any] else { return nil }
        return converter(json)
    }

    static func converter(_ p: [String: Any]) -> Pedido? {
        guard (p["v"] as? String) == versao, let id = p["id"] as? String, let em = p["em"] as? String, em.hasPrefix("p:"), em.count <= 40,
              let criado = p["criado"] as? String else { return nil }
        var d: [String: Any] = ["origem": "pwa"]
        for (k, v) in (p["m"] as? [String: Any]) ?? [:] where (v as? Bool) == true { if let n = marcas[k] { d[n] = true } }
        if let n = p["pessoas"] as? Int { d["quantidadePessoas"] = n }
        if let nec = p["nec"] as? [String], !nec.isEmpty { d["necessidades"] = nec }
        if let c = p["comp"] as? [String: Any], (c["autorizado"] as? Bool) == true { d["compartilhamento"] = c }
        if (p["d"] as? Bool) == true { d["ficticio"] = true }
        return Pedido(ocorrencia: id.lowercased(), remetente: em, criadoEm: criado, lat: p["lat"] as? Double, lon: p["lon"] as? Double,
                      precisao: (p["prec"] as? NSNumber)?.doubleValue, dados: d)
    }
}
