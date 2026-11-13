import Foundation

/// Cliente do gateway v2 do backend. A chave é digitada no aparelho (nunca embutida no app).
struct Gateway {
    let urlBase: String
    let receptor: String
    let chave: String

    struct Resposta { let status: Int; let corpo: [String: Any]?; let erro: String? }

    func entregar(_ m: [String: Any], receptorPseudonimo: String, recebidoEm: String, modelo: String, posicaoReceptor: [String: Any]? = nil,
                  concluir: @escaping (Resposta) -> Void) {
        var corpo: [String: Any] = [
            "mensagem": m, "receptorPseudonimizado": receptorPseudonimo, "recebidoLocalmenteEm": recebidoEm,
            "canal": "nearby", "modeloAparelhoReceptor": modelo, "plataformaReceptor": "ios",
        ]
        // GPS do gateway: campo próprio; o servidor nunca o usa como posição da família.
        if let p = posicaoReceptor { corpo["posicaoReceptor"] = p }
        chamar("POST", "/api/gateway/v2/mensagens", corpo, concluir)
    }

    func confirmacoes(_ ocorrencia: String, concluir: @escaping (Resposta) -> Void) {
        chamar("GET", "/api/gateway/v2/ocorrencias/\(ocorrencia)/confirmacoes", nil, concluir)
    }

    private func chamar(_ metodo: String, _ caminho: String, _ corpo: [String: Any]?, _ concluir: @escaping (Resposta) -> Void) {
        let base = urlBase.hasSuffix("/") ? String(urlBase.dropLast()) : urlBase
        guard let url = URL(string: base + caminho) else { concluir(Resposta(status: 0, corpo: nil, erro: "URL inválida")); return }
        var req = URLRequest(url: url, timeoutInterval: 20)
        req.httpMethod = metodo
        req.setValue(receptor, forHTTPHeaderField: "X-Gateway-Receptor")
        req.setValue(chave, forHTTPHeaderField: "X-Gateway-Chave")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        if let corpo = corpo {
            req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
            req.httpBody = try? JSONSerialization.data(withJSONObject: corpo)
        }
        URLSession.shared.dataTask(with: req) { dados, resp, erro in
            let status = (resp as? HTTPURLResponse)?.statusCode ?? 0
            let json = dados.flatMap { (try? JSONSerialization.jsonObject(with: $0)) as? [String: Any] }
            let texto = dados.map { String(decoding: $0.prefix(200), as: UTF8.self) } ?? ""
            DispatchQueue.main.async {
                if let erro = erro { concluir(Resposta(status: 0, corpo: nil, erro: erro.localizedDescription)) }
                else if (200..<300).contains(status) { concluir(Resposta(status: status, corpo: json, erro: nil)) }
                else { concluir(Resposta(status: status, corpo: json, erro: "HTTP \(status) \(texto)")) }
            }
        }.resume()
    }
}
