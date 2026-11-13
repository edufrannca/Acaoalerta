# Alerta em Ação — Hackathon APONTI Academy 2026

> **Evento:** Hackathon APONTI Academy — Devs Criam com IA 2026 (REC'n'Play 2026)
> **Desafio:** Antena na Lama
> **Caminho escolhido:** Alertar e proteger · atuação complementar em prevenção e mobilização comunitária
> **Região atendida:** os 14 municípios da Região Metropolitana do Recife (RMR)

**Aplicação publicada:** **https://alerta-em-acao.onrender.com**

> **Ambiente de demonstração.** As centrais da Defesa Civil, Bombeiros, SAMU, Polícia Militar e as organizações de apoio são **simuladas**. Nenhum órgão real recebe os pedidos feitos aqui. Em emergência real, ligue **193** (Bombeiros) ou **199** (Defesa Civil).

## Sobre o projeto

Quando chove forte na RMR, a água sobe rápido, a internet fica fraca e muita gente não sabe se está em risco nem como pedir ajuda. Os avisos oficiais existem, mas chegam em linguagem técnica e não dizem o que fazer, nem avisam quem mora longe de quem precisa de socorro.

O **Alerta em Ação** é um aplicativo que funciona no navegador do celular e pode ser instalado na tela inicial. Ele é para qualquer morador da RMR, principalmente quem vive em áreas de alagamento e encosta, e para as famílias que querem cuidar de quem mora longe. Com **um toque** a pessoa pede socorro com a localização. O pedido fica guardado no celular mesmo sem internet e é enviado quando a conexão volta, ou passa por um celular próximo usando um código QR. Quem a pessoa autorizou, como um filho que acompanha a casa da mãe, é avisado na hora.

**O diferencial** em relação a aplicativos que só mostram avisos do tempo: o Alerta em Ação liga o aviso à ação. Ele cruza os avisos oficiais e a chuva medida com os locais que a pessoa escolheu, pergunta "Você e sua família estão bem?", encaminha o pedido para a equipe responsável pelo município e mostra com honestidade em que etapa o pedido está, sem prometer que o socorro já está a caminho.

**Benefícios**
- Pedido de socorro em um toque, que funciona com internet fraca ou sem internet.
- Família avisada na hora, com alarme e tela de emergência quando o app está aberto.
- Informação de chuva e alagamento em linguagem simples, com o que fazer.
- Comunidade pode oferecer ajuda de forma coordenada, sem expor a localização exata de ninguém.
- Funciona para pessoas com deficiência visual, auditiva ou com pouca leitura: leitura por voz, alto contraste, cores para daltonismo e texto grande.

## O que funciona hoje

| Recurso | Situação |
|---|---|
| Pedido de socorro com um toque, salvo no celular e enviado quando houver internet | Funcionando, com testes automáticos (incluindo rede desligada) |
| Mensagens que separam "salvo neste celular", "enviando", "registrado" e "a equipe de teste recebeu" | Funcionando, com testes automáticos |
| Encaminhamento por município e tipo de risco às centrais (com confirmação, protocolo e nova tentativa) | Funcionando com **centrais simuladas** |
| Regras fixas de urgência com justificativa para cada sinal; a IA complementa, não decide | Funcionando, com testes automáticos |
| Avisos oficiais (INMET) cruzados com os locais monitorados → "Você e sua família estão bem?" | Funcionando com dados reais |
| Indicador de **atenção** a alagamentos (chuva medida pela APAC, avisos do INMET, ocorrências verificadas) | Funcionando. É um **indicador por regras, não uma previsão validada**: não estima quando vai alagar |
| Locais monitorados (CEP, endereço ou mapa), moradores e **animais de estimação** | Funcionando, com testes automáticos |
| Família: convite por código, aprovação do morador, consentimentos (LGPD) e aviso de pedido de socorro | Funcionando, com testes automáticos |
| Alarme de emergência (tela cheia, sirene e vibração) com o app aberto; notificação push com o app fechado | Tela testada automaticamente. Som no modo silencioso e push **ainda não testados em celulares físicos** |
| Pedidos compartilhados com a comunidade e botão "Oferecer ajuda" (área aproximada) | Funcionando com organizações simuladas |
| Retransmissão sem internet por código QR para um celular próximo | Funcionando, com testes automáticos |
| Nearby (Android/iOS, app nativo) | Prova de conceito escrita, **não compilada nem testada** |

Limites detalhados em [docs/LIMITACOES.md](docs/LIMITACOES.md).

## Como acessar e instalar

1. Abra **https://alerta-em-acao.onrender.com** no celular. O plano gratuito do Render pode levar até cerca de 1 minuto para acordar na primeira visita.
2. Para instalar: Android/Chrome, menu ⋮ → **Instalar app**; iPhone/Safari, **Compartilhar → Adicionar à Tela de Início**. Passo a passo em `/instalar`.
3. Crie uma conta em **Perfil** para monitorar locais e convidar familiares. O botão de socorro funciona sem conta.

## Equipe

> ⚠️ **Pendente de preenchimento pela equipe.** Os nomes abaixo vêm do histórico de commits deste repositório. Responsabilidades, trilhas e o(a) validador(a) não estão registrados aqui e devem ser confirmados pela equipe.

| Integrante | Responsabilidades / trilha |
|---|---|
| Luiz Eduardo de França Rodrigues | Desenvolvedor FullStack |
| Leandra Michely | Desenvolvedor Frontend |
| Fernando Menezes | Desenvolvedor Frontend |
| Jader Santos | Desenvolvedor Backend |
| **Validador(a) - Maria Eduarda -  Movimento Pró-Criança** | 

## Tecnologias

| Camada | Tecnologias |
|---|---|
| Front-end (PWA) | React 19, TypeScript, Vite, Tailwind CSS 4, MapLibre GL + OpenStreetMap, IndexedDB (fila offline), service worker (Workbox), Web Push |
| Back-end | ASP.NET Core 10 (C#), monólito modular, SignalR (tempo real), EF Core |
| Banco de dados | PostgreSQL 16 + PostGIS |
| IA | Groq, modelo `openai/gpt-oss-20b`, com saída validada por esquema |
| Dados públicos | APAC (chuva), INMET (avisos), Prefeitura do Recife (áreas de risco e abrigos), IBGE (limites municipais) |
| Deploy | Docker + Render (serviço web e PostgreSQL, plano gratuito) |
| Testes | xUnit, WebApplicationFactory, Vitest, Testing Library, Playwright, axe-core |

## Uso ético e responsável da inteligência artificial

**1. Ferramentas usadas no desenvolvimento.** Usamos o **Claude** (Anthropic, via Claude Code) para gerar e revisar código, testes e documentação; os commits feitos com ele aparecem como coautoria no histórico. *Outras ferramentas usadas pela equipe: a preencher.*

**2. IA dentro do produto (Groq).** Quando chega um pedido de socorro, o modelo `openai/gpt-oss-20b` lê o texto livre e devolve um resumo, as pessoas citadas e os perigos, cada item com o trecho do texto que o justifica. Itens sem evidência literal são descartados. Se a IA falhar ou demorar, o pedido segue normalmente pelas regras.

**3. Regras fixas primeiro.** A urgência é sinalizada por regras determinísticas, como "água subindo + ilhados", com justificativa visível. A IA **não decide** a prioridade, **não substitui** o operador e **não pode** confirmar atendimento. No indicador de alagamento, a IA só redige a explicação, e o texto é descartado se trouxer número que não está nos dados, percentual ou "vai alagar".

**4. Validação crítica com um erro real.** Numa inferência real com a Groq, em 13/11/2026, com um pedido fictício de 4 pessoas (3, 34 e 78 anos), os campos estruturados vieram corretos. Mas o **resumo livre disse "outras duas pessoas (34 anos)"** quando só uma pessoa tinha 34 anos. Registramos o erro em [docs/INTELIGENCIA_ARTIFICIAL.md](docs/INTELIGENCIA_ARTIFICIAL.md). O painel mostra os dados validados ao lado do resumo e a decisão é sempre humana. Validar os números citados no resumo continua pendente.

**5. Correções durante o desenvolvimento** (registradas nos commits):
- Mensagens técnicas ao cidadão ("recebido pelo servidor") foram reescritas em linguagem simples ("Seu pedido de ajuda foi registrado!"). O sistema não diz que o pedido foi registrado antes da confirmação, nem que o socorro está a caminho.
- O mapa travava a página inteira em aparelhos sem WebGL, incluindo o botão de socorro. Corrigido: o mapa vira uma descrição em texto.
- Quem entrava na conta com o app aberto não recebia os avisos pessoais até recarregar a página. Corrigido.
- O indicador de alagamento não estima horário, por falta de um modelo validado com histórico.

**6. Revisão humana.** O texto e a acessibilidade foram ajustados a partir de pedidos e revisões da equipe (linguagem simples, menos texto, contraste para daltonismo). *Validação com usuários e com o(a) jovem validador(a): a preencher pela equipe.*

**7. Avaliação com o modelo real no CI.** Em 13/11/2026 o job `ia-groq` rodou 10 casos fictícios com `openai/gpt-oss-20b`: todas as saídas tinham esquema válido e o validador **descartou 8 itens sem evidência** no texto (5 no caso "criança e pessoa idosa", 2 em "idade sozinha não é ameaça", 1 em "localização desconhecida"). Uma chamada recebeu limite de requisições (HTTP 429) e foi repetida. Respostas entre 0,5 e 2,5 s.

**8. Limitações.** O resumo da IA pode errar números. A IA não foi avaliada com pedidos reais.

## Acessibilidade e internet fraca

Conferido com o [checklist do kit prático](https://github.com/alisonmelo/HACKATHON-PE-RECPLAY26/blob/main/guias/01-checklist-acessibilidade.md):

| Item | Como está |
|---|---|
| Linguagem simples | Mensagens do socorro e das telas principais reescritas sem termos técnicos |
| Contraste e cor | Verificado com axe-core (WCAG 2.x A/AA) nos testes automáticos; modos **alto contraste** e **cores para daltonismo**; níveis de risco mostrados por texto, ícone e barras, não só por cor |
| Rótulos, teclado e foco | Campos com `<label>`; botões nativos; foco visível; foco vai para a ação principal nos alertas |
| Toque e leitura no celular | Botões principais com pelo menos 44 px, texto ampliável até 150%; testado em tela de 390 px sem rolagem lateral |
| Leitor de tela e sem áudio | `aria-live` só quando o nível muda; todo alerta aparece por escrito; "Ouvir informações" por voz |
| Internet fraca | Pedido salvo no celular (IndexedDB) e enviado depois; telefones 193/199 e 192/190 com `tel:`; dados em cache com aviso de "cópia salva"; falha de fonte externa mostra mensagem, não tela vazia |

Resultado do Lighthouse: ver [docs/TESTES.md](docs/TESTES.md#acessibilidade-e-desempenho). **Não testado** com VoiceOver/TalkBack em aparelhos físicos.

## Testes automatizados

Execução local em 13/11/2026:

| Suíte | Ferramenta | Testes | Resultado |
|---|---|---|---|
| Back-end: unitários | xUnit | 114 | aprovados |
| Back-end: integração (API + PostgreSQL/PostGIS reais) | xUnit + WebApplicationFactory | 64 | aprovados |
| Back-end: arquitetura | xUnit | 7 | aprovados |
| Back-end: avaliação da IA (com respostas controladas) | xUnit | 24 | aprovados* |
| Front-end: componentes | Vitest + Testing Library | 26 | aprovados |
| Ponta a ponta + acessibilidade (axe) | Playwright + axe-core | 25 | aprovados |

\* Localmente, o teste com o modelo real é ignorado sem a chave da API. **No CI ele rodou de verdade** (ver abaixo).

**CI (GitHub Actions):** o workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) roda back-end (com PostGIS), front-end e a avaliação com o modelo real. **Execução verde em 13/11/2026:** [run 43475377379](https://github.com/edufrannca/Acaoalerta/actions/runs/43475377379), com back-end, front-end e IA aprovados. As execuções anteriores não rodaram porque o orçamento de Actions da conta estava em US$ 0 com bloqueio; corrigido o orçamento, apareceu 1 falha real (o teste de voz dependia do sintetizador `espeak-ng`, que não existe no CI), também corrigida. Detalhes em [docs/TESTES.md](docs/TESTES.md).

## Como executar localmente

Pré-requisitos: **.NET 10 SDK**, **Node.js 22** e **Docker** (ou PostgreSQL 16 com PostGIS).

```bash
cp .env.example .env && node scripts/gerar-chaves.mjs >> .env   # segredos só de desenvolvimento
docker compose up -d banco                                       # PostgreSQL + PostGIS
./scripts/executar-local.sh                                      # compila a PWA e sobe a API em http://localhost:8080
```

Ou tudo em contêiner: `docker compose up --build`.

Testes:

```bash
./scripts/testar-tudo.sh                       # tudo
cd backend && dotnet test AlertaEmAcao.sln     # back-end (precisa do banco)
cd apps/web && npm ci && npx vitest run        # front-end
cd testes-e2e && npm ci && npx playwright install chromium && npx playwright test   # com o app rodando
```

A chave da Groq (`IA__ChaveApi`) é opcional; sem ela, o sistema funciona só com as regras.

## Limitações e o que é simulado

- **Centrais de atendimento e organizações de apoio são simuladas.** Nenhum órgão real é acionado.
- O indicador de alagamento é **observacional e baseado em regras**, sem previsão validada nem dados de maré, relevo ou drenagem.
- Um app web não toca alarme com o celular no **silencioso** se o app estiver fechado. Isso exige app nativo.
- Nearby nativo (Android/iOS) não foi compilado nem testado; testes em aparelhos físicos estão pendentes ([matriz](docs/MATRIZ_TESTES_DISPOSITIVOS.md)).
- Infraestrutura gratuita não é adequada para operação real de emergência.

Mais em [docs/LIMITACOES.md](docs/LIMITACOES.md).

## Segurança e privacidade

Nenhum arquivo `.env` real ou chave está no repositório; só o modelo `.env.example`. Chaves (Groq, VAPID, banco) ficam em variáveis de ambiente do Render. As contas de demonstração usam dados fictícios. Endereços, moradores, telefones e detalhes dos pedidos ficam cifrados no banco. Ver [docs/SEGURANCA_LGPD.md](docs/SEGURANCA_LGPD.md).

## Documentação complementar

[Visão do produto](docs/VISAO_PRODUTO.md) · [Arquitetura](docs/ARQUITETURA.md) · [IA](docs/INTELIGENCIA_ARTIFICIAL.md) · [Inteligência territorial e alagamentos](docs/INTELIGENCIA_TERRITORIAL.md) · [Monitoramento familiar](docs/MONITORAMENTO_FAMILIAR.md) · [Rede comunitária](docs/REDE_COMUNITARIA.md) · [Offline e QR](docs/RETRANSMISSAO_OFFLINE.md) · [Centrais simuladas](docs/CENTRAIS_SIMULADAS.md) · [Segurança e LGPD](docs/SEGURANCA_LGPD.md) · [Testes](docs/TESTES.md) · [Deploy](docs/DEPLOY.md) · [Limitações](docs/LIMITACOES.md) · [Roteiro da demonstração](docs/ROTEIRO_DEMONSTRACAO.md) · [Pitch](docs/PITCH.md)
