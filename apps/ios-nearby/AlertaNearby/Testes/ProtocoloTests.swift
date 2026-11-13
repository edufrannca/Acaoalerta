import XCTest
@testable import AlertaNearby

/// Mesmos vetores verificados pelo backend (C#) e pelo Android (Kotlin): docs/protocolo/vetores-v2.json (copiado como recurso de teste).
final class ProtocoloTests: XCTestCase {
    private func vetores() throws -> [String: Any] {
        let url = try XCTUnwrap(Bundle(for: ProtocoloTests.self).url(forResource: "vetores-v2", withExtension: "json"))
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
    }

    func testCanonicaEIntegridadeConferemComOsVetoresCompartilhados() throws {
        let v = try vetores()
        let chave = Protocolo.chaveGrupo(v["codigoGrupo"] as! String)
        XCTAssertEqual(v["chaveGrupoHex"] as? String, Protocolo.hex(chave))
        for caso in v["vetores"] as! [[String: Any]] {
            let m = caso["mensagem"] as! [String: Any]
            XCTAssertEqual(Protocolo.validar(m), [])
            XCTAssertEqual(caso["canonica"] as? String, Protocolo.canonica(m))
            XCTAssertEqual(caso["integridade"] as? String, Protocolo.integridade(m, chave: chave))
            XCTAssertTrue(Protocolo.verificar(m, chave: chave))
        }
        for p in v["pseudonimos"] as! [[String: String]] {
            XCTAssertEqual(p["remetente"], Protocolo.pseudonimizar(p["instalacao"]!))
        }
    }

    func testAdulteracaoInvalidaECamposDeTransitoNao() throws {
        let v = try vetores()
        let chave = Protocolo.chaveGrupo("ALERTA-DEMO-2026")
        var m = (v["vetores"] as! [[String: Any]])[0]["mensagem"] as! [String: Any]
        m["numeroTentativas"] = 9
        m["estadoTransmissao"] = Protocolo.Estado.proximidade
        XCTAssertTrue(Protocolo.verificar(m, chave: chave))
        m["municipio"] = "2609600"
        XCTAssertFalse(Protocolo.verificar(m, chave: chave))
        XCTAssertFalse(Protocolo.verificar((v["vetores"] as! [[String: Any]])[0]["mensagem"] as! [String: Any], chave: Protocolo.chaveGrupo("OUTRO")))
    }

    func testMensagemMontadaNoIOSSobreviveACodificacaoJSON() throws {
        let chave = Protocolo.chaveGrupo("ALERTA-DEMO-2026")
        let m = Protocolo.montar(tipo: Protocolo.Tipo.pedido, ocorrencia: UUID().uuidString.lowercased(), remetente: "ap-teste", chave: chave,
                                 dados: ["ficticio": true, "quantidadePessoas": 2, "idades": [5, 70]], municipio: "2611606",
                                 latitude: -8.12345678, longitude: -34.9, precisao: 12.6)
        XCTAssertEqual(Protocolo.validar(m), [])
        let ida = try XCTUnwrap(Protocolo.decodificar(try XCTUnwrap(Protocolo.codificar(m))))
        XCTAssertTrue(Protocolo.verificar(ida, chave: chave))
        let c = Protocolo.confirmacao(de: ida, nivel: 2, remetente: "ap-receptor", chave: chave, comprovante: "persistido")
        XCTAssertEqual(c["identificadorCorrelacao"] as? String, m["identificadorMensagem"] as? String)
        XCTAssertTrue(Protocolo.verificar(try XCTUnwrap(Protocolo.decodificar(try XCTUnwrap(Protocolo.codificar(c)))), chave: chave))
    }
}
