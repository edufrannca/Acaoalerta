# Nearby Connections: relatório da prova de conceito

## Objetivo
Transmitir um pedido **fictício** entre dois aparelhos próximos sem internet: A → B (ACK) → B armazena → B encaminha ao backend → painel.

## O que existe
| Parte | Situação |
|---|---|
| Backend `POST /api/gateway/pacotes` (contrato `alerta-em-acao/gateway/v1`, chave HMAC por receptor, limite de saltos, canal `ProximidadeNearby`/`GatewaySimulado`) | **Implementado e testado** (teste de integração `Gateway_simulado_registra_pedido_retransmitido`) |
| Emissão da chave do receptor (`GET /api/demo/gateway-chave`, administrador) | Implementado |
| Painel mostrando o canal e quem retransmitiu | Implementado |
| App Android (Kotlin, `play-services-nearby`, P2P_CLUSTER, ACK, `recebidos.jsonl`, encaminhamento HTTP) | **Código escrito, NÃO compilado** (sem Android SDK no ambiente) |
| App iOS (Swift, google/nearby `NearbyConnections`) | **Esqueleto, NÃO compilado** (sem Xcode) |

## Aparelhos utilizados nos testes
**Nenhum.** Não houve hardware disponível no ambiente de desenvolvimento. **O recurso não deve ser apresentado como testado fisicamente.**

## Como validar (quando houver aparelhos)
1. `GET /api/demo/gateway-chave?receptor=gateway-poc` (token de administrador).
2. `./gradlew :app:installDebug -Palerta.url=<url> -Palerta.receptor=gateway-poc -Palerta.chaveGateway=<chave>` em dois Androids.
3. A: dados/Wi-Fi de internet desligados, Bluetooth ligado → "Modo A". B: com internet → "Modo B".
4. Esperado: ACK em A; "Backend respondeu HTTP 200" em B; ocorrência no painel com canal `ProximidadeNearby`.
5. Android ↔ iOS: compilar `apps/ios-nearby` com o pacote google/nearby e repetir.

## Limitações declaradas
- **Sem multissalto:** o Nearby não faz roteamento em malha automaticamente. Cadeia de retransmissão (store-and-forward, TTL, deduplicação por id, registro de saltos) é trabalho futuro. O contrato já tem `saltos` e o servidor deduplica pelo id.
- Aceitação automática de conexão na PoC; em produção, confirmar o código de verificação.
- Mensagens sem cifragem de ponta a ponta além da camada do próprio Nearby.

## LoRa / Meshtastic (futuro)
O mesmo contrato compacto pode ser carregado por nós LoRa (ex.: Meshtastic, payload curto) até um gateway comunitário com internet que faça o `POST /api/gateway/pacotes`. Requisitos: autenticação de dispositivo, cifragem (canais PSK do Meshtastic), confirmação, registro de saltos e limite de tamanho (~200 bytes). **Nenhuma rede LoRa foi implantada**; na demonstração, o endpoint do gateway é **simulado**.
