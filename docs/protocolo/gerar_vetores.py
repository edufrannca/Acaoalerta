#!/usr/bin/env python3
"""Gera vetores de teste do protocolo alerta-em-acao/proximidade/v2 a partir da especificação
(implementação independente em Python). C#, Kotlin e Swift verificam o mesmo arquivo."""
import base64, hashlib, hmac, json, sys

ROTULO = b"alerta-em-acao/proximidade/v2"

def chave_grupo(codigo): return hmac.new(ROTULO, codigo.strip().upper().encode(), hashlib.sha256).digest()
def esc(s): return s.replace("\\", "\\\\").replace("\n", "\\n").replace("\r", "\\r")

def valor(v):
    if v is None: return None
    if isinstance(v, bool): return "true" if v else "false"
    if isinstance(v, int): return str(v)
    if isinstance(v, str): return esc(v)
    if isinstance(v, list):
        out = []
        for x in v:
            if isinstance(x, bool): out.append("true" if x else "false")
            elif isinstance(x, int): out.append(str(x))
            elif isinstance(x, str): out.append(esc(x).replace(",", "\\,"))
            else: out.append("")
        return ",".join(out)
    return None

def canonica(m):
    c = m.get("coordenadasAutorizadas")
    linhas = [m["versaoProtocolo"], m["identificadorMensagem"], m["identificadorOcorrencia"], m["tipoMensagem"], m.get("municipio") or "",
              f'{c["latitude"]:.6f}' if c else "", f'{c["longitude"]:.6f}' if c else "",
              str(int(round(c["precisaoMetros"]))) if c and c.get("precisaoMetros") is not None else "",
              m["horarioCriacao"], m["horarioUltimaAtualizacao"], m["remetentePseudonimizado"], m.get("identificadorCorrelacao") or ""]
    s = "".join(l + "\n" for l in linhas)
    dados = m.get("dadosEstruturados") or {}
    for k in sorted(dados.keys()):
        v = valor(dados[k])
        if v is not None: s += f"{k}={v}\n"
    return s

def integridade(m, codigo):
    mac = hmac.new(chave_grupo(codigo), canonica(m).encode(), hashlib.sha256).digest()
    return "hmac-sha256:" + base64.urlsafe_b64encode(mac).decode().rstrip("=")

def pseudo(inst): return "ap-" + hashlib.sha256(inst.encode()).hexdigest()[:16]

CODIGO = "ALERTA-DEMO-2026"
V = "alerta-em-acao/proximidade/v2"
msgs = [
  {"versaoProtocolo": V, "identificadorMensagem": "6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b", "identificadorOcorrencia": "0b8e7d6c-5f4a-4b3c-9d2e-1f0a9b8c7d6e",
   "tipoMensagem": "PEDIDO_SOCORRO", "municipio": "2611606",
   "coordenadasAutorizadas": {"latitude": -8.1262, "longitude": -34.9478, "precisaoMetros": 12.4},
   "horarioCriacao": "2026-11-13T14:05:09.123Z", "horarioUltimaAtualizacao": "2026-11-13T14:05:09.123Z",
   "remetentePseudonimizado": pseudo("instalacao-ficticia-A"),
   "dadosEstruturados": {"ficticio": True, "quantidadePessoas": 4, "idades": [3, 34, 78], "aguaSubindo": True, "ilhado": True,
                         "mobilidadeReduzida": True, "descricao": "[FICTÍCIO] Água subindo, 2º andar,\nrua sem acesso", "referenciaLocal": None},
   "numeroTentativas": 0, "estadoTransmissao": "SALVO_NO_APARELHO"},
  {"versaoProtocolo": V, "identificadorMensagem": "a2b3c4d5-e6f7-4a8b-9c0d-1e2f3a4b5c6d", "identificadorOcorrencia": "c7d8e9f0-a1b2-4c3d-8e4f-5a6b7c8d9e0f",
   "tipoMensagem": "PEDIDO_SOCORRO", "horarioCriacao": "2026-11-13T14:10:00.000Z", "horarioUltimaAtualizacao": "2026-11-13T14:10:00.000Z",
   "remetentePseudonimizado": pseudo("instalacao-ficticia-B"), "numeroTentativas": 2},
  {"versaoProtocolo": V, "identificadorMensagem": "11111111-2222-4333-8444-555555555555", "identificadorOcorrencia": "0b8e7d6c-5f4a-4b3c-9d2e-1f0a9b8c7d6e",
   "tipoMensagem": "CONFIRMACAO", "horarioCriacao": "2026-11-13T14:05:11.500Z", "horarioUltimaAtualizacao": "2026-11-13T14:05:11.500Z",
   "remetentePseudonimizado": pseudo("instalacao-ficticia-C"), "identificadorCorrelacao": "6f1d2c3b-4a59-4e6f-8a7b-9c0d1e2f3a4b",
   "dadosEstruturados": {"nivel": 2, "comprovante": "persistido:recebidos.jsonl#7"}, "numeroTentativas": 0, "estadoTransmissao": "RECEBIDO_POR_PROXIMIDADE"},
]
vetores = []
for m in msgs:
    m = dict(m); m["integridadeMensagem"] = integridade(m, CODIGO)
    vetores.append({"mensagem": m, "canonica": canonica(m), "integridade": m["integridadeMensagem"]})
saida = {"versaoProtocolo": V, "codigoGrupo": CODIGO, "chaveGrupoHex": chave_grupo(CODIGO).hex(),
         "pseudonimos": [{"instalacao": i, "remetente": pseudo(i)} for i in ["instalacao-ficticia-A", "instalacao-ficticia-B"]],
         "identificadorServicoNearby": "br.alertaemacao.nearby",
         "servicoBonjour": "_" + hashlib.sha256(b"br.alertaemacao.nearby").digest()[:6].hex().upper() + "._tcp",
         "vetores": vetores}
json.dump(saida, open(sys.argv[1], "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print(saida["servicoBonjour"])
