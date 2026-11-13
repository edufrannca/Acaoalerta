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
