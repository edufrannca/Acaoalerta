# Testes

Todos os números abaixo vêm de execuções reais em 13/11/2026 (.NET 10, PostgreSQL 16 + PostGIS 3.4 local, Chromium headless).

| Suíte | Ferramenta | Quantidade | Resultado |
|---|---|---|---|
| Unitários (domínio + protocolo de proximidade) | xUnit | 90 | 90 aprovados |
| Avaliação da IA (validador, Groq com HTTP controlado: 429, timeout, JSON inválido, indisponível) | xUnit | 24 | 24 aprovados (*) |
| Arquitetura (dependências entre camadas) | xUnit + reflexão | 7 | 7 aprovados |
| Integração (API + banco real + centrais via HTTP + gateway v2) | xUnit + WebApplicationFactory | 39 | 39 aprovados |
| Frontend (fila offline, telas, leitura por voz, acessibilidade) | Vitest + Testing Library + fake-indexeddb | 10 | 10 aprovados |
| Ponta a ponta (jornadas, instalação PWA, axe WCAG 2 A/AA em 5 telas, alto contraste) | Playwright + axe-core | 14 | 14 aprovados |
| Protocolo Kotlin contra os vetores | kotlinc 2.1 (JVM) | 3 vetores | conferem |
| Gateway ponta a ponta (`simuladores/nearby-ponta-a-ponta.py`) | script | 1 | passou — SIMULADO |
| Testes JUnit Android / XCTest iOS / builds nativos | Gradle / Xcode | — | **não executados** (CI bloqueado; sem SDK/Xcode no ambiente) |
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

## Não testado
- Nearby em aparelhos físicos (código não compilado).
- Web Push entregue em aparelho real (o envio está implementado; não houve VAPID nem aparelho na execução).
- Chamadas às APIs públicas ao vivo dentro dos testes (usamos respostas gravadas; a coleta ao vivo foi verificada manualmente pelo navegador).
- Carga e desempenho; acessibilidade com leitor de tela.
