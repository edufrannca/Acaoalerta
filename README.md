# Alerta em Ação

**Demonstração publicada:** https://alerta-em-acao.onrender.com (instalar no celular: `/instalar`)

**Rede metropolitana de prevenção, comunicação resiliente e proteção comunitária para os 14 municípios da Região Metropolitana do Recife.**

Hackathon *Devs Criam com IA 2026*, Trilha 2: Alertar e Proteger.

> **Ambiente de demonstração.** As centrais da Defesa Civil, dos Bombeiros, do SAMU, da PM e as organizações de apoio são **simuladas**. Nenhum órgão real é acionado. Em emergência real, ligue **193** (Bombeiros) ou **199** (Defesa Civil).

## O que o sistema faz (e está funcionando)

| Fluxo | Situação |
|---|---|
| Pedido de socorro com um toque: id gerado no aparelho, horário, GPS, persistência local, transmissão e complemento opcional | **Real**, testado (unitário, integração, E2E) |
| Regras determinísticas de urgência com evidência para cada sinalização (não ordenam vidas) | **Real**, testado |
| IA com saída em esquema validado e descarte de itens sem evidência literal; falha da IA não bloqueia nada | **Real**. Exige chave de API (`IA__ChaveApi`); validada com respostas simuladas |
| Roteamento por município e tipo de risco para as 14 Defesas Civis e para Bombeiros, SAMU e PM | **Real**: regras configuráveis no banco |
| Centrais receptoras: HTTP, autenticação HMAC, validação de esquema, persistência, protocolo, idempotência, falhas controladas, retorno HTTP | **Simuladas com comportamento técnico real** |
| Retentativa com backoff, confirmação individual por central, nenhum "recebido" sem protocolo | **Real**, testado |
| Alerta proativo: aviso → PostGIS cruza a área com os locais monitorados de quem consentiu → "Você e sua família estão bem?" | **Real** (avisos do INMET ao vivo e avisos demonstrativos) |
| Offline-first: IndexedDB + service worker, "Pedido salvo neste aparelho. Ainda não transmitido.", sincronização sem duplicar | **Real**, testado no Playwright com a rede desligada |
| Mapa MapLibre: limites dos 14 municípios, chuva da APAC, áreas de risco do Recife, abrigos do COP, relatos agregados, avisos | **Real** com fontes públicas. Abrigos fictícios são identificados |
| Painel de coordenação ao vivo (SignalR), verificação, reenvio e auditoria de acesso | **Real** |
| Apoio comunitário: oferta → necessidade publicada → organização simulada confirma | **Real** (organizações fictícias) |
| Web Push (VAPID e aes128gcm, implementação própria testada contra o RFC 8291) | **Real**. Exige chaves VAPID; não verificado com aparelho físico |
| Nearby Connections (Android e iOS) | **Prova de conceito escrita, não compilada nem testada em dispositivos** |

Detalhes em [docs/LIMITACOES.md](docs/LIMITACOES.md) e [docs/INTEGRACOES.md](docs/INTEGRACOES.md).

## Arquitetura em uma frase

Monólito modular em **ASP.NET Core 10** (Clean, Hexagonal e Vertical Slice) com **PostgreSQL + PostGIS**, PWA em **React 19 + Vite + Tailwind + MapLibre**, centrais simuladas num módulo isolado que conversa com a plataforma apenas por **contratos HTTP versionados**. Ver [docs/ARQUITETURA.md](docs/ARQUITETURA.md).

```
apps/web               PWA do cidadão, painel, central simulada e controle da demonstração
apps/android-nearby    PoC Kotlin (Nearby Connections)
apps/ios-nearby        PoC Swift (esqueleto)
backend/src            Contratos · Modulos (14 módulos de domínio) · Infraestrutura · CentraisSimuladas · Api
backend/tests          Unitarios · Integracao (PostgreSQL real) · Arquitetura · AvaliacaoIA
testes-e2e             Playwright (jornadas completas)
simuladores            scripts de eventos climáticos e falhas de rede
dados                  fontes oficiais usadas e descrição dos dados fictícios
docs                   documentação
```

## Executar localmente

Requisitos: .NET 10 SDK, Node 22 e Docker (ou PostgreSQL 16 com PostGIS).

```bash
cp .env.example .env && node scripts/gerar-chaves.mjs >> .env   # segredos de desenvolvimento
docker compose up -d banco
./scripts/executar-local.sh            # compila a PWA e sobe a API em http://localhost:8080
```

Ou tudo em contêiner: `docker compose up --build`.

Entre em **Perfil → Modo demonstração** e escolha um perfil fictício (Ana, Defesa Civil Recife, Administrador…). O roteiro completo está em [docs/ROTEIRO_DEMONSTRACAO.md](docs/ROTEIRO_DEMONSTRACAO.md).

## Testes

```bash
./scripts/testar-tudo.sh
```

| Conjunto | Quantidade | Resultado na última execução (13/11/2026) |
|---|---|---|
| Backend: unitários (inclui protocolo de proximidade com vetores compartilhados) | 90 | passaram |
| Backend: integração (PostgreSQL + PostGIS reais, HTTP real até as centrais, gateway v2) | 39 | passaram |
| Backend: arquitetura (dependências entre camadas) | 7 | passaram |
| Backend: avaliação de IA (casos fictícios, alucinação, esquema, falhas da Groq) | 24 | passaram (a inferência real é ignorada sem chave — **não executada até agora**) |
| Frontend: Vitest + Testing Library | 10 | passaram |
| E2E: Playwright (jornadas, offline real, instalação PWA, axe WCAG) | 14 | passaram |
| Apps nativos (APK, iOS) e aparelhos físicos | — | **não executados** — ver docs/MATRIZ_TESTES_DISPOSITIVOS.md |

Ver [docs/TESTES.md](docs/TESTES.md).

## Publicação

`render.yaml` (Render, plano gratuito: serviço Docker + PostgreSQL). Passo a passo e alternativas (Neon) em [docs/DEPLOY.md](docs/DEPLOY.md). Infraestrutura gratuita **não** é adequada para operação real de emergência.

## Documentação

[Visão do produto](docs/VISAO_PRODUTO.md) · [Arquitetura](docs/ARQUITETURA.md) · [Requisitos funcionais](docs/REQUISITOS_FUNCIONAIS.md) · [Não funcionais](docs/REQUISITOS_NAO_FUNCIONAIS.md) · [Municípios da RMR](docs/MUNICIPIOS_RMR.md) · [Integrações](docs/INTEGRACOES.md) · [Inteligência territorial](docs/INTELIGENCIA_TERRITORIAL.md) · [IA](docs/INTELIGENCIA_ARTIFICIAL.md) · [Alertas proativos](docs/ALERTAS_PROATIVOS.md) · [Offline](docs/COMUNICACAO_OFFLINE.md) · [Nearby](docs/NEARBY_ANDROID_IOS.md) · [Encaminhamento](docs/ENCAMINHAMENTO_INSTITUCIONAL.md) · [Centrais simuladas](docs/CENTRAIS_SIMULADAS.md) · [Segurança e LGPD](docs/SEGURANCA_LGPD.md) · [Modelo de dados](docs/MODELO_DADOS.md) · [Testes](docs/TESTES.md) · [Deploy](docs/DEPLOY.md) · [Limitações](docs/LIMITACOES.md) · [Roadmap](docs/ROADMAP.md) · [Roteiro da demonstração](docs/ROTEIRO_DEMONSTRACAO.md) · [Pitch](docs/PITCH.md) · [Protocolo de proximidade](docs/PROTOCOLO_PROXIMIDADE.md) · [Matriz de testes em aparelhos](docs/MATRIZ_TESTES_DISPOSITIVOS.md)
