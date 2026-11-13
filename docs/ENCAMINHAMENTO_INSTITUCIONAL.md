# Encaminhamento institucional

> **AMBIENTE SIMULADO: NENHUM ÓRGÃO REAL FOI ACIONADO.** Todos os destinos são centrais simuladas do próprio sistema.

## Roteamento (`RoteadorEncaminhamento`, domínio puro)
Regras em banco (`regras_encaminhamento`): município (ou todos) + gatilho → tipo de instituição. Os indícios vêm das regras determinísticas (`DetectorIndicios`).

| Gatilho | Destino (simulado) |
|---|---|
| `Sempre` | Defesa Civil do município identificado |
| `RiscoHidrologico`, `RiscoGeologico`, `PessoaIsolada` | + Corpo de Bombeiros |
| `PessoaFerida`, `NecessidadeMedica` | + SAMU |
| `SegurancaPublica` | + Polícia Militar |

- Preferência pela instituição do município; senão, a de jurisdição metropolitana/estadual.
- Fora da RMR ou município desconhecido → nenhum envio automático; fica para triagem humana no painel.
- Organizações de apoio não recebem ocorrências; recebem **ofertas de ajuda** (fluxo "Quero ajudar").
- Complemento posterior pode adicionar destinos e envia atualização às centrais já acionadas.

## Máquina de estados (`EncaminhamentoInstitucional`)
```
Pendente ─► Transmitindo ─► Recebido (protocolo da central)
              ├─► FalhaTemporaria ─(backoff 2^n s, máx. 60 s)─► Transmitindo
              ├─► Rejeitado (4xx definitivo: esquema, negócio ou autenticação)
              └─► Esgotado (12 tentativas)
FalhaTemporaria / Rejeitado / Esgotado → botão "Reenviar agora" no painel (operador ou administrador)
```
- Criado na **mesma transação** da ocorrência (padrão outbox); o `TrabalhoDespacho` (1 s) entrega. Mensagens montadas em sequência e enviadas em paralelo: uma central lenta não atrasa as outras.
- `Idempotency-Key` = id do encaminhamento: reenvio nunca duplica na central.
- "Recebido" só existe quando a central responde com protocolo **depois de persistir**.
- Retorno de estado da central (`POST /api/integracoes/retorno-central`, chave `X-Retorno-Chave`) aparece na linha do tempo do cidadão.

## Contrato `alerta-em-acao/central/v1`
Categoria, situação, localização (exata para destinos operacionais), pessoas, sinalizações com evidências, resumo (rotulado "gerado por IA" quando houver) e contato só se autorizado. Campo `ambiente = "DEMONSTRACAO"` obrigatório.

## Testes de integração (executados)
Recife com água subindo → Defesa Civil + Bombeiros com protocolo; os 14 municípios têm Defesa Civil própria; reenvio idempotente; falha de uma central não afeta outra e a retentativa entrega sem duplicar; rejeição registrada e não reenviada automaticamente (422 e 401); atraso controlado ainda entrega; a central valida autenticação, esquema e idempotência; complemento adiciona destino; operador muda o estado e o retorno HTTP chega ao Alerta em Ação.
