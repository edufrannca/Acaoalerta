# Comunicação offline-first

## Componentes
- **Service Worker (Workbox via vite-plugin-pwa):** pré-cache do app shell (HTML, JS, CSS, fontes, ícones); `NetworkFirst` com fallback para municípios, limites, abrigos, necessidades e áreas de risco; `CacheFirst` para os tiles do mapa já vistos.
- **IndexedDB (`idb`):** loja `fila` (envios), `config` (id do dispositivo) e `cache` (últimos dados exibidos, com data "cópia salva").
- **Fila (`filaOffline.ts`):**
  1. grava o item com o **mesmo id usado no servidor**;
  2. tenta transmitir em seguida;
  3. estados: `salvo_no_aparelho` → `transmitindo` → `recebido_servidor` | `recusado`;
  4. `5xx`, timeout, rede, 408, 409 e 429 voltam para a fila; outros `4xx` → recusado (com mensagem);
  5. complementos esperam o pedido principal chegar (dependência `referenciaId`);
  6. sincroniza no evento `online` e a cada 15 s com o app aberto.
- **Servidor:** idempotente pelo id; reenvio retorna `200` com o mesmo registro.

## O que a interface diz
- Sem rede: **"Pedido salvo neste aparelho. Ainda não transmitido."**, junto com a recomendação de ligar 193.
- Recebido: "Recebido pelo servidor do Alerta em Ação", que é diferente de "central confirmou", exibido por destino e com protocolo.
- Persistência local **nunca** é apresentada como comunicação com uma autoridade.

## Testes executados
- **Vitest** (fake-indexeddb): sem rede fica salvo; 2xx → recebido; 5xx fica / 4xx recusado; 409 não é definitivo; complemento depois do pai; reenvio com o mesmo id; cabeçalho do dispositivo.
- **Playwright (rede realmente desligada com `context.setOffline`)**: abrir o app → desligar → pedir socorro → ver "salvo neste aparelho" → "Meus pedidos" mostra `salvo_no_aparelho` → religar → `recebido_servidor` → o servidor tem **exatamente 1** ocorrência com aquele id. **Passou.**

## Limitações
- Sem Background Sync: o envio acontece com o app aberto (ou quando ele for reaberto).
- Primeira visita precisa de internet para instalar o app shell.
- Mapa offline só mostra os tiles já vistos; limites e camadas vêm do cache de dados.
