# Monitoramento familiar: locais, moradores, vínculos, consentimentos e notificações

## Locais monitorados (até 5 por pessoa)
Três formas de informar o local, mais o GPS atual:

| Método | Fonte | Coordenadas |
|---|---|---|
| CEP | ViaCEP (endereço) | ViaCEP **não** traz coordenadas. A posição vem do OpenStreetMap/Nominatim só quando há correspondência na RMR; senão a pessoa escolhe no mapa. Resultado só da rua exige confirmar o ponto. |
| Endereço (rua, número, bairro, município, PE) | OpenStreetMap/Nominatim | Consulta explícita (botão), nunca a cada tecla. Vários resultados → a pessoa escolhe. |
| No mapa | MapLibre + limites municipais oficiais (APAC) | Toque ou arraste o marcador; setas do teclado ajustam. Funciona sem CEP nem geocodificação. |
| Onde estou | GPS do aparelho | Com precisão em metros. |

Antes de salvar: "Confirmo que o marcador está no lugar certo". O município é identificado pelos limites oficiais (sem serviço externo). Fora dos 14 municípios da RMR, o local é recusado.

Cada local guarda: apelido, tipo (residência, trabalho, outro), "eu moro aqui" (uma por pessoa), CEP e endereço, latitude/longitude, origem e descrição da precisão, dono, datas de cadastro e atualização, ativo/inativo. Endereço e moradores ficam **cifrados** (AES-GCM). Editar, substituir e remover estão disponíveis; remover a residência onde mora encerra vínculos e convites dela.

**Nominatim:** uma requisição por segundo para toda a aplicação (trava compartilhada, inclusive com a consulta reversa de localidade), User-Agent identificado, cache de 1 dia (ViaCEP: 7 dias), atribuição na tela, limite de 20 consultas/min por IP e só para quem tem conta.

## Moradores da residência
Só números: moradores, crianças, idosos, mobilidade reduzida, dependem de assistência; contato de referência e observações privadas opcionais. Nenhum grupo pode passar do total. Nunca aparecem no mapa público nem para a comunidade. Familiares só veem se o morador marcar "Ver número de pessoas e moradores". O módulo **Minha família** (integrantes com idade e necessidades) continua separado e é usado só para enriquecer o pedido do próprio usuário.

## Vínculo familiar (convite + aprovação)
1. O morador marca "Eu moro aqui" na residência e toca **Gerar código de convite** (ex.: `K7Q2M-9XHTA`; 10 caracteres sem 0/O/1/I/L).
2. O código vale 24 h, é de uso único e o servidor guarda só o hash (SHA-256). No máximo 5 convites em aberto e 10 familiares por residência; 10 tentativas por IP a cada 10 min.
3. O familiar (com conta) digita o código em Perfil → **Acompanhar uma residência** e dá um nome ("Casa da minha mãe"). Código inexistente, expirado ou já usado recebe a **mesma** mensagem, que não revela contas nem convites.
4. O vínculo fica **aguardando aprovação**. Até aprovar, o familiar não vê nem o município.
5. O morador aprova ou recusa e define as permissões: receber avisos (padrão sim), ver necessidades (sim), ver moradores/pessoas (não), ver posição exata do pedido (não).
6. Morador ou familiar encerram o vínculo a qualquer momento; o acesso cai na hora.

**Nunca** há vínculo por mesmo endereço, CEP ou coordenadas (teste: duas famílias no mesmo prédio). O vínculo é sempre com a residência onde **o morador** declara morar; ninguém concede acesso a dados de outra pessoa.

## Notificação de familiares
Pedido de socorro de pessoa cadastrada com o consentimento D e residência "moro aqui":
1. O pedido é salvo no aparelho, transmitido e gravado no servidor exatamente como antes (um toque, idempotente, GPS real).
2. O servidor associa o pedido à residência **declarada** (nunca por proximidade) e registra no histórico. O solicitante vê "Pedido associado à sua residência" e pode desfazer no próprio pedido.
3. Para cada familiar com vínculo ativo e "receber avisos": cria uma `NotificacaoFamiliar` guardada no servidor, publica no SignalR (grupo `usuario:{id}`) e tenta Web Push se o familiar ativou e o servidor tem VAPID. Cada tentativa fica registrada (`aplicativo`, `tempo_real`, `web_push` com `aceito_pelo_servico_push`/`falha`/`nao_configurado`/`sem_assinatura`).
4. Atualizações posteriores geram novos avisos: informações complementares, situação atualizada, confirmação por central **simulada**, atendimento simulado, encerramento.
5. Sem duplicar: chave única por familiar e evento (`{pedido}:pedido`, `{pedido}:estado:confirmada`…), garantida também por índice único no banco.
6. O familiar abre `/familia/pedidos/{id}`: residência, município, horário, estado e nível comprovado, situação, histórico de eventos e, conforme permissões, necessidades, número de pessoas/moradores e posição exata. Nunca: relato livre, dados de saúde, nome de outros moradores.

Familiar offline: o aviso fica guardado e aparece em Perfil → **Avisos de familiares** quando ele abrir o app com conexão. Falha de push não apaga o registro. Revogar o consentimento D ou o vínculo bloqueia a visão imediatamente.

**SOS anônimo** continua igual e **nunca** é associado a residência. Quem pediu socorro sem conta e depois entrou na conta pode associar explicitamente o pedido (botão "Avisar meus familiares vinculados" na tela do pedido), desde que tenha o consentimento D e a residência cadastrada.

## Consentimentos (LGPD)
| | Finalidade | Efeito |
|---|---|---|
| A | Alertas preventivos dos locais monitorados | Avisos e perguntas proativas; habilita push |
| B | Compartilhamento operacional durante SOS ativo (equipes simuladas nesta versão) | Registro da autorização para envio contínuo de localização e dados de atendimento |
| C | Compartilhamento comunitário de novos pedidos | Pedidos novos já saem com resumo mínimo (necessidades + região) sem perguntar de novo |
| D | Notificação de familiares vinculados | Associação do pedido à residência e avisos aos vínculos aprovados |

Todos opcionais e desmarcados; a conta é criada sem nenhum. Cada concessão ou revogação é uma linha nova (`registros_consentimento`: finalidade, concedido, versão do texto `2026-11-13.1`, origem `cadastro`/`perfil`, data). Usuários antigos não ganham registro retroativo; só o campo de alertas que eles já haviam escolhido continua valendo como A. A permissão de GPS do celular é independente. Pedir socorro nunca depende de A–D; o tratamento estritamente necessário à proteção da vida tem base legal própria (art. 7º, VII; art. 11, II, "e"), registrada separadamente.

C aplicado: pedidos **novos** recebem o resumo com origem "preferência"; pedidos antigos não são publicados. Revogar C retira da comunidade os pedidos ativos com origem "preferência"; o que foi autorizado no próprio pedido continua até a pessoa desligar ali.

## Comunidade (mínimo)
Cada cartão público mostra só: região aproximada (município + círculo ≥ 400 m), necessidades autorizadas, verificação, estado mínimo e **Oferecer ajuda** (Início, Mapa, Quero ajudar; também sem necessidades e mesmo que seja o único pedido). Filtrado no backend: sem tipo detalhado, número de pessoas, crianças, idosos, horário, relato, endereço, GPS ou moradores.

## Roteiro de validação física (2 aparelhos)
Registrar modelo, sistema, navegador/PWA instalada, horários e capturas.

**Aparelho A — familiar**
1. Abrir https://alerta-em-acao.onrender.com → Perfil → Criar uma conta (pode deixar autorizações desmarcadas).
2. Adicionar local → Pelo CEP (ex.: CEP real da casa) → conferir o marcador → "Casa da minha mãe", tipo Residência → Salvar. Abrir o local: conferir chuva/estação, avisos e orientações.
3. (Opcional) Autorizações → Alertas preventivos → Preferências → Receber push → Ativar notificações neste aparelho (no iPhone, só com o app instalado na Tela de Início, iOS 16.4+).

**Aparelho B — morador**
4. Criar conta marcando **Notificação de familiares vinculados**.
5. Adicionar local → No mapa (ou CEP) → "Minha casa", Residência, **Eu moro aqui**, moradores (ex.: 3, 1 criança) → Salvar → **Gerar código de convite**.

**A**: Perfil → Acompanhar uma residência → digitar o código e "Casa da minha mãe" → aguardando aprovação.
**B**: abrir "Minha casa" → Aprovar.
**B**: Início → Preciso de socorro (pedido FICTÍCIO; não ligar para serviços reais) → marcar necessidades → conferir "Pedido recebido pelo servidor" e "Pedido associado à sua residência".
**A**: receber o push (se ativado) ou abrir Perfil → Avisos de familiares → abrir o pedido → conferir estado, nível, necessidades e "Posição exata: o morador não compartilhou". Em B, ligar "Ver a posição exata do pedido" e conferir que aparece em A.
**B**: remover o acesso → em A o pedido deixa de abrir.
**SOS anônimo**: em um terceiro navegador sem conta, perto da mesma casa, pedir socorro → A **não** recebe aviso.

Se Web Push não estiver configurado no servidor (sem chaves VAPID) ou o iPhone não suportar, a demonstração usa o histórico interno (Avisos de familiares), e isso deve ser dito.

Estado: **validação física pendente** (não executada em aparelhos reais nesta entrega).
