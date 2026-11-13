# Testes

Todos os números abaixo vêm de execuções locais reais em 13/11/2026 (.NET 10, PostgreSQL 16 + PostGIS 3.4 local, Chromium headless).

| Suíte | Ferramenta | Quantidade | Resultado |
|---|---|---|---|
| Unitários (domínio, protocolo de proximidade, área aproximada, comparação de GPS, ofertas) | xUnit | 114 | 114 aprovados |
| Avaliação da IA (validador, Groq com HTTP controlado: 429, timeout, JSON inválido, indisponível) | xUnit | 24 | 24 aprovados (*) |
| Arquitetura (dependências entre camadas) | xUnit + reflexão | 7 | 7 aprovados |
| Integração (API + banco real + centrais via HTTP + gateway v2 + comunidade + retransmissão QR + locais, vínculos, consentimentos, notificações familiares) | xUnit + WebApplicationFactory | 64 | 64 aprovados |
| Frontend (fila offline, telas, leitura por voz, acessibilidade, QR de retransmissão, cartão comunitário mínimo) | Vitest + Testing Library + fake-indexeddb | 26 | 26 aprovados |
| Ponta a ponta (jornadas, offline → QR → aparelho próximo, comunidade e oferta, família avisada, instalação PWA, axe WCAG 2 A/AA em 12 telas, alto contraste) | Playwright + axe-core | 25 | 25 aprovados |
| Protocolo Kotlin contra os vetores | kotlinc 2.1 (JVM) | 3 vetores | conferem |
| Gateway ponta a ponta (`simuladores/nearby-ponta-a-ponta.py`) | script | 1 | passou — SIMULADO |
| Testes JUnit Android / XCTest iOS / builds nativos (inclui importação do pedido da PWA) | Gradle / Xcode | — | **não executados** (CI bloqueado; sem SDK/Xcode no ambiente) — código escrito, não compilado |
| Testes em aparelhos físicos | — | — | **não executados** — ver MATRIZ_TESTES_DISPOSITIVOS |

(*) Um dos 24 é a inferência real (Groq `openai/gpt-oss-20b`). Sem `IA__CHAVEAPI` ele registra "IGNORADO" e retorna — contado como aprovado pelo xUnit, mas **não é inferência real**. No CI, roda no job `ia-groq` quando o secret `GROQ_API_KEY` existe e grava `relatorio-ia-groq.json`. Esse teste **não foi executado com o modelo real** (rede do ambiente de desenvolvimento bloqueia api.groq.com e o Actions não inicia). Houve **uma inferência real** em produção, feita fora da suíte de testes em 13/11/2026 e registrada em [INTELIGENCIA_ARTIFICIAL.md](INTELIGENCIA_ARTIFICIAL.md), incluindo um erro de número no resumo.

## Como executar
```bash
docker compose up -d banco            # PostGIS em localhost:5432
cd backend && dotnet test AlertaEmAcao.sln
cd apps/web && npm ci && npx vitest run
./scripts/executar-local.sh           # app em http://localhost:8080
cd testes-e2e && npm ci && npx playwright install chromium && npx playwright test
```
Ou `./scripts/testar-tudo.sh`. A CI (`.github/workflows/ci.yml`) roda backend e frontend com um serviço PostGIS.

## Situação do CI (GitHub Actions)
Até 13/11/2026, 14h37, os jobs não iniciavam (*"account is locked due to a billing issue"*) porque o orçamento de Actions da conta estava em US$ 0 com bloqueio de uso. Ajustado o orçamento:
- 1ª execução: frontend aprovado; backend com **1 falha real**. O teste de voz esperava 400 para texto grande, mas sem `espeak-ng` no CI a API respondia 503 antes de validar. Corrigido: a entrada é validada primeiro.
- **[Execução 43475377379](https://github.com/edufrannca/Acaoalerta/actions/runs/43475377379): verde.** Backend (209 testes), frontend (tsc, Vitest, build) e `ia-groq` aprovados.
- `ia-groq` com o modelo real (`openai/gpt-oss-20b`): 10 casos fictícios, todos com esquema válido; 8 itens sem evidência descartados pelo validador; uma chamada com HTTP 429 repetida após 4 s. O relatório fica no artefato `relatorio-ia-groq`.

## Acessibilidade e desempenho
- **axe-core (WCAG 2.x A/AA)** nos testes ponta a ponta: sem violações nas telas cobertas.
- **Lighthouse 12** (celular simulado, app local, 13/11/2026). Acessibilidade: Início 96 → **100** após corrigir o contraste do botão "Estou seguro" (4,32:1 → 4,97:1); Perfil, Socorro, Alertas e Abrigos **100**. Boas práticas 96–100.
- **Desempenho medido baixo** (35–56 no Lighthouse com rede e CPU limitadas; ~1,8 MB transferidos no Início, a maior parte do mapa MapLibre). As informações de segurança (socorro, 193/199) não dependem do mapa, mas o carregamento inicial em 3G é um ponto a melhorar.
- **Não testado** com VoiceOver/TalkBack em aparelhos físicos.

## O que é coberto
- **Domínio:** identificação dos 14 municípios por ponto (incluindo bordas e pontos fora da RMR), regras de urgência ("idade sozinha não é ameaça"), roteamento, retentativa, política de localização, parsers APAC/INMET.
- **Integração:** fluxo completo de socorro até o protocolo da central; idempotência; isolamento entre dispositivos e entre centrais; os 7 modos de falha; retorno de estado; alertas proativos por polígono e por município; apoio comunitário; abrigos; cifragem em repouso; exportação LGPD; gateway Nearby simulado; cabeçalhos de segurança; validação de entrada.
- **Adaptadores:** parsers testados com amostras no formato real (campos verificados nas respostas ao vivo da APAC, INMET, COP e CKAN Recife), sem rede.
- **E2E:** SOS sem cadastro → central simulada recebe; **offline real** (`setOffline`) → fila → sincroniza com 1 registro; alerta → "Estou seguro"; painel do operador; falha e reenvio de central; "Quero ajudar" → organização confirma.

## Rede comunitária e retransmissão (novos)
- Pedido só aparece para a comunidade com autorização do dono; revogação remove; outro aparelho não autoriza (403).
- Resposta pública sem nome, telefone, relato, referência, coordenadas nem id do aparelho; faixas para crianças/idosos/mobilidade.
- Área pública contém o ponto registrado (5.000 pontos aleatórios); busca centrada no GPS de quem consulta.
- Conta fictícia fora da lista pública; aparece só na lista demonstrativa.
- Oferta: exige ciência de segurança; não abre para pedido não compartilhado nem para o próprio solicitante; aceite e encaminhamento só pela central do pedido; voluntário só vê a orientação depois do encaminhamento.
- QR: integridade adulterada → 422; posição do emissor preservada; posição do ajudante separada; QR sem coordenadas → "localização não informada" mesmo com GPS do ajudante; segundo leitor → `duplicado=true`; emissor sincroniza depois com o mesmo id (200) e continua dono; o ajudante não acessa o pedido.
- Gateway v2 com `posicaoReceptor` e mensagem sem coordenadas → ocorrência sem posição; GPS do gateway guardado à parte.
- Paridade do texto canônico do QR entre PWA (TypeScript) e servidor (C#) com o mesmo vetor.

## Não testado
- Nearby em aparelhos físicos (código não compilado).
- Web Push entregue em aparelho real (o envio está implementado; não houve VAPID nem aparelho na execução).
- Chamadas às APIs públicas ao vivo dentro dos testes (usamos respostas gravadas; a coleta ao vivo foi verificada manualmente pelo navegador).
- Carga e desempenho; acessibilidade com leitor de tela.

## Validação no ambiente publicado (13/11/2026, 02:30)
| Verificação | Resultado |
|---|---|
| `/health` | ok, banco ok, PostGIS 3.6.2 |
| Gateway v2 → API → centrais SIMULADAS | nível 5 em 2,6 s; protocolos DC-RECIFE e CBM-SIM; reenvio por outro gateway `duplicado=true` |
| IA Groq real (`openai/gpt-oss-20b`) | Concluida em 839 ms, esquema válido; resumo com 1 imprecisão (ver INTELIGENCIA_ARTIFICIAL) |
| Alerta demonstrativo (área Ibura) | 1 notificação; pergunta "Há um aviso para sua região. Você e sua família estão bem?" |
| `/instalar`, manifest | 200; 4 ícones (2 maskable) |

## Validação no ambiente publicado (13/11/2026, 04:15, commit b8a1682)
Feita pelo navegador contra https://alerta-em-acao.onrender.com, só com pedidos marcados como demonstração.

| Verificação | Resultado |
|---|---|
| Pedido com resumo comunitário autorizado (conta fictícia) | 201, nível 4; fora da lista pública; na lista demonstrativa com tipo, 4 pessoas, faixas, necessidades, área de 600 m (antes da redução para 400 m) |
| Vazamentos na resposta comunitária | nome, telefone, coordenadas e id do aparelho **ausentes** |
| Oferta sem ciência de segurança | 400 |
| Oferta → aceite → orientação → concluída | AguardandoAvaliacao → AceitaPelaEquipe → Encaminhada → Concluida; voluntário vê só a orientação, sem coordenadas |
| Central de outro município decide a oferta | 403 |
| QR adulterado | 422 |
| QR lido por aparelho próximo | 200 em 405 ms, nível 4, "registrada pelo aparelho da família" |
| Mesmo QR por um segundo aparelho | `duplicado=true` |
| Emissor sincroniza depois pelo próprio aparelho | 200, dono reconhecido, canal ProximidadeQr, nível 5 (central simulada) |
| Aparelho que ajudou tenta abrir o pedido | 403 |
| Operador | posição do emissor ±18 m; posição do ajudante separada; "Leituras compatíveis: 31 m, margem 48 m" |

Isto **não** é teste físico: as chamadas saíram de um navegador, simulando os dois aparelhos.

## Monitoramento familiar (13/11/2026)
Cobertos por `ResidenciasTestes.cs` (7 cenários de integração) e pelo E2E "familiar vinculado é avisado…":
CEP válido, inexistente, inválido, sem rua (sem coordenadas → mapa), fora da RMR; busca textual; endereço ambíguo; ponto no mapa com município oficial; fonte fora do ar (503); cadastro por GPS e por mapa; edição; moradores (validação de totais); dados cifrados no banco; limite de 5; remoção; acesso de outra conta negado; contexto para endereço distante;
convite (formato, uso único, código errado com mensagem genérica, próprio código recusado, expirado em 24 h), aprovação só pelo morador, município oculto antes da aprovação, revogação; duas famílias no mesmo endereço sem vínculo;
SOS do morador → aviso só ao familiar aprovado; estranho e vizinho não recebem; reenvio sem duplicar; visão do familiar com permissões (posição e moradores só quando liberados); complemento e confirmação da central simulada geram avisos; marcação de lida; revogação corta o acesso; SOS anônimo perto da casa não é associado; falha de push registrada sem perder o aviso; sem consentimento D nada é associado; associação explícita;
cadastro sem consentimentos (nada registrado), consentimentos independentes, histórico com versão e origem, preferência C só para pedidos novos, revogação de C retira só os pedidos dependentes; cadastro com consentimentos marcados registra origem "cadastro".
Validação em aparelhos físicos: **pendente** (roteiro em `MONITORAMENTO_FAMILIAR.md`).
