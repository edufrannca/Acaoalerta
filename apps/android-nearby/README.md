# Alerta em Ação — Proximidade (Android)

App nativo de **demonstração** para comunicação sem internet com Google Nearby Connections
(`play-services-nearby` 19.3.0, estratégia `P2P_CLUSTER`, serviceId `br.alertaemacao.nearby` — idênticos ao iOS).
Protocolo: `docs/PROTOCOLO_PROXIMIDADE.md`. Dados sempre fictícios.

## Estado verificado
| Item | Situação |
|---|---|
| `Protocolo.kt` contra os vetores compartilhados | **Verificado** em JVM (kotlinc 2.1 + org.json de referência), 3/3 vetores |
| Tipos do app contra o SDK Android 35 | **Verificado** com `android.jar` 35 e stubs das APIs do Nearby/AndroidX (o SDK real do Nearby não estava acessível) |
| Build Gradle completo / APK | **Pendente**: roda no GitHub Actions (`apps-nativos.yml`) ou no Android Studio |
| Teste em aparelho físico | **Não executado** até esta versão — ver `docs/MATRIZ_TESTES_DISPOSITIVOS.md` |

## Gerar o APK
- **GitHub Actions:** workflow *Apps nativos (Nearby)* → artefato `alerta-nearby-android-apk` e release `apk-testes`.
- **Local (Android Studio Ladybug+ / JDK 17):** `cd apps/android-nearby && ./gradlew :app:testDebugUnitTest :app:assembleDebug`
  → `app/build/outputs/apk/debug/app-debug.apk`. Instale com `adb install -r app-debug.apk` ou copie o arquivo para o aparelho
  (permitir "instalar apps desconhecidos").

## Usar
1. Abra o app e conceda Bluetooth, Aparelhos por perto e Localização.
2. **Configuração:** nome do aparelho (ex.: "Android A"), código do grupo (`ALERTA-DEMO-2026` em todos), e — só no aparelho que vai
   encaminhar ao servidor — URL do servidor, identificador do gateway e a chave (tela `/demo` → "Chave do gateway").
3. Receptor: **Receber**. Emissor: **Enviar: procurar** → toque no aparelho → confira o **mesmo código** nos dois → conectar.
4. Emissor: **Criar pedido FICTÍCIO**. Veja os níveis: 1 (salvo) → 2 (receptor salvou) → 4 (servidor) → 5 (central simulada).
5. **Exportar evidências** gera JSON sem chaves para a matriz de testes.

## Limitações
- Funciona com o app aberto e em primeiro plano; sem serviço em segundo plano.
- Sem multissalto automático. O APK é debug (assinatura de teste), não é versão de loja.
