#!/usr/bin/env bash
# Altera o modo de falha de uma CENTRAL SIMULADA.
# Uso: ./falhas-de-rede.sh <CODIGO> <Normal|Indisponivel|Atraso|Rejeitar|ErroAutenticacao|Intermitente|Timeout> [atrasoMs]
# Ex.: ./falhas-de-rede.sh CBM-SIM Indisponivel   ...   ./falhas-de-rede.sh CBM-SIM Normal
. "$(dirname "$0")/_comum.sh"
COD="${1:?código da central}"; MODO="${2:?modo}"; ATRASO="${3:-5000}"
curl -fsS -X PUT "$URL/simulador/centrais/$COD/modo" -H "Authorization: Bearer $(token_admin)" \
  -H 'Content-Type: application/json' -d "{\"modo\":\"$MODO\",\"atrasoMs\":$ATRASO}" >/dev/null
echo "Central simulada $COD agora em modo $MODO."
