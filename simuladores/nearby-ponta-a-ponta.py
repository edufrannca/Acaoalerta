#!/usr/bin/env python3
"""Teste reproduzível do caminho A (sem internet) → Nearby → B (gateway) → API → centrais SIMULADAS.

Sem aparelhos físicos, este script faz o papel do aparelho B: monta uma mensagem v2 assinada exatamente como
os apps Android/iOS, entrega no gateway, reenvia (para provar que não duplica) e acompanha os níveis 4 e 5.
Resultado: SIMULADO (não substitui os testes físicos da matriz). Uso:
  ALERTA_URL=https://... python3 simuladores/nearby-ponta-a-ponta.py
"""
import importlib.util, json, os, sys, time, uuid, urllib.request
from datetime import datetime, timezone

URL = os.environ.get("ALERTA_URL", "http://localhost:8080").rstrip("/")
GRUPO = os.environ.get("ALERTA_GRUPO", "ALERTA-DEMO-2026")
RECEPTOR = os.environ.get("ALERTA_RECEPTOR", "gateway-script")
raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
spec = importlib.util.spec_from_file_location("proto", os.path.join(raiz, "docs", "protocolo", "gerar_vetores.py"))
# Reaproveita a implementação de referência (sem executar o gerador).
fonte = open(spec.origin, encoding="utf-8").read().split("CODIGO = ")[0]
proto = {}; exec(fonte, proto)

def chamar(metodo, caminho, corpo=None, cab=None):
    req = urllib.request.Request(URL + caminho, method=metodo, data=json.dumps(corpo).encode() if corpo is not None else None,
                                 headers={"Content-Type": "application/json", **(cab or {})})
    try:
        with urllib.request.urlopen(req, timeout=30) as r: return r.status, json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e: return e.code, json.loads(e.read() or b"{}")

def agora(): return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"

t0 = time.time()
_, tok = chamar("POST", "/api/auth/demo", {"perfil": "admin"}, {"X-Demo-Codigo": os.environ.get("ALERTA_CODIGO_DEMO", "")})
_, ch = chamar("GET", f"/api/demo/gateway-chave?receptor={RECEPTOR}", cab={"Authorization": "Bearer " + tok["accessToken"]})
cab = {"X-Gateway-Receptor": RECEPTOR, "X-Gateway-Chave": ch["chave"]}

ocorrencia = str(uuid.uuid4())
m = {"versaoProtocolo": proto["V"] if "V" in proto else "alerta-em-acao/proximidade/v2",
     "identificadorMensagem": str(uuid.uuid4()), "identificadorOcorrencia": ocorrencia, "tipoMensagem": "PEDIDO_SOCORRO",
     "municipio": "2611606", "coordenadasAutorizadas": {"latitude": -8.1262, "longitude": -34.9478, "precisaoMetros": 30},
     "horarioCriacao": agora(), "horarioUltimaAtualizacao": agora(), "remetentePseudonimizado": proto["pseudo"]("script-aparelho-A"),
     "dadosEstruturados": {"ficticio": True, "quantidadePessoas": 3, "idades": [5, 38, 77], "aguaSubindo": True, "ilhado": True,
                           "descricao": "[FICTÍCIO] teste ponta a ponta do gateway"},
     "numeroTentativas": 1, "estadoTransmissao": "SALVO_NO_APARELHO"}
m["integridadeMensagem"] = proto["integridade"](m, GRUPO)
print(f"1. Mensagem {m['identificadorMensagem']} · ocorrência {ocorrencia} (nível 1 simulado)")

t1 = time.time()
st, r1 = chamar("POST", "/api/gateway/v2/mensagens", {"mensagem": m, "receptorPseudonimizado": proto["pseudo"]("script-aparelho-B"),
                "recebidoLocalmenteEm": agora(), "canal": "nearby", "plataformaReceptor": "script"}, cab)
print(f"2. Gateway → servidor: HTTP {st} em {int((time.time()-t1)*1000)} ms · nível {r1.get('nivelComprovado')} · duplicado={r1.get('duplicado')}")
assert st == 200 and r1["nivelComprovado"] >= 4, r1

m2 = dict(m, numeroTentativas=2)
st, r2 = chamar("POST", "/api/gateway/v2/mensagens", {"mensagem": m2, "receptorPseudonimizado": proto["pseudo"]("script-aparelho-C"),
                "recebidoLocalmenteEm": agora(), "canal": "nearby"}, cab)
print(f"3. Reenvio por outro gateway: HTTP {st} · duplicado={r2.get('duplicado')}")
assert r2.get("duplicado") is True

for i in range(30):
    st, c = chamar("GET", f"/api/gateway/v2/ocorrencias/{ocorrencia}/confirmacoes", cab=cab)
    if c.get("nivelComprovado", 0) >= 5: break
    time.sleep(1)
print(f"4. Nível {c['nivelComprovado']} após {int((time.time()-t1)*1000)} ms")
for x in c["confirmacoes"]:
    print(f"   nível {x['nivel']}: {x['descricao']} · {x['origem']} → {x['destino']} · {x.get('comprovante') or ''}")
niveis = [x["nivel"] for x in c["confirmacoes"]]
assert all(niveis.count(n) == 1 for n in (1, 2, 3, 4)) and 5 in niveis and max(niveis) <= 5
print(f"5. OK em {int((time.time()-t0)*1000)} ms. Resultado: SIMULADO (sem aparelhos físicos). Nenhum órgão real foi acionado.")
