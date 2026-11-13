# Roteiro de demonstração (13/11/2026, 15h)

> Diga em voz alta: **"Ambiente simulado: nenhum órgão real foi acionado."** Todos os dados, pessoas e centrais são fictícios.
> **Só apresente o que já passou num ensaio antes das 15h.** Itens marcados ⚠ dependem de teste físico ainda não realizado: se não passarem
> no ensaio, mostre a gravação identificada ("gravação do ensaio de HH:MM") ou pule, dizendo o estado real.

## Preparação (14h30)
1. Abra a URL publicada (acorda o plano gratuito) e confira `/health`. Contingência: `docker compose up` no notebook.
2. Celulares: PWA instalada (Android via Chrome, iPhone via Safari); apps nativos instalados e configurados (grupo `ALERTA-DEMO-2026`, chave do gateway em `/demo`).
3. Telas: A = celular espelhado; B = `/painel` (operador DC Recife); C = `/demo` (Administrador). Centrais em **Normal**.
4. Rode `ALERTA_URL=<url> python3 simuladores/nearby-ponta-a-ponta.py` (confirma gateway → centrais).

Contas fictícias (senha `Demo@2026`, domínio `@demo.alerta`): `cidada`, `cidadao.olinda`, `voluntaria`, `operador.dc.recife`, `operador.dc.olinda`,
`operador.dc.ipojuca`, `operador.dc.jaboatao`, `operador.bombeiros`, `operador.samu`, `org.maos`, `admin` (ou Perfil → Modo demonstração).

## Sequência (≈ 9 min)
| # | O que mostrar | Onde | Estado |
|---|---|---|---|
| 1 | App instalado no Android (ícone, abre em tela cheia) | celular Android | ⚠ ensaiar |
| 2 | App instalado no iPhone | iPhone | ⚠ ensaiar |
| 3 | Identidade visual; botão **Aa** (texto 115–150%, alto contraste) | PWA | pronto (E2E + axe) |
| 4 | Leitura por voz: "Ouvir situação da região", pausar, repetir | PWA | pronto no navegador; ⚠ voz pt-BR no celular |
| 5 | GPS e mapa: município identificado, limites dos 14 | PWA | pronto; ⚠ GPS no celular |
| 6 | Riscos e fontes: aviso INMET, chuva APAC com horário, área documentada, "ausência de registros não comprova ausência de perigo" | PWA | pronto |
| 7 | Alerta proativo demonstrativo (C → "Publicar aviso", área Ibura) | `/demo` | pronto |
| 8 | "Há um aviso para sua região. Você e sua família estão bem?" | PWA (Ana) | pronto; ⚠ push no celular |
| 9 | Check-in **Estou seguro** | PWA | pronto |
| 10 | Pedido fictício — enviado antes do formulário; 193/199/192/190 visíveis (não ligar) | PWA | pronto |
| 11 | Complemento: 4 pessoas, idades 3/34/78, mobilidade reduzida, água subindo, ilhados | PWA | pronto |
| 12 | Análise **Groq real** no painel (modelo, tempo, pessoas citadas, evidências) | `/painel` | ⚠ só se a chave estiver no Render e aparecer "Concluida" no ensaio; senão mostre "regras aplicadas" |
| 13 | Encaminhamento por município (Recife → DC-RECIFE + CBM-SIM; Olinda → DC-OLINDA) | painel | pronto |
| 14 | Centrais simuladas recebendo (`/central/DC-RECIFE`) | central | pronto |
| 15 | Protocolos `SIM-…` e níveis 4/5 | painel | pronto |
| 16 | Falha de central (CBM-SIM Indisponível) → retentativa → Recebido sem duplicar | `/demo` + painel | pronto |
| 17 | Apoio comunitário: oferta → organização simulada confirma | PWA + `/central/ORG-MAOS` | pronto |
| 18 | PWA offline: modo avião → "Pedido salvo neste aparelho. Ainda não transmitido." → religar → recebido | celular | pronto no E2E; ⚠ ensaiar no celular |
| 19 | Nearby Android → Android (AA-01-B, emissor sem internet) | apps nativos | ⚠ depende de APK + teste físico |
| 20 | Nearby Android → iPhone (AI-01) | apps nativos | ⚠ depende de build iOS + teste físico |
| 21 | Nearby iPhone → Android (IA-01) | apps nativos | ⚠ idem |
| 22 | Nearby iPhone → iPhone (II-01) | apps nativos | ⚠ idem (exige 2 iPhones) |
| 23 | Gateway encaminhando ao backend (receptor com internet → níveis 4 e 5) | app receptor + painel | pronto via script (SIMULADO); ⚠ físico |
| 24 | Painel "Níveis de entrega comprovados" (1 a 5; nunca 6) | painel | pronto |

## Cena extra — família avisada (≈ 2 min)
Preparar antes: conta da "moradora" com a autorização de notificar familiares, residência "Minha casa" (moro aqui) e vínculo aprovado com a conta da "filha" (código de convite). Ver `MONITORAMENTO_FAMILIAR.md`.
1. Celular da moradora: Preciso de socorro (pedido fictício) → "Pedido associado à sua residência".
2. Celular/desktop da filha: aviso "Pedido de socorro na residência monitorada" (faixa vermelha com o app aberto, push se ativado, ou Perfil → Avisos de familiares) → abrir → estado e nível; dizer que a central é SIMULADA.
3. Mostrar que a comunidade vê só necessidades e área aproximada, e que um SOS anônimo perto da casa não avisa ninguém.

## Encerramento
"Tudo passou por HTTP, banco e protocolo de verdade; só o destinatário é simulado. O nível 6 — recebimento por um órgão real — exige homologação: é o próximo passo."

## Plano B
- Servidor hibernado: abrir `/health` e aguardar ~1 min; ou notebook com `docker compose up`.
- Nearby falhou ao vivo: mostrar a gravação do ensaio (identificada com horário) e a matriz preenchida; não improvisar teste novo no palco.
- Groq indisponível: o painel mostra "IA indisponível; regras determinísticas" — comportamento projetado.
