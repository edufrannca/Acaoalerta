# Nearby Connections — Android e iOS

Especificação do contrato: [PROTOCOLO_PROXIMIDADE.md](PROTOCOLO_PROXIMIDADE.md). Testes físicos: [MATRIZ_TESTES_DISPOSITIVOS.md](MATRIZ_TESTES_DISPOSITIVOS.md).

## O que existe (13/11/2026)
| Parte | Situação verificada |
|---|---|
| Protocolo v2 único (C#, Kotlin, Swift) + vetores gerados por implementação independente em Python | C#: testes xUnit passam; Kotlin: verificado em JVM (kotlinc 2.1); Swift: XCTest no CI (pendente de execução) |
| Gateway `/api/gateway/v2` com níveis 1–5, integridade, idempotência | Teste de integração passa; script ponta a ponta SIMULADO passa |
| App Android (descoberta, anúncio, pareamento por código, persistência antes do ACK, reenvio, gateway, aviso a vários receptores, evidências) | Tipos verificados contra `android.jar` 35 + stubs das APIs; **APK não gerado** (Google Maven inacessível no ambiente; CI bloqueado na conta) |
| App iOS (SwiftUI + google/nearby, mesmas funções) | Escrito; **não compilado** (sem Xcode no ambiente) |
| Testes físicos | **Nenhum executado** |

O serviceId antigo do esqueleto iOS (`br.alertaemacao.nearby.SOCORRO`) era **diferente** do Android — impediria a interoperação. Corrigido para `br.alertaemacao.nearby` nos dois.

## Comportamento esperado por plataforma (a confirmar fisicamente)
- Android ↔ Android: BLE/Bluetooth/Wi-Fi conforme disponibilidade (`P2P_CLUSTER`).
- iOS ↔ iOS: BLE, Wi-Fi LAN e AWDL.
- Android ↔ iOS: depende de BLE e/ou Wi-Fi LAN comum; **interoperabilidade só vale após AI-01 e IA-01 passarem**.
- Receptor que não usa Nearby e internet ao mesmo tempo: guarda o pedido (nível 2) e encaminha quando a internet volta (Cenário C).
- Segundo plano/tela bloqueada/app encerrado: não há serviço em segundo plano; registre o que ocorrer (Cenário E).

## Limitações
Sem multissalto automático (o contrato tem `numeroTentativas` e deduplicação; cadeia de saltos é trabalho futuro). APK debug e build iOS de
desenvolvimento, não versões de loja. Não é Cell Broadcast.

## LoRa / Meshtastic (futuro, só documentado)
O mesmo envelope v2, compactado, pode trafegar em nós LoRa até um gateway comunitário com internet que faça o `POST /api/gateway/v2/mensagens`.
Requisitos: autenticação de dispositivo, cifragem (PSK do Meshtastic), confirmação, registro de saltos e limite de ~200 bytes. Nenhuma rede LoRa foi implantada.
