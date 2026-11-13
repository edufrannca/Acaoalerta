# Integrações

## Fontes públicas reais (verificadas em 12/11/2026)

| Fonte | Endpoint | Autenticação | Licença / uso | Uso no sistema | Frequência | Falha |
|---|---|---|---|---|---|---|
| APAC: chuva por estação (SIRH) | `geoportal.apac.pe.gov.br/.../met_monitoramento_chuvas_pe/MapServer/4/query` | Nenhuma | Dados públicos do governo de PE | Chuva 1 h–72 h, leitura e estado | Cache de 5 min; a fonte atualiza a cada ~10 min | Mantém a última leitura, marcada "fonte indisponível"; leituras > 2 h aparecem "desatualizadas"; `-1` vira "sem dado" |
| APAC: limites municipais | `.../MapServer/7/query` | Nenhuma | Idem | Polígonos dos 14 municípios | 6 h | Cópia local embarcada |
| INMET: avisos ativos | `apiprevmet3.inmet.gov.br/avisos/ativos` | Nenhuma | Público | Avisos oficiais com polígono e municípios (IBGE) | 10 min | Registra a indisponibilidade; os avisos já gravados continuam valendo até o fim da vigência |
| Prefeitura do Recife: áreas de risco | `dados.recife.pe.gov.br/api/3/action/datastore_search?resource_id=75344435-…` | Nenhuma | **ODbL** | 401 pontos da Regional Sul | 6 h | Limitação exibida ao usuário |
| COP Recife: abrigos | `esigportal2.recife.pe.gov.br/arcgis/.../Abrigos_Ativos/FeatureServer/0/query` | Nenhuma | Público | Nome, tipo, endereço, coordenadas, status, acessibilidade. **Nomes e telefones de coordenadores não são importados** | 30 min | Abrigos fictícios identificados continuam disponíveis |
| OpenStreetMap: mapa base | `tile.openstreetmap.org` | Nenhuma | ODbL, atribuição obrigatória (exibida) | Fundo do mapa | Cache do service worker | Sem fundo, as camadas próprias continuam visíveis |

Todos os adaptadores têm timeout (12–30 s), registram o estado em `/api/estado` e nunca inventam dados.

## Fontes avaliadas e não usadas
- **COP Recife `BASES_FEATURES_COP_CONSULTA`**: responde `499 Token Required`. **Não contornamos a autenticação.**
- **Cemaden**: os dados pluviométricos da RMR já chegam pela APAC (estações "[CEMADEN]").
- **Google Weather API (alertas)**: exige chave e faturamento. Não contratado.

## Integrações simuladas (demonstração)
Ver `CENTRAIS_SIMULADAS.md`. Contrato `alerta-em-acao/central/v1` em `backend/src/AlertaEmAcao.Contratos`.

## Dependem de autorização institucional (futuro)
- Recebimento real por Defesa Civil, Bombeiros, SAMU, PM e CIODS (`CanalIntegracao.HttpInstitucionalHomologado` reservado; o cliente recusa o envio).
- Sinesp CAD e sistemas municipais de despacho.
- Avisos da Defesa Civil via Interface de Divulgação de Alertas Públicos (IDAP) / cell broadcast.

## IA
`IA__Provedor=anthropic` (Messages API) ou `openai` (qualquer API compatível com `/chat/completions`), com `IA__ChaveApi` e `IA__Modelo`. Sem chave, o sistema roda só com as regras determinísticas, e isso aparece na interface.

## Web Push
`WebPush__ChavePublica/ChavePrivada` (gere com `node scripts/gerar-chaves.mjs`). No iOS, só com o PWA instalado na tela de início (iOS 16.4+).
