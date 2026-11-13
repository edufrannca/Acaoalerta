#!/usr/bin/env bash
# Simula um aparelho-ponte (Nearby/LoRa) entregando um pedido FICTÍCIO ao gateway do Alerta em Ação.
# Não envolve aparelhos reais: apenas exercita o endpoint /api/gateway/pacotes.
# Uso: ./gateway-nearby.sh [nearby|lora] [lat] [lon]
. "$(dirname "$0")/_comum.sh"
CANAL="${1:-nearby}"; LAT="${2:--8.1180}"; LON="${3:--34.9410}"; RECEPTOR="gateway-demo"
CHAVE=$(curl -fsS "$URL/api/demo/gateway-chave?receptor=$RECEPTOR" -H "Authorization: Bearer $(token_admin)" | json 'd["chave"]')
ID=$(python3 -c 'import uuid;print(uuid.uuid4())')
curl -fsS -X POST "$URL/api/gateway/pacotes" -H 'Content-Type: application/json' -H "X-Gateway-Chave: $CHAVE" -d "{
  \"versao\":\"alerta-em-acao/gateway/v1\",\"id\":\"$ID\",\"dispositivoOrigem\":\"aparelho-ficticio-A\",\"receptor\":\"$RECEPTOR\",
  \"saltos\":1,\"criadoEm\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"latitude\":$LAT,\"longitude\":$LON,\"precisaoMetros\":30,
  \"pessoas\":3,\"aguaSubindo\":true,\"feridos\":false,\"texto\":\"[FICTÍCIO] Pedido retransmitido sem internet\",\"canal\":\"$CANAL\"}"; echo
echo "Pacote $ID entregue ao gateway (canal $CANAL). Veja no painel."
