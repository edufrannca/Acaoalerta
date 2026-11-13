# Roteiro de demonstração (12 cenários, ~8 min)

> Todos os dados, pessoas e centrais são **fictícios**. Diga em voz alta: **"Ambiente simulado: nenhum órgão real foi acionado."**

## Preparação (15 min antes)
1. Abrir a URL publicada (acorda o serviço gratuito) ou rodar `docker compose up`.
2. Abrir três janelas lado a lado:
   - **A, celular** (DevTools em modo dispositivo ou um telefone real): `/` → Perfil → *Ana, cidadã (Recife)*;
   - **B, operador**: `/perfil` → *Defesa Civil Recife (simulada)* → `/painel`;
   - **C, administrador**: `/perfil` → *Administrador da demonstração* → `/demo`.
3. Em `/demo`, confirme que todas as centrais estão em **Normal**.

Contas fictícias (senha `Demo@2026`, domínio `@demo.alerta`): `cidada`, `cidadao.olinda`, `voluntaria`, `operador.dc.recife`, `operador.dc.olinda`, `operador.dc.ipojuca`, `operador.dc.jaboatao`, `operador.bombeiros`, `operador.samu`, `org.maos`, `admin`.

## Cenários
| # | Cenário | Onde | O que mostrar |
|---|---|---|---|
| 1 | Prevenção | A: Início → "Usar minha localização" (ou escolher Recife) | Município identificado, aviso vigente, chuva da estação APAC mais próxima com horário, área documentada com distância e fonte. Frase: "ausência de registros não comprova ausência de perigo". |
| 2 | Alerta proativo | C: "Publicar aviso demonstrativo" → área *Ibura e entorno* → "Publicar e processar" | Contador de usuários notificados (só os locais dentro do polígono). Rótulo **[DEMONSTRAÇÃO]**. |
| 3 | Check-in | A: cartão "Você e sua família estão bem?" → **Estou seguro** | Registro salvo e transmitido; aparece em C (`/demo` → notificações). |
| 4 | Pedido de socorro | A (ou aba anônima): **Preciso de socorro** | Pedido enviado **antes** de qualquer pergunta; 193/199/192/190 visíveis. Complemento: "ilhados", 4 pessoas (criança de 3 anos, idosa de 78 com mobilidade reduzida), água subindo, rua sem acesso. |
| 5 | IA / regras | B: abrir a ocorrência | Sinalizações com evidência (ex.: `AGUA_SUBINDO_COM_ILHAMENTO`), "necessidade de assistência" em nível **atenção** (idade não é ameaça), contexto territorial, resumo da IA rotulado (ou "IA não configurada: regras aplicadas"). |
| 6 | Encaminhamento | B: aba destinos; abrir `/central/DC-RECIFE` e `/central/CBM-SIM` | Cada central **recebeu e persistiu**; protocolo `SIM-…`; horário de envio e de recebimento. |
| 7 | Painel | B | Localização, pessoas, IA, destinos, horários, confirmações, histórico; mudar o estado na central → "Equipe designada" aparece na linha do tempo de A. |
| 8 | Falha institucional | C: `CBM-SIM` → **Indisponível (503)**; A: novo pedido em Recife com "água subindo" | B mostra Bombeiros em "Falha temporária" com tentativas e a Defesa Civil recebida normalmente. C: voltar a **Normal** → retentativa → **Recebido**, sem duplicar. |
| 9 | Offline | A: DevTools → Network → **Offline**; enviar um complemento ou novo pedido | "Pedido salvo neste aparelho. Ainda não transmitido." → Meus pedidos: *salvo no aparelho* → **Online** → *recebido pelo servidor*. |
| 10 | Nearby | (sem aparelhos) | Explicar a PoC e mostrar o código e `docs/NEARBY_ANDROID_IOS.md`. Opcional: `simuladores/gateway-nearby.sh` faz o POST no gateway e o pedido aparece com canal "Gateway simulado". **Não afirmar que foi testado em aparelhos.** |
| 11 | Apoio comunitário | A: **Quero ajudar** → oferta para uma necessidade; outra janela: *Organização de apoio (fictícia)* → `/central/ORG-MAOS` | Organização confirma; o andamento aparece em A. |
| 12 | Abrigos | A: Mapa → camada Abrigos | Abrigos da COP Recife (fonte e data) e fictícios rotulados; **"Disponibilidade não verificada"** em todos. |

## Encerramento
Voltar ao painel com a linha do tempo completa e dizer: "Tudo o que vocês viram passou por HTTP, banco e protocolo de verdade; só o destinatário é simulado. O próximo passo é homologar com uma Defesa Civil."

## Plano B
- Sem internet no local: `docker compose up` no notebook (mapa base sem tiles; todo o resto funciona).
- Serviço hibernado: abrir `/health` e aguardar cerca de 1 minuto.
