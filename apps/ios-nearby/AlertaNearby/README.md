# Alerta em Ação — Proximidade (iOS)

App SwiftUI de **demonstração** com o SDK oficial [google/nearby](https://github.com/google/nearby) (Swift Package
`NearbyConnections`, commit fixado em `project.yml`), estratégia `.cluster`, serviceId `br.alertaemacao.nearby` — idênticos ao Android.

## Estado verificado
| Item | Situação |
|---|---|
| Código Swift | Escrito; **não compilado no ambiente de desenvolvimento** (sem Xcode). Compilação automática no GitHub Actions (`macos-15`) |
| Testes do protocolo (XCTest com os vetores compartilhados) | Rodam no CI junto do build para simulador |
| Instalação em iPhone físico | **Requer o Mac do proprietário** com Xcode e um Apple ID (assinatura) |

## Requisitos
Xcode 16+ (o SDK pede 14+), iOS 15+, Homebrew + XcodeGen (`brew install xcodegen`).

## Instalar no iPhone
1. `cd apps/ios-nearby && DEVELOPMENT_TEAM=<Team ID> BUNDLE_ID=br.alertaemacao.nearby.<seunome> ./gerar-projeto.sh`
   (sem Team ID: abra o projeto e escolha seu time em *Signing & Capabilities*; conta gratuita = "Personal Team", vale 7 dias).
2. Abra `AlertaNearby.xcodeproj`, conecte o iPhone por cabo, selecione-o e **Run (⌘R)**. A primeira resolução do pacote Nearby demora.
3. No iPhone: Ajustes → Geral → VPN e Gerenciamento de Dispositivos → confiar no desenvolvedor; ative o **Modo de Desenvolvedor** se pedido.
4. Ao abrir, permita **Bluetooth**, **Rede Local** e **Localização (durante o uso)**.

## Permissões (Info.plist gerado pelo `project.yml`)
`NSBluetoothAlwaysUsageDescription`, `NSLocalNetworkUsageDescription`, `NSLocationWhenInUseUsageDescription`,
`NSBonjourServices = ["_6D2B796E03F0._tcp"]` (6 primeiros bytes do SHA-256 do serviceId, regra do SDK), `NSAllowsLocalNetworking`.
Nenhuma capability/entitlement especial é exigida para Bluetooth + rede local.

## Limitações do iOS
Sem descoberta nem retransmissão com o app em segundo plano, tela bloqueada ou encerrado. Interoperabilidade Android ↔ iOS
depende de BLE/Wi-Fi LAN e **precisa ser comprovada fisicamente** (matriz AI-01/IA-01).
