# Testes

Todos os números abaixo vêm de execuções reais em 12/11/2026 (.NET 10, PostgreSQL 16 + PostGIS 3.4 local, Chromium headless).

| Suíte | Ferramenta | Quantidade | Resultado |
|---|---|---|---|
| Unitários (domínio) | xUnit | 87 | 87 aprovados |
| Avaliação da IA | xUnit | 19 | 19 aprovados (*) |
| Arquitetura (dependências entre camadas) | xUnit + reflexão | 7 | 7 aprovados |
| Integração (API + banco real + centrais via HTTP) | xUnit + WebApplicationFactory | 38 | 38 aprovados |
| Frontend (fila offline, telas) | Vitest + Testing Library + fake-indexeddb | 8 | 8 aprovados |
| Ponta a ponta (jornadas) | Playwright | 6 | 6 aprovados |

(*) Um dos 19 é a avaliação com modelo real. Sem `IA__CHAVEAPI` ele apenas registra "IGNORADO" e retorna; **nenhum modelo real foi avaliado**.

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
