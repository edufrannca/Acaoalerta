#!/usr/bin/env bash
# Publica um AVISO DEMONSTRATIVO (não oficial) e processa os alertas proativos.
# Uso: ./eventos-climaticos.sh [ibura|olinda|<codigoIBGE>] [duracaoMinutos]
. "$(dirname "$0")/_comum.sh"
AREA="${1:-ibura}"; DUR="${2:-120}"
case "$AREA" in
  ibura)  CORPO="{\"titulo\":\"Chuva intensa com risco de deslizamentos\",\"severidade\":\"Perigo\",\"poligonoGeoJson\":$(python3 -c 'import json;print(json.dumps(json.dumps({"type":"Polygon","coordinates":[[[-34.97,-8.15],[-34.92,-8.15],[-34.92,-8.10],[-34.97,-8.10],[-34.97,-8.15]]]})))'),\"duracaoMinutos\":$DUR}" ;;
  olinda) CORPO="{\"titulo\":\"Chuva intensa com risco de alagamentos\",\"severidade\":\"Perigo\",\"municipioCodigo\":\"2609600\",\"duracaoMinutos\":$DUR}" ;;
  *)      CORPO="{\"titulo\":\"Chuva intensa\",\"severidade\":\"Perigo\",\"municipioCodigo\":\"$AREA\",\"duracaoMinutos\":$DUR}" ;;
esac
TK=$(token_admin)
curl -fsS -X POST "$URL/api/demo/avisos" -H "Authorization: Bearer $TK" -H 'Content-Type: application/json' -d "$CORPO"; echo
curl -fsS -X POST "$URL/api/demo/avisos/processar" -H "Authorization: Bearer $TK"; echo
echo "Aviso DEMONSTRATIVO publicado (não é aviso oficial)."
