# Simuladores

> **AMBIENTE SIMULADO: NENHUM ÓRGÃO REAL FOI ACIONADO.** Os scripts chamam apenas o próprio Alerta em Ação.

Requisitos: `bash`, `curl`, `python3` e o modo demonstração ativo (`Demo__Habilitado=true`).
Por padrão apontam para `http://localhost:8080`; use `ALERTA_URL=https://sua-url ./script.sh`.

| Script | O que faz |
|---|---|
| `centrais.sh` | Lista as centrais simuladas e o modo de cada uma |
| `falhas-de-rede.sh <CODIGO> <MODO> [atrasoMs]` | Coloca uma central em `Normal`, `Indisponivel`, `Atraso`, `Rejeitar`, `ErroAutenticacao`, `Intermitente` ou `Timeout` |
| `eventos-climaticos.sh [ibura\|olinda\|<IBGE>] [min]` | Publica um aviso **demonstrativo** (não oficial) e processa os alertas proativos |
| `gateway-nearby.sh [nearby\|lora]` | Entrega um pedido fictício ao gateway (`/api/gateway/pacotes`), como faria um aparelho-ponte |

## Cenário de falha completo
```bash
./falhas-de-rede.sh CBM-SIM Indisponivel
# envie um pedido com "água subindo" em Recife pelo app
./falhas-de-rede.sh CBM-SIM Normal      # a retentativa entrega sem duplicar
```

Os mesmos controles existem na tela `/demo` (perfil Administrador da demonstração).
Executados contra a instância local em 12/11/2026: `centrais.sh`, `falhas-de-rede.sh`, `eventos-climaticos.sh ibura` (1 notificação gerada) e `gateway-nearby.sh` (pedido registrado com canal Nearby).
