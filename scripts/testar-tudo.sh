#!/usr/bin/env bash
# Executa todos os testes automatizados. Requer PostgreSQL+PostGIS em localhost (docker compose up -d banco).
set -euo pipefail
cd "$(dirname "$0")/.."
echo "== Backend (.NET) =="; (cd backend && dotnet test AlertaEmAcao.sln)
echo "== Frontend (Vitest) =="; (cd apps/web && npx tsc -b && npx vitest run)
echo "== E2E (Playwright) — requer a aplicação rodando em http://localhost:8080 =="
(cd testes-e2e && npm ci --no-audit --no-fund && npx playwright test)
