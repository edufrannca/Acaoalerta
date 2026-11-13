# Retransmissão por aparelho próximo

Fluxo: **família sem internet → aparelho próximo → internet do aparelho próximo → servidor → central simulada**. O aparelho próximo atua como gateway; não é preciso compartilhar hotspot. **Não há rede mesh multissalto**: cada pedido passa por no máximo um aparelho intermediário.

## O que funciona onde
| Capacidade | PWA (navegador) | App nativo Android | App nativo iOS |
|---|---|---|---|
| Registrar o pedido sem internet (salvo na hora) | sim (IndexedDB) | sim (arquivos do app) | sim (arquivos do app) |
| Descobrir aparelhos por Bluetooth/Wi-Fi | **não** (nenhuma API web equivalente) | sim, Nearby Connections | sim, Nearby Connections para Swift (Multipeer/Bonjour) |
| Transmitir para aparelho próximo | **código QR** lido pela câmera de outro celular | Nearby, com pareamento confirmado por código | idem |
| Aparelho próximo encaminhar ao servidor | sim: abre `/retransmitir` e envia pela internet dele | sim (gateway v2) | sim (gateway v2) |
| Confirmações voltarem ao emissor | quando o emissor sincroniza (mesmo id) | sim, pela conexão Nearby, e ao sincronizar | idem |
| Funcionar com o app em segundo plano ou fechado | não | não validado | **não validado; não prometido** |

## Caminho 1 — QR (sem app instalado)
1. Sem internet, o pedido é salvo no aparelho: "Seu pedido está salvo neste aparelho." (nível 1).
2. "Procurar aparelhos próximos para transmitir" mostra um código QR. A tela explica que o navegador não procura aparelhos por Bluetooth.
3. Outra pessoa aponta a câmera do celular dela e abre `https://…/retransmitir#p=…`. O conteúdo fica no fragmento da URL, que não é enviado em logs de servidor.
4. O aparelho que ajuda **salva o pedido primeiro** (fila local; nível 2 do ponto de vista dele) e envia a `POST /api/retransmissoes/qr` (nível 3 → 4). Se ele também estiver sem internet, o envio sai quando a conexão voltar, **desde que a PWA já tenha sido aberta uma vez nesse aparelho** (página guardada pelo service worker).
5. O servidor confere o código de integridade (SHA-256 do texto canônico; detecta corrupção ou edição, mas não autentica quem criou), registra o canal `ProximidadeQr` e devolve nível 4 (ou 5, quando a central simulada confirma).
6. Quando o aparelho da família volta a ter internet, a fila envia o mesmo identificador; o servidor reconhece o dono pelo pseudônimo e **não duplica**. A tela da família passa a mostrar "Níveis 2–3 · lido por QR".

O QR leva: identificador do pedido, pseudônimo do emissor (`p:` + SHA-256 truncado do id do aparelho), horário, posição **do emissor** com precisão, situações marcadas, número de pessoas, necessidades e a escolha de compartilhamento. Não leva nome, telefone, relato nem o identificador real do aparelho. Paridade aparelho↔servidor testada com o mesmo vetor (`comunidade.test.ts` e `ComunidadeTestes.cs`).

## Caminho 2 — app nativo Nearby
- O app nativo **não lê o IndexedDB do navegador**. A ponte é um link: a PWA abre `alertaemacao://importar#p=…` (mesmo formato do QR). Android: `intent-filter` VIEW; iOS: `CFBundleURLTypes` + `onOpenURL`.
- O app cria a mensagem do protocolo v2 com o **mesmo `identificadorOcorrencia`**, o horário e a posição do emissor, e usa o pseudônimo da PWA como `remetentePseudonimizado`.
- O app mostra a sequência real: procurando (descoberta ligada) → aparelhos encontrados → pedido de conexão com código de verificação que as duas pessoas conferem antes de aceitar → conectado → envio → nível 2 só quando chega o ACK enviado **depois** que o receptor gravou → "sem internet — aguardando conexão para encaminhar" no receptor quando for o caso → nível 4 → nível 5. Os rótulos exatos estão no registro de eventos de cada mensagem.
- Sucesso nunca é declarado só por descoberta Bluetooth ou bytes enviados.

## Níveis de entrega
| Nível | Significado |
|---|---|
| 1 | salvo no aparelho emissor |
| 2 | recebido e **persistido** pelo aparelho próximo |
| 3 | o aparelho próximo assumiu o encaminhamento (gateway) |
| 4 | recebido e persistido pelo servidor |
| 5 | confirmado por **central simulada** |

Nenhum nível significa atendimento por órgão público real.

## GPS do emissor ≠ GPS do gateway
- A ocorrência guarda a posição lida no aparelho da família. Sem coordenadas no pedido: "localização não informada".
- A posição do aparelho que retransmitiu (opcional no QR; última posição conhecida no app nativo, se autorizada) vai em campo próprio (`posicaoReceptor` / `latitudeRetransmissor`), fica cifrada em `PosicoesRetransmissor` e só aparece ao operador, rotulada "não é a posição comprovada do solicitante", com a comparação pelas margens de precisão.

## Duplicidade e integridade
- Mesmo pedido por dois gateways (Nearby ou QR) → uma ocorrência; o segundo recebe `duplicado=true`.
- Nearby: HMAC-SHA256 do grupo + chave por gateway; QR: hash de integridade + limite de taxa.
- O código demonstrativo do grupo (`ALERTA-DEMO-2026`) **não é** autenticação de produção (ver `SEGURANCA_LGPD.md`).
