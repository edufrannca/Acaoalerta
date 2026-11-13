# Segurança e LGPD

## Bases legais (proposta, a validar com o encarregado)
- **Proteção da vida e da incolumidade física** (art. 7º, VII; art. 11, II, e): pedido de socorro e encaminhamento.
- **Consentimento** (art. 7º, I): alertas proativos, locais monitorados, compartilhamento de contato, voluntariado.

## Níveis de acesso
| Perfil | Vê |
|---|---|
| Cidadão / dispositivo | Só os próprios pedidos (por usuário ou por `X-Dispositivo-Id`) |
| Voluntário | Necessidades publicadas e mapa comunitário agregado; nunca a localização exata de vítimas |
| Organização | Ofertas e necessidades atribuídas a ela |
| Operador de central | Só o que foi encaminhado à sua central (claim `central`); localização exata; contato só se autorizado |
| Administrador | Painel geral com localização **aproximada** e modo de demonstração |

Testes de integração verificam: painel exige papel; operador vê somente a própria central; administrador vê localização aproximada; mapa comunitário sem dados pessoais nem coordenadas precisas; outro dispositivo não lê o pedido alheio.

## Medidas técnicas
- **Cifrados em repouso (AES-256-GCM, `Dados__ChaveCifragem`):** telefone do usuário, integrantes e contatos do grupo familiar, complemento da ocorrência (relato e pessoas envolvidas), trilha de localização, resultado da IA e contato autorizado de ofertas. Há teste que confirma o prefixo `v1:` no banco.
- Senhas com PBKDF2-SHA256 de 210 mil iterações; tokens bearer com expiração.
- Limite de requisições em autenticação e socorro.
- Cabeçalhos: CSP restritiva, `nosniff`, `X-Frame-Options: DENY`, `frame-ancestors 'none'`, `Referrer-Policy`, `Permissions-Policy`. HSTS fica com o provedor de TLS (Render).
- Localização reduzida (~1 km) em mapas públicos e agregados.
- Histórico por ocorrência (ator, ação, horário) e tabela `auditoria`.
- A entrada enviada para a IA é mínima: sem nome, telefone ou id.
- Segredos só em variáveis de ambiente; `.env` no `.gitignore`; chaves de centrais derivadas por HMAC.

## Direitos do titular
- **Acesso/portabilidade:** `GET /api/eu/exportar` devolve um JSON com perfil, família, locais, pedidos e notificações (testado).
- **Correção:** edição de perfil, família e locais; remoção de locais monitorados.
- **Exclusão de conta:** **não implementada no MVP** (pendente). Pedidos já encaminhados ficariam com a central como registro de atendimento.

## Retenção (proposta)
Locais monitorados: até a remoção. Ocorrências: prazo a definir com as Defesas Civis, depois anonimização. Dados de demonstração: recriados a cada implantação.

## Fora do escopo do MVP
Relatório de impacto (RIPD), designação de encarregado, acordos de tratamento com órgãos reais e testes de intrusão.

## Rede comunitária e retransmissão (13/11/2026)
- **Minimização:** a comunidade só vê o resumo que o solicitante autorizou (padrão: nada), com faixas em vez de números para grupos vulneráveis, horário arredondado a 15 min e área ≥ 600 m que contém o ponto sem revelá-lo. Totais regionais só com ≥ 3 pedidos.
- **Base legal pretendida:** consentimento específico e revogável do titular para o resumo comunitário; proteção da vida para o encaminhamento às centrais.
- **Voluntários:** nunca recebem posição, nome ou contato da família. Recebem apenas a orientação escrita pela equipe responsável depois do aceite. O contato do voluntário vai só para essa equipe, se ele autorizar (cifrado em repouso).
- **Auditoria:** aceite, recusa e encaminhamento de ofertas gravam registro com papel, horário e recurso, como o acesso ao detalhe da ocorrência.
- **Retransmissão:** o QR e o link de importação levam um pseudônimo do aparelho (não o identificador real) e nenhum texto livre; o conteúdo fica no fragmento da URL (não vai para logs do servidor). A posição do aparelho que ajuda é opcional, separada e cifrada.
- **Integridade:** QR com hash SHA-256 do texto canônico (detecta corrupção/edição; não autentica); Nearby com HMAC do grupo e chave por gateway; idempotência pelo identificador gerado no emissor.
- **Limite honesto:** o código de grupo `ALERTA-DEMO-2026` e o código da área de demonstração são compartilhados e servem só para a demonstração. Produção exigiria chaves por aparelho emitidas no cadastro e assinatura do emissor.
- **Dados fictícios separados:** pedidos de contas fictícias e QR marcados como demonstração ficam fora da lista pública.

## Monitoramento familiar e consentimentos (13/11/2026)
- **Comunidade recebe:** município, círculo aproximado (≥ 400 m), necessidades autorizadas, verificação, estado mínimo. Nada de pessoas, crianças, idosos, saúde, relato, endereço, horário ou GPS (filtrado no backend).
- **Familiar autorizado recebe:** apelido que ele deu à residência, município, horário, estado/nível, situação marcada, histórico de eventos; necessidades, número de pessoas/moradores e posição exata só se o morador permitir. Nunca relato livre nem dados de saúde.
- **Operador da central do pedido:** como antes (posição precisa, relato, trilha), com auditoria.
- **Revogação:** consentimento D revogado ou vínculo encerrado → o familiar perde acesso imediatamente e não recebe novos avisos; consentimento C revogado → pedidos ativos compartilhados por preferência saem da comunidade; remover a residência encerra vínculos e convites.
- Convites: hash SHA-256, 24 h, uso único, limite por IP, mensagem de erro única (sem enumeração de contas).
- Endereço, moradores, contato e observações: cifrados em repouso. CEP/endereço só são consultados por quem tem conta; a fonte externa recebe só o texto do endereço digitado.
