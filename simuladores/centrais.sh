#!/usr/bin/env bash
# Lista as CENTRAIS SIMULADAS e seus modos.  Uso: ./centrais.sh
. "$(dirname "$0")/_comum.sh"
echo "CENTRAL SIMULADA — AMBIENTE DE DEMONSTRAÇÃO ($URL)"
curl -fsS "$URL/simulador/centrais" -H "Authorization: Bearer $(token_admin)" | python3 -c '
import json, sys
for c in json.load(sys.stdin)["centrais"]:
    print("{:<14} {:<18} {}".format(c.get("codigo", "?"), str(c.get("modo", "?")), c.get("nome", "")))'
