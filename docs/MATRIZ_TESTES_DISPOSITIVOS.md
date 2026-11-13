# Matriz de testes em dispositivos físicos

> **Regra:** só marque **PASSOU/FALHOU — teste físico executado** depois de executar com aparelhos reais e anexar evidência
> (exportação do app, captura de tela ou vídeo). Descoberta de aparelho não é entrega. Nada aqui foi preenchido sem execução.

Classificações: **PASSOU — teste físico executado** · **FALHOU — teste físico executado** · **BLOQUEADO — dependência ou permissão indisponível** ·
**NÃO EXECUTADO — teste pendente** · **SIMULADO — somente ambiente de teste**.

## Estado em 13/11/2026, 03:30 (Recife)
| Situação | Motivo |
|---|---|
| Nenhum teste físico executado | O desenvolvimento ocorreu em ambiente de nuvem sem aparelhos; os testes dependem da equipe na sala |
| APK Android | **BLOQUEADO** até o GitHub Actions iniciar (todo run termina em `startup_failure`, inclusive um workflow mínimo) ou até compilar no Android Studio |
| App iOS | **BLOQUEADO** até compilar no Mac com Xcode e Apple ID |
| Caminho do gateway até a central | **SIMULADO**: local (1,4 s) e **no ambiente publicado** (02:30, 2,6 s até o nível 5, protocolos SIM-DC-RECIFE-20261113-000001 e SIM-CBM-SIM-20261113-000001, reenvio por outro gateway sem duplicar) — não substitui o teste físico |

| GW-SIM-PUB | 13/11 02:30 | navegador (aparelho A simulado) | navegador (gateway) | — | — | — | — | — | — | ocorrência ec92dff7… | — | — | — | — | simulado | 723 ms | SIM-DC-RECIFE-20261113-000001, SIM-CBM-SIM-20261113-000001 | SIMULADO — somente ambiente de teste | — | registro na conversa de validação |

## Inventário de aparelhos (preencher antes de começar)
| Apelido | Modelo | Sistema e versão | App (versão) | Dono/autorização |
|---|---|---|---|---|
| Android A | | | | |
| Android B | | | | |
| iPhone A | | | | |
| iPhone B | | | | |

Com 1 aparelho de uma plataforma, AA-01 ou II-01 ficam **BLOQUEADO — dependência indisponível** (não “aprovados por analogia”).

## Preparação (uma vez)
1. Servidor publicado (ou `docker compose up` no notebook + mesma rede). Em `/demo` (Administrador) → **Chave do gateway**: gere para
   `gateway-android` e `gateway-ios`.
2. Instale o app nativo: Android → APK (release `apk-testes` ou Android Studio); iPhone → Xcode (`apps/ios-nearby/AlertaNearby/README.md`).
3. Em cada aparelho → **Configuração**: nome (`Android A`, `iPhone B`…), grupo `ALERTA-DEMO-2026`, e no receptor/gateway: URL, identificador e chave → **Salvar**.
4. Permissões: Android — Bluetooth/Aparelhos por perto, Localização. iPhone — Bluetooth, Rede Local, Localização (durante o uso).
5. Relógios dos aparelhos no automático (horários entram nas evidências).

## Procedimento base (todas as combinações)
1. Receptor: **Receber**. Emissor: **Enviar: procurar** — anote o tempo até “encontrado … ms” (descoberta).
2. Emissor toca no receptor; **os dois** mostram “Confirme o pareamento”: confira que o **código é igual** nos dois → “Os códigos conferem”.
   Anote “conectado … ms” (conexão).
3. Emissor: **Criar pedido de socorro FICTÍCIO**. Observe no emissor: “bytes transferidos … ms” (envio) e “confirmação … Nível 2 · … ms” (confirmação).
4. Receptor com internet e gateway: deve mostrar “servidor confirmou persistência” e depois “Nível 5 … SIM-…”. O emissor recebe as confirmações 4 e 5 se ainda conectado.
5. Painel (`/painel`, operador DC Recife ou Olinda conforme a coordenada fictícia): a ocorrência aparece com canal **ProximidadeNearby** e os níveis 1–5.
6. Nos dois aparelhos → **Exportar evidências** → salve como `evidencias/<código>-<aparelho>.json`. Copie para a tabela.

## Cenários
| Código | Emissor → Receptor | Internet | Passos extras | Esperado |
|---|---|---|---|---|
| AA-01-A, AI-01-A, IA-01-A, II-01-A | as 4 direções | ambos com | base | Níveis 2 → 4 → 5; 1 ocorrência |
| AA-01-B, AI-01-B, IA-01-B, II-01-B | as 4 direções | **emissor sem** (modo avião + Bluetooth ligado; no Android, Wi-Fi pode ficar ligado sem rede) | base | Idem; o pedido saiu de um aparelho sem internet |
| AA-01-C … II-01-C | as 4 direções | **ambos sem** | após o nível 2, abra o painel: **não deve existir** a ocorrência; religue a internet do receptor | Nível 2 sem 4 enquanto offline; depois 4 e 5 automaticamente |
| D-01 | qualquer | — | receptor fora de alcance / Bluetooth desligado / permissão negada | Mensagem de falha; pedido preservado no emissor; nenhuma entrega declarada |
| D-02 | qualquer | — | **Recusar** no pareamento | Sem conexão; nada enviado |
| D-03 | qualquer | — | fechar o app receptor durante o envio | “transmissão interrompida”/“sem confirmação em 15 s”; reenvio na próxima conexão |
| D-04 | qualquer | — | reconectar e reenviar o mesmo pedido | Receptor: “duplicata … sem duplicar”; servidor: `duplicado=true`; 1 ocorrência |
| D-05 | qualquer | — | grupos diferentes nos dois aparelhos | Receptor descarta: “integridade NÃO confere” |
| E-01 | qualquer | — | tela bloqueada / app em segundo plano / app encerrado | Documentar o que acontece (esperado: descoberta e envio só com app aberto) |
| F-01 | qualquer | emissor sem | repetir a 1, 5, 10, 20 m (anotar obstáculos) | Registrar até onde funcionou — não extrapolar |
| G-01 | 1 emissor → 2+ receptores | — | conectar a todos → **Enviar aviso demonstrativo** | Contar: encontrados / aceitaram / receberam / confirmaram nível 2 |

## Registro
| Código | Data/hora | Emissor (modelo, SO) | Receptor (modelo, SO) | Versão app | Internet E/R | BT | Wi-Fi | Permissões | Estratégia | ID mensagem | Descoberta (ms) | Conexão (ms) | Envio (ms) | Confirmação (ms) | Recebido local | Backend | Protocolo SIMULADO | Resultado | Erro | Evidência |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| AA-01-A | | | | 0.2 | S/S | | | | P2P_CLUSTER | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| AI-01-A | | | | 0.2 | S/S | | | | P2P_CLUSTER/.cluster | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| IA-01-A | | | | 0.2 | S/S | | | | .cluster/P2P_CLUSTER | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| II-01-A | | | | 0.2 | S/S | | | | .cluster | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| AA-01-B | | | | 0.2 | N/S | | | | P2P_CLUSTER | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| AI-01-B | | | | 0.2 | N/S | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| IA-01-B | | | | 0.2 | N/S | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| II-01-B | | | | 0.2 | N/S | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| AA-01-C | | | | 0.2 | N/N | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| AI-01-C | | | | 0.2 | N/N | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| IA-01-C | | | | 0.2 | N/N | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| II-01-C | | | | 0.2 | N/N | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| D-01…D-05, E-01, F-01, G-01 | | | | | | | | | | | | | | | | | | NÃO EXECUTADO — teste pendente | | |
| GW-SIM | 13/11 03:20 | script (aparelho A simulado) | script (gateway) | — | — | — | — | — | — | fc0480b8… | — | — | — | — | simulado | 368 ms | SIM-DC-RECIFE-…-000023, SIM-CBM-SIM-…-000011 | SIMULADO — somente ambiente de teste | — | saída do script |

## PWA nos celulares (Android Chrome / iPhone Safari instalado)
Abra `/instalar` no aparelho → **Diagnóstico deste aparelho** → **Copiar** e cole abaixo. Depois:

| Item | Android PWA | iPhone PWA |
|---|---|---|
| Instalação (ícone na tela inicial) e abertura standalone | NÃO EXECUTADO | NÃO EXECUTADO |
| Cadastro/login (Perfil) e reabertura mantendo sessão | NÃO EXECUTADO | NÃO EXECUTADO |
| GPS: autorizar, município correto, revogar, sem GPS → município manual | NÃO EXECUTADO | NÃO EXECUTADO |
| Mapa: zoom/navegação; sem internet (camadas salvas × indisponíveis) | NÃO EXECUTADO | NÃO EXECUTADO |
| Alerta demonstrativo → “Há um aviso para sua região. Você e sua família estão bem?” | NÃO EXECUTADO | NÃO EXECUTADO |
| Web Push: permitir, negar, receber, tocar e abrir o app | NÃO EXECUTADO | NÃO EXECUTADO (só app instalado, iOS 16.4+) |
| Check-in “Estou seguro” | NÃO EXECUTADO | NÃO EXECUTADO |
| Socorro: pedido imediato, complemento (pessoas/idades/riscos), links 193/199/192/190 abrem o discador (não ligar) | NÃO EXECUTADO | NÃO EXECUTADO |
| Offline: modo avião → pedido salvo → religar → recebido (1 ocorrência) | NÃO EXECUTADO | NÃO EXECUTADO |
| Apoio comunitário: oferta, validação, confirmação da organização, offline | NÃO EXECUTADO | NÃO EXECUTADO |
| Leitura por voz (ouvir, pausar, repetir, parar, velocidade) | NÃO EXECUTADO | NÃO EXECUTADO |
| TalkBack / VoiceOver e texto ampliado | NÃO EXECUTADO | NÃO EXECUTADO |

Automatizado (Chromium desktop, não substitui celular): instalação PWA (manifest, ícones, SW, rotas offline), axe WCAG 2 A/AA em 5 telas,
alto contraste/zoom persistentes — **14/14 E2E passaram** em 13/11/2026.
