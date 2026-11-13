# Centrais simuladas

> **CENTRAL SIMULADA: AMBIENTE DE DEMONSTRAÇÃO.** Nenhuma delas é ou representa um órgão real. Todos os nomes contêm "SIMULADA" (validado no domínio).

Módulo separado (`AlertaEmAcao.CentraisSimuladas`), com banco próprio (schema `simulador`) e acesso apenas por HTTP. Ele recebe exatamente o que receberia uma integração real.

## Centrais
- `DC-{municipio}`: Defesa Civil simulada de cada um dos 14 municípios.
- `CBM-SIM` (Bombeiros), `SAMU-SIM`, `PM-SIM`.
- `ORG-MAOS`, `ORG-MARE`: organizações de apoio fictícias.

## Endpoints
| Método | Rota | Uso |
|---|---|---|
| POST | `/simulador/centrais/{codigo}/mensagens` | Recebe (`X-Central-Chave` + `Idempotency-Key`); valida o esquema; **persiste e depois** devolve o protocolo `SIM-{codigo}-{data}-{seq}` |
| GET | `/simulador/centrais` | Lista com o modo atual |
| GET | `/simulador/centrais/{codigo}/recebimentos` | Painel da central (operador vinculado à central) |
| PATCH | `.../recebimentos/{protocolo}/estado` | Operador muda o estado → callback HTTP para o Alerta em Ação |
| PUT | `/simulador/centrais/{codigo}/modo` | Administrador: modo de falha |

## Modos de falha
`Normal`, `Indisponivel` (503), `Atraso` (configurável até 20 s, depois responde normalmente), `Rejeitar` (422), `ErroAutenticacao` (401), `Intermitente` (503 em ~50% das requisições), `Timeout` (espera 30 s e o remetente desiste). Use na tela `/demo` ou com `simuladores/falhas-de-rede.sh`.

## Autenticação
Chave por central = HMAC-SHA256(`Centrais__SegredoMestre`, `"central:"+codigo`). Nada fica em texto no repositório; a chave é derivada em execução.

## Interface
`/central/:codigo`: faixa vermelha "CENTRAL SIMULADA: AMBIENTE DE DEMONSTRAÇÃO", lista de recebimentos com protocolo, horário e conteúdo, e botões de estado.
