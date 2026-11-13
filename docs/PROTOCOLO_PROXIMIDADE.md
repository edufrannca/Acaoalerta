# Protocolo de proximidade `alerta-em-acao/proximidade/v2`

Contrato **único** usado pelo app Android (Kotlin), pelo app iOS (Swift) e pelo gateway do backend (C#).
Implementações: `apps/android-nearby/.../Protocolo.kt`, `apps/ios-nearby/AlertaNearby/Fontes/Protocolo.swift`,
`backend/src/AlertaEmAcao.Contratos/ProtocoloProximidade.cs`. Referência independente: `docs/protocolo/gerar_vetores.py`.

## Transporte Nearby (idêntico nas duas plataformas)
| Item | Valor |
|---|---|
| serviceId | `br.alertaemacao.nearby` |
| Estratégia | `P2P_CLUSTER` (Android) ↔ `.cluster` (iOS) |
| Bonjour (iOS `NSBonjourServices`) | `_6D2B796E03F0._tcp` = `_` + 6 primeiros bytes do SHA-256 do serviceId em hex maiúsculo + `._tcp` (regra do próprio SDK, `Utils::GenerateServiceType`) |
| Nome do aparelho | bytes UTF-8 do nome (Android `endpointName` ↔ iOS `context`) |
| Carga | `Payload` de bytes (JSON UTF-8), no máximo 32 KiB |
| Pareamento | código de verificação do SDK exibido **nos dois aparelhos**; o usuário só aceita se os códigos coincidirem e reconhecer o nome; caso contrário, recusa |
| Cifragem do canal | a do próprio Nearby (UKEY2); integridade de aplicação por HMAC (abaixo) |

## Envelope (JSON, camelCase)
| Campo | Tipo | Observação |
|---|---|---|
| `versaoProtocolo` | string | `alerta-em-acao/proximidade/v2` |
| `identificadorMensagem` | UUID | novo a cada mensagem; reenvio mantém o mesmo |
| `identificadorOcorrencia` | UUID | **o mesmo em todos os aparelhos e no servidor** (idempotência) |
| `tipoMensagem` | enum | `PEDIDO_SOCORRO`, `ATUALIZACAO`, `AVISO_DEMONSTRATIVO`, `CONFIRMACAO` |
| `municipio` | string? | código IBGE |
| `coordenadasAutorizadas` | objeto? | `latitude`, `longitude` (≤ 6 casas), `precisaoMetros?` |
| `horarioCriacao`, `horarioUltimaAtualizacao` | string | ISO-8601 UTC com `Z` |
| `remetentePseudonimizado` | string | `ap-` + 16 hex do SHA-256 do id de instalação (sem nome/telefone) |
| `dadosEstruturados` | objeto? | valores bool, inteiro, string ou lista de inteiros/strings |
| `numeroTentativas` | int | incrementado a cada reenvio |
| `identificadorCorrelacao` | UUID? | em `CONFIRMACAO`: a mensagem confirmada |
| `estadoTransmissao` | enum? | `SALVO_NO_APARELHO`(1), `RECEBIDO_POR_PROXIMIDADE`(2), `RECEBIDO_PELO_GATEWAY`(3), `RECEBIDO_PELO_SERVIDOR`(4), `RECEBIDO_PELA_CENTRAL_SIMULADA`(5), `FALHOU` |
| `integridadeMensagem` | string | `hmac-sha256:` + base64url sem padding |

Opcionais: **ausente ≡ null**. Campos desconhecidos são ignorados.

`dadosEstruturados` de pedido: `ficticio`, `quantidadePessoas`, `idades[]`, `pessoasFeridas`, `mobilidadeReduzida`, `ilhado`, `aguaSubindo`,
`correnteza`, `riscoDeslizamento`, `desmoronamento`, `barreiraInstavel`, `acessoBloqueado`, `referenciaLocal`, `descricao`.
De confirmação: `nivel`, `comprovante?`, `protocolos?`.

## Integridade
- Chave do grupo: `HMAC-SHA256(chave = "alerta-em-acao/proximidade/v2", dados = MAIÚSCULAS(TRIM(códigoDoGrupo)))`. Demonstração: `ALERTA-DEMO-2026`
  (configurável no backend por `Proximidade__CodigoGrupo`). Aparelhos de outro grupo têm as mensagens descartadas.
- Forma canônica: linhas terminadas em `\n`, nesta ordem: versão, id da mensagem, id da ocorrência, tipo, município, latitude (`%.6f`),
  longitude (`%.6f`), precisão (inteiro), horário de criação, horário de atualização, remetente, correlação; depois `chave=valor` de
  `dadosEstruturados` em ordem ordinal, sem nulos (bool `true/false`, inteiros decimais, strings com `\\`, `\n`, `\r` escapados; listas
  separadas por vírgula, com `\,` em strings). **`numeroTentativas` e `estadoTransmissao` ficam de fora** (mudam em trânsito).
- Vetores: `docs/protocolo/vetores-v2.json`, verificados em **Python, C# (xUnit), Kotlin (JUnit/JVM) e Swift (XCTest)**.

## Sequência
```
A (sem internet)                 B (receptor / gateway)                  API                    Central SIMULADA
salva → nível 1
descobre B, pede conexão ──────► código de verificação nos dois → aceitar
envia PEDIDO_SOCORRO ──────────► valida, verifica HMAC, SALVA (fsync)
                       ◄──────── CONFIRMACAO nível 2
                                 com internet: POST /api/gateway/v2/mensagens ─► persiste (nível 4) ─► POST protocolo
                       ◄──────── CONFIRMACAO nível 4      ◄─────────────────── 200            ◄─── nível 5 (SIM-…)
                       ◄──────── CONFIRMACAO nível 5 (protocolos) após GET …/confirmacoes
```
- Descoberta ≠ entrega; transferência concluída pelo SDK ≠ entrega. Só a CONFIRMACAO da aplicação, enviada **após persistir**, conta.
- Sem confirmação em 15 s: a mensagem continua salva e é reenviada na próxima conexão (mesmo id; o receptor reconfirma sem duplicar).
- Receptor sem internet: guarda como pendente e encaminha quando a rede volta (Android: `NetworkCallback`; iOS: `NWPathMonitor`).
- O servidor deduplica por `identificadorOcorrencia` (vários gateways podem entregar a mesma mensagem).

## Gateway HTTP (backend)
- `POST /api/gateway/v2/mensagens` — cabeçalhos `X-Gateway-Receptor` e `X-Gateway-Chave` (HMAC do segredo mestre por receptor; obtida em `/demo`).
  Corpo `EntregaGatewayV2 { mensagem, receptorPseudonimizado, recebidoLocalmenteEm, canal, ... }`. Respostas: 200 (`nivelComprovado`, `duplicado`,
  `confirmacoes`), 401 chave inválida, 422 `mensagem_invalida` / `integridade_invalida` / `tipo_nao_encaminhavel`.
- `GET /api/gateway/v2/ocorrencias/{id}/confirmacoes` — níveis comprovados e protocolos SIMULADOS.

## O que este protocolo não é
Não é Cell Broadcast, não usa a rede das operadoras e não tem roteamento multissalto automático. `AVISO_DEMONSTRATIVO` é distribuição local
de teste entre aparelhos conectados, sempre rotulada como não oficial.
