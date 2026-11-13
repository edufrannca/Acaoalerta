import Foundation

/// Armazenamento local durável: um arquivo JSON por mensagem (gravação atômica).
/// A confirmação de recebimento (nível 2) só é enviada depois que `salvar` retorna sem erro.
final class Repositorio {
    struct Registro {
        var mensagem: [String: Any]
        let direcao: String          // "enviada" | "recebida"
        var nivel: Int
        var pendenteGateway: Bool
        var ultimoErro: String?
        var eventos: [[String: String]]
        var protocolos: String?
        var origem: String?

        var id: String { (mensagem["identificadorMensagem"] as? String) ?? "" }
        var ocorrencia: String { (mensagem["identificadorOcorrencia"] as? String) ?? "" }
        var tipo: String { (mensagem["tipoMensagem"] as? String) ?? "" }

        func json() -> [String: Any] {
            var j: [String: Any] = ["mensagem": mensagem, "direcao": direcao, "nivel": nivel, "pendenteGateway": pendenteGateway, "eventos": eventos]
            j["ultimoErro"] = ultimoErro ?? NSNull()
            j["protocolos"] = protocolos ?? NSNull()
            j["origem"] = origem ?? NSNull()
            return j
        }

        static func de(_ j: [String: Any]) -> Registro? {
            guard let m = j["mensagem"] as? [String: Any], let d = j["direcao"] as? String else { return nil }
            return Registro(mensagem: m, direcao: d, nivel: (j["nivel"] as? Int) ?? 0, pendenteGateway: (j["pendenteGateway"] as? Bool) ?? false,
                            ultimoErro: j["ultimoErro"] as? String, eventos: (j["eventos"] as? [[String: String]]) ?? [],
                            protocolos: j["protocolos"] as? String, origem: j["origem"] as? String)
        }
    }

    private let dir: URL
    private let log: URL
    private let fila = DispatchQueue(label: "br.alertaemacao.nearby.repositorio")

    init() {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        dir = base.appendingPathComponent("mensagens", isDirectory: true)
        log = base.appendingPathComponent("historico.log")
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
    }

    func salvar(_ r: Registro) throws {
        try fila.sync {
            let dados = try JSONSerialization.data(withJSONObject: r.json(), options: [])
            try dados.write(to: dir.appendingPathComponent(r.id + ".json"), options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        }
    }

    func obter(_ id: String) -> Registro? {
        fila.sync {
            guard let d = try? Data(contentsOf: dir.appendingPathComponent(id + ".json")),
                  let j = (try? JSONSerialization.jsonObject(with: d)) as? [String: Any] else { return nil }
            return Registro.de(j)
        }
    }

    func todos() -> [Registro] {
        let arquivos = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? []
        return arquivos.filter { $0.pathExtension == "json" }
            .compactMap { obter($0.deletingPathExtension().lastPathComponent) }
            .sorted { (($0.mensagem["horarioUltimaAtualizacao"] as? String) ?? "") > (($1.mensagem["horarioUltimaAtualizacao"] as? String) ?? "") }
    }

    func pendentesGateway() -> [Registro] { todos().filter { $0.pendenteGateway && $0.tipo != Protocolo.Tipo.confirmacao } }
    func enviadasSemConfirmacao() -> [Registro] { todos().filter { $0.direcao == "enviada" && $0.nivel < 2 && $0.tipo != Protocolo.Tipo.confirmacao } }

    func evento(_ r: inout Registro, _ texto: String) {
        r.eventos.append(["em": Protocolo.agoraISO(), "texto": texto])
        try? salvar(r)
        registrarLog("\(r.id.prefix(8)) \(texto)")
    }

    func registrarLog(_ texto: String) {
        fila.sync {
            let linha = "\(Protocolo.agoraISO()) \(texto)\n"
            if let h = try? FileHandle(forWritingTo: log) {
                h.seekToEndOfFile(); h.write(Data(linha.utf8)); h.closeFile()
            } else {
                try? Data(linha.utf8).write(to: log)
            }
        }
    }

    func ultimasLinhasLog(_ n: Int) -> [String] {
        guard let t = try? String(contentsOf: log, encoding: .utf8) else { return [] }
        return Array(t.split(separator: "\n").suffix(n).reversed()).map(String.init)
    }

    /// Evidências sem chaves, para anexar à matriz de testes físicos.
    func exportarEvidencias() -> String {
        let j: [String: Any] = ["exportadoEm": Protocolo.agoraISO(), "mensagens": todos().map { $0.json() }, "log": ultimasLinhasLog(500)]
        guard let d = try? JSONSerialization.data(withJSONObject: j, options: [.prettyPrinted]) else { return "{}" }
        return String(decoding: d, as: UTF8.self)
    }
}
