# Requisitos funcionais

Estado: **I** = implementado e testado · **P** = implementado sem teste automatizado específico · **S** = simulado (demonstração) · **F** = futuro.

| # | Requisito | Estado | Onde |
|---|---|---|---|
| RF01 | Pedir socorro com um toque, sem cadastro e sem formulário obrigatório | I | `Inicio.tsx`, `POST /api/ocorrencias` |
| RF02 | Id gerado no aparelho, horário e GPS disponível; persistência local antes da rede | I | `lib/socorro.ts`, `filaOffline.ts` |
| RF03 | Complemento opcional: idades, pessoas, feridos, crianças, idosos, mobilidade, equipamento médico, ilhamento, água subindo, correnteza, deslizamento, desabamento, barreira, acesso bloqueado, relato e referência | I | `ComplementoOcorrencia` |
| RF04 | Atualização de localização limitada (15 s / 15 m / batimento de 2 min) | I | `PoliticaAtualizacaoLocalizacao` |
| RF05 | Ligação para 193/199/192/190 por toque explícito, sem prometer o acompanhamento da chamada | I | `Contatos.tsx` |
| RF06 | "Estou seguro" como autodeclaração, com opção de ajudar | I | `POST /api/situacoes` |
| RF07 | Cadastro opcional, sem CPF; família e contatos (cifrados) | I | `/api/auth`, `/api/eu/familia` |
| RF08 | Locais monitorados (até 5) com município identificado | I | `/api/eu/locais` |
| RF09 | Mapa com os limites dos 14 municípios, chuva, áreas de risco, abrigos, relatos agregados e avisos, com filtros e fonte/horário | I/P | `Mapa.tsx` |
| RF10 | Estados informativos (suscetibilidade, aviso oficial/demonstrativo, relatada, verificada, desatualizada, desconhecida) sem afirmar "área segura" | I | `AvaliadorContextoTerritorial` |
| RF11 | Avisos oficiais do INMET filtrados para a RMR, com vigência | I | `ParserAvisosInmet`, `TrabalhoAlertas` |
| RF12 | Alerta proativo: cruzamento espacial, consentimento, sem duplicar, pergunta "Você e sua família estão bem?" e 3 respostas | I | `ServicoAlertas`, `PerguntaProativa` |
| RF13 | Web Push quando configurado; caixa de notificações no app sempre | P | `EnviadorWebPush`, `sw-push.js` |
| RF14 | Regras determinísticas com evidências; idade nunca é critério isolado | I | `MotorRegrasUrgencia` |
| RF15 | IA com esquema validado, descarte de itens inventados e tolerância a falha | I | `ValidadorSaidaIA`, `ServicoAnalise` |
| RF16 | Roteamento por município e tipo de atendimento a partir de regras e instituições cadastradas | I | `RoteadorEncaminhamento` |
| RF17 | Centrais simuladas com autenticação, esquema, persistência, protocolo, idempotência e falhas controladas | I/S | `CentraisSimuladas.cs` |
| RF18 | Confirmação individual por central; retentativa; reenvio manual | I | `ServicoDespacho` |
| RF19 | Painel em tempo próximo do real, com análise, encaminhamentos, histórico e verificação | I | `Painel.tsx`, SignalR |
| RF20 | Retorno de estado da central para a plataforma e para o cidadão | I | `/api/integracoes/retorno-central` |
| RF21 | Abrigos públicos (COP Recife) e fictícios identificados; "Disponibilidade não verificada" | I | `/api/abrigos` |
| RF22 | Apoio comunitário: oferta → participação → confirmação da organização simulada | I | `/api/apoio` |
| RF23 | Gateway simulado / Nearby: ingestão de pacote compacto | I (backend) / F (aparelhos) | `/api/gateway/pacotes` |
| RF24 | Modo demonstração: contas fictícias, avisos demonstrativos, modos de falha das centrais | I | `/demo`, `/api/demo` |
| RF25 | Auditoria de acesso a dados detalhados | I | `RegistroAuditoria` |
| RF26 | Integração institucional homologada | F | `CanalIntegracao.HttpInstitucionalHomologado` (reservado) |
