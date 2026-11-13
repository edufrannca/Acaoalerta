#!/usr/bin/env bash
# Funções comuns dos simuladores. Só chamam o próprio Alerta em Ação (nunca um órgão real).
set -euo pipefail
URL="${ALERTA_URL:-http://localhost:8080}"
command -v curl >/dev/null || { echo "curl é necessário"; exit 1; }
command -v python3 >/dev/null || { echo "python3 é necessário (para ler JSON)"; exit 1; }
json() { python3 -c "import json,sys; d=json.load(sys.stdin); print(eval(sys.argv[1]))" "$1"; }
token_admin() {
  curl -fsS -X POST "$URL/api/auth/demo" -H 'Content-Type: application/json' -H "X-Demo-Codigo: ${ALERTA_CODIGO_DEMO:-}" -d '{"perfil":"admin"}' | json 'd["accessToken"]'
}
