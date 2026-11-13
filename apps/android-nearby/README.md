# PoC Nearby Connections — Android (Kotlin)

Demonstra 1 salto sem internet: **A** (offline) → **B** (gateway) → backend demonstrativo.

```bash
# Obtenha a chave do gateway (perfil administrador demo):
curl -H "Authorization: Bearer <token-admin>" "https://<url>/api/demo/gateway-chave?receptor=gateway-poc"
./gradlew :app:installDebug -Palerta.url=https://<url> -Palerta.receptor=gateway-poc -Palerta.chaveGateway=<chave>
```
Aparelho A: "Modo A" (desligue dados/Wi-Fi da internet, mantenha Bluetooth). Aparelho B: "Modo B" (com internet).
O pedido aparece no painel com canal `ProximidadeNearby`.

**Estado:** código escrito sem Android SDK no ambiente de desenvolvimento — **não compilado e não testado em dispositivos**.
O endpoint `/api/gateway/pacotes` do backend está implementado e coberto por teste de integração.
Sem multissalto: retransmissão em cadeia exige implementação adicional (ver docs/NEARBY_ANDROID_IOS.md).
