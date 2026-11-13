# Testes

Todos os números abaixo vêm de execuções reais em 13/11/2026 (.NET 10, PostgreSQL 16 + PostGIS 3.4 local, Chromium headless).

| Suíte | Ferramenta | Quantidade | Resultado |
|---|---|---|---|
| Unitários (domínio, protocolo de proximidade, área aproximada, comparação de GPS, ofertas) | xUnit | 100 | 100 aprovados |
| Avaliação da IA (validador, Groq com HTTP controlado: 429, timeout, JSON inválido, indisponível) | xUnit | 24 | 24 aprovados (*) |
| Arquitetura (dependências entre camadas) | xUnit + reflexão | 7 | 7 aprovados |
| Integração (API + banco real + centrais via HTTP + gateway v2 + comunidade + retransmissão QR) | xUnit + WebApplicationFactory | 52 | 52 aprovados |
| Frontend (fila offline, telas, leitura por voz, acessibilidade, QR de retransmissão) | Vitest + Testing Library + fake-indexeddb | 16 | 16 aprovados |
| Ponta a ponta (jornadas, offline → QR → aparelho próximo, comunidade e oferta, instalação PWA, axe WCAG 2 A/AA em 9 telas, alto contraste) | Playwright + axe-core | 22 | 22 aprovados |
| Protocolo Kotlin contra os vetores | kotlinc 2.1 (JVM) | 3 vetores | conferem |
| Gateway ponta a ponta (`simuladores/nearby-ponta-a-ponta.py`) | script | 1 | passou — SIMULADO |
| Testes JUnit Android / XCTest iOS / builds nativos (inclui importação do pedido da PWA) | Gradle / Xcode | — | **não executados** (CI bloqueado; sem SDK/Xcode no ambiente) — código escrito, não compilado |
| Testes em aparelhos físicos | — | — | **não executados** — ver MATRIZ_TESTES_DISPOSITIVOS |

(*) Um dos 24 é a inferência real (Groq `openai/gpt-oss-20b`). Sem `IA__CHAVEAPI` ele registra "IGNORADO" e retorna — contado como aprovado pelo xUnit, mas **não é inferência real**. No CI, roda no job `ia-groq` quando o secret `GROQ_API_KEY` existe e grava `relatorio-ia-groq.json`. Até esta versão **nenhuma inferência real foi executada** (rede do ambiente bloqueia api.groq.com e o Actions não inicia).

## Como executar
```bash
docker compose up -d banco            # PostGIS em localhost:5432
cd backend && dotnet test AlertaEmAcao.sln
cd apps/web && npm ci && npx vitest run
./scripts/executar-local.sh           # app em http://localhost:8080
cd testes-e2e && npm ci && npx playwright install chromium && npx playwright test
```
Ou `./scripts/testar-tudo.sh`. A CI (`.github/workflows/ci.yml`) roda backend e frontend com um serviço PostGIS.

## O que é coberto
- **Domínio:** identificação dos 14 municípios por ponto (incluindo bordas e pontos fora da RMR), regras de urgência ("idade sozinha não é ameaça"), roteamento, retentativa, política de localização, parsers APAC/INMET.
- **Integração:** fluxo completo de socorro até o protocolo da central; idempotência; isolamento entre dispositivos e entre centrais; os 7 modos de falha; retorno de estado; alertas proativos por polígono e por município; apoio comunitário; abrigos; cifragem em repouso; exportação LGPD; gateway Nearby simulado; cabeçalhos de segurança; validação de entrada.
- **Adaptadores:** parsers testados com amostras no formato real (campos verificados nas respostas ao vivo da APAC, INMET, COP e CKAN Recife), sem rede.
- **E2E:** SOS sem cadastro → central simulada recebe; **offline real** (`setOffline`) → fila → sincroniza com 1 registro; alerta → "Estou seguro"; painel do operador; falha e reenvio de central; "Quero ajudar" → organização confirma.

## Rede comunitária e retransmissão (novos)
- Pedido só aparece para a comunidade com autorização do dono; revogação remove; outro aparelho não autoriza (403).
- Resposta pública sem nome, telefone, relato, referência, coordenadas nem id do aparelho; faixas para crianças/idosos/mobilidade.
- Área pública contém o ponto registrado (5.000 pontos aleatórios); busca centrada no GPS de quem consulta.
- Conta fictícia fora da lista pública; aparece só na lista demonstrativa.
- Oferta: exige ciência de segurança; não abre para pedido não compartilhado nem para o próprio solicitante; aceite e encaminhamento só pela central do pedido; voluntário só vê a orientação depois do encaminhamento.
- QR: integridade adulterada → 422; posição do emissor preservada; posição do ajudante separada; QR sem coordenadas → "localização não informada" mesmo com GPS do ajudante; segundo leitor → `duplicado=true`; emissor sincroniza depois com o mesmo id (200) e continua dono; o ajudante não acessa o pedido.
- Gateway v2 com `posicaoReceptor` e mensagem sem coordenadas → ocorrência sem posição; GPS do gateway guardado à parte.
- Paridade do texto canônico do QR entre PWA (TypeScript) e servidor (C#) com o mesmo vetor.

## Não testado
- Nearby em aparelhos físicos (código não compilado).
- Web Push entregue em aparelho real (o envio está implementado; não houve VAPID nem aparelho na execução).
- Chamadas às APIs públicas ao vivo dentro dos testes (usamos respostas gravadas; a coleta ao vivo foi verificada manualmente pelo navegador).
- Carga e desempenho; acessibilidade com leitor de tela.

## Validação no ambiente publicado (13/11/2026, 02:30)
| Verificação | Resultado |
|---|---|
| `/health` | ok, banco ok, PostGIS 3.6.2 |
| Gateway v2 → API → centrais SIMULADAS | nível 5 em 2,6 s; protocolos DC-RECIFE e CBM-SIM; reenvio por outro gateway `duplicado=true` |
| IA Groq real (`openai/gpt-oss-20b`) | Concluida em 839 ms, esquema válido; resumo com 1 imprecisão (ver INTELIGENCIA_ARTIFICIAL) |
| Alerta demonstrativo (área Ibura) | 1 notificação; pergunta "Há um aviso para sua região. Você e sua família estão bem?" |
| `/instalar`, manifest | 200; 4 ícones (2 maskable) |
