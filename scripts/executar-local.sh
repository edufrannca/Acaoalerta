#!/usr/bin/env bash
# Execução local sem Docker para a aplicação (requer .NET 10 SDK, Node 22 e PostgreSQL com PostGIS).
# Banco via Docker: docker compose up -d banco
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f .env ] && set -a && . ./.env && set +a
export ConnectionStrings__Postgres="${ConnectionStrings__Postgres:-Host=localhost;Database=alerta;Username=postgres;Password=postgres}"
(cd apps/web && npm ci --no-audit --no-fund && npx vite build)
cd backend
ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}" ASPNETCORE_URLS=http://0.0.0.0:8080 \
  dotnet run --project src/AlertaEmAcao.Api
