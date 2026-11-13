# Alertas proativos

```
INMET (10 min) ─┐                 ┌─ cria Notificacao (única por aviso+usuário)
                ├─ Aviso (vigência,├─ caixa do app (sempre)
Demonstrativo ──┘  área, origem)   ├─ SignalR "notificacao" (conexões abertas)
                        │          └─ Web Push (se consentido + VAPID + assinatura)
                        ▼
      PostGIS: ST_Intersects(aviso.geom, local.geom)
      ou município ∈ aviso.municipios
      AND usuário.ConsenteAlertas AND local.Ativo
```

1. `ServicoAlertas.SincronizarOficiaisAsync`: busca, filtra a RMR por código IBGE, valida a vigência e deduplica por `CodigoExterno`.
2. `ProcessarNovosAsync` (a cada 5 s): para cada aviso vigente ainda não processado, consulta os locais abrangidos de usuários que consentiram e cria **uma** notificação por usuário (índice único `AvisoId+UsuarioId`).
3. Entrega: na caixa do app, em tempo real e via push, com registro por canal. "Aceito pelo serviço de push" **não** é tratado como "entregue no aparelho".
4. O app mostra o cartão **"Atenção à sua região" / "Você e sua família estão bem?"** com **Estou seguro**, **Preciso de socorro** (cria o pedido vinculado ao aviso) e **Ver orientações**.

## Demonstração
`/demo` → "Publicar aviso demonstrativo" (áreas pré-definidas que cobrem os locais fictícios de Ana e João, ou um município). O título recebe o prefixo **[DEMONSTRAÇÃO]** e a cor roxa; nunca é chamado de oficial.

## Testado
Integração: aviso em polígono notifica só Ana (não João, que está em Olinda); reprocessar não duplica; aviso por município notifica só os locais daquele município; resposta registrada. E2E: alerta → pergunta → Estou seguro.

## Limitações
- PWA não monitora localização em segundo plano; o cruzamento usa os **locais monitorados** cadastrados.
- Push depende do navegador e da conexão; no iOS exige o app instalado (16.4+).
- Cell broadcast e SMS 40199 da Defesa Civil são canais oficiais complementares, fora do escopo.
