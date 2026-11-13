# Arquitetura

**Monólito modular + Clean Architecture + Hexagonal (portas e adaptadores) + Vertical Slices**, num único processo implantável (custo zero) e preparado para extração futura.

```
┌───────────────────────── PWA (React/Vite) ─────────────────────────┐
│ Cidadão · Mapa (MapLibre) · Painel · Central simulada · Demo         │
│ IndexedDB (fila offline) · Service Worker (Workbox) · SignalR client │
└──────────────┬──────────────────────────────────────────▲──────────┘
               │ HTTPS/JSON                               │ WebSocket
┌──────────────▼──────────────────────────────────────────┴──────────┐
│ AlertaEmAcao.Api  (endpoints mínimos, autenticação Bearer, rate limit, │
│                    SignalR, OpenAPI, cabeçalhos de segurança)         │
├────────────────────────────────────────────────────────────────────┤
│ AlertaEmAcao.Modulos  (domínio + casos de uso; sem EF/ASP.NET)        │
│  Identidade · Municipios · Familias · Localizacao · Monitoramento     │
│  Alertas · Notificacoes · Ocorrencias · InteligenciaTerritorial       │
│  AnaliseSituacional · Comunicacao · Encaminhamento · Abrigos          │
│  ApoioComunitario        ── portas: IUnidadeTrabalho, IClienteCentral,│
│                            IProvedorIA, IFonteChuva, IEnviadorPush…   │
├────────────────────────────────────────────────────────────────────┤
│ AlertaEmAcao.Infraestrutura (EF Core/Npgsql, PostGIS, adaptadores     │
│  APAC/INMET/Recife/COP, IA Anthropic/OpenAI, Web Push, cifragem,      │
│  trabalhos em segundo plano: despacho, IA, alertas, fontes)           │
└──────────────┬──────────────────────────────────────────────────────┘
               │ HTTP real (contrato v1, HMAC, Idempotency-Key)
┌──────────────▼───────────────┐      ┌─────────────────────────────┐
│ AlertaEmAcao.CentraisSimuladas│      │ PostgreSQL 16 + PostGIS 3.4 │
│ (schema "simulador"; só       │─────▶│ schemas "alerta" e          │
│  depende de Contratos)        │      │ "simulador"                 │
└──────────────────────────────┘      └─────────────────────────────┘
```

## Regras de dependência (verificadas por testes de arquitetura)
- `Contratos` não depende de nada interno.
- `Modulos` não referencia EF Core, ASP.NET, Npgsql, Infraestrutura nem Api.
- `CentraisSimuladas` depende só de `Contratos`: poderia rodar em outro servidor (`Centrais__UrlBaseCentrais`).
- `Infraestrutura` não depende de `Api` nem das centrais.
- Entidades de domínio não expõem setters públicos.

## Fluxo principal (pedido de socorro)
1. O PWA gera o `id` (UUID), grava na fila do IndexedDB e transmite (`POST /api/ocorrencias`, `X-Dispositivo-Id`).
2. `ServicoOcorrencias` (transação única): idempotência pelo `id` → identifica o município (polígonos da APAC) → `MotorRegrasUrgencia` → `RoteadorEncaminhamento` → cria `EncaminhamentoInstitucional` pendentes → SignalR.
3. `TrabalhoDespacho` (a cada 1 s) envia por HTTP a cada central, em paralelo e de forma independente; só confirma com `2xx` mais protocolo; `5xx`/timeout geram backoff; `4xx` gera rejeição.
4. `TrabalhoAnaliseIA` (a cada 3 s) chama a IA se configurada, valida esquema e evidências e persiste. Nunca bloqueia o passo 3.
5. A central simulada muda o estado → `POST /api/integracoes/retorno-central` → histórico e status da ocorrência → SignalR ao cidadão e aos operadores.

## Decisões
| Decisão | Motivo |
|---|---|
| Monólito modular | Hospedagem gratuita com 1 processo; os limites entre módulos são preservados em namespaces e portas |
| PostGIS via colunas geradas + SQL | NetTopologySuite indisponível no ambiente de build; o domínio tem ray casting próprio, testado, e o PostGIS faz as consultas em conjunto (aviso × locais) |
| Outbox de encaminhamentos no mesmo banco | Garante que nenhum pedido persistido fique sem tentativa de entrega, mesmo após reinício (`RecuperarInterrompidosAsync`) |
| Bearer token do ASP.NET + chaves do Data Protection no banco | Sem pacote JWT externo; tokens sobrevivem a reinícios |
| Cifragem AES-256-GCM de campos sensíveis | Família, relato, trilha de localização, resultado da IA e contato do voluntário |
| Web Push implementado sem biblioteca | Sem dependência adicional; validado com o vetor oficial do RFC 8291 |

## Monitoramento familiar (13/11/2026)
- **Localizacao**: `LocalMonitorado` ampliado (tipo, moro aqui, endereço e moradores cifrados, origem/precisão) e a porta `IGeocodificador`; o adaptador `GeocodificadorPublico` (Infraestrutura) fala com ViaCEP e Nominatim, com `LimitadorNominatim` compartilhado.
- **Familias**: `VinculoFamiliar`, `ConviteVinculo`, `ServicoVinculos`.
- **Identidade**: `RegistroConsentimento` (append-only) e `ServicoConsentimentos`.
- **Notificacoes**: `NotificacaoFamiliar` e `ServicoNotificacaoFamiliar`, acionado pelo `NotificadorSignalR` depois que cada mudança da ocorrência é gravada (falha nunca derruba o pedido).
- **Ocorrencias**: `ResidenciaId` (só por consentimento D e escolha do solicitante) e origem do compartilhamento (`pedido`/`preferencia`).
- Endpoints em `ResidenciasEndpoints.cs`. Migração `ResidenciasVinculosConsentimentos` (colunas novas com padrão seguro; dados antigos preservados).
