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

    func testPedidoDaPwaPreservaIdentificadorHorarioEPosicaoDoEmissor() throws {
        let json = #"{"v":"aea-qr/1","id":"0193A5B0-1111-7222-8333-444455556666","em":"p:abc","criado":"2026-11-13T12:34:56Z","lat":-8.0402,"lon":-34.9301,"prec":18,"m":{"ilhado":true,"barreiraComSinaisInstabilidade":true,"aguaSubindo":false},"pessoas":3,"nec":["agua"],"comp":{"autorizado":true,"incluirPessoas":false,"incluirNecessidades":true},"h":"x"}"#
        let b64 = Data(json.utf8).base64EncodedString().replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_").replacingOccurrences(of: "=", with: "")
        let p = try XCTUnwrap(ImportacaoPwa.decodificar(URL(string: "alertaemacao://importar#p=\(b64)")!))
        XCTAssertEqual(p.ocorrencia, "0193a5b0-1111-7222-8333-444455556666")
        XCTAssertEqual(p.remetente, "p:abc")
        XCTAssertEqual(p.criadoEm, "2026-11-13T12:34:56Z")
        XCTAssertEqual(p.lat ?? 0, -8.0402, accuracy: 1e-9)
        XCTAssertEqual(p.dados["barreiraInstavel"] as? Bool, true)
        XCTAssertNil(p.dados["aguaSubindo"])
        XCTAssertNil(ImportacaoPwa.decodificar(URL(string: "alertaemacao://importar#p=lixo")!))
    }
}
