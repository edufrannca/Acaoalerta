# Modelo de dados

PostgreSQL 16 + PostGIS 3.4. Schema `alerta` (sistema) e schema `simulador` (centrais simuladas, acessadas só por HTTP).

## Schema `alerta`
| Tabela | Conteúdo | Observações |
|---|---|---|
| `municipios` | 14 municípios, código IBGE, limite | `geom` MULTIPOLYGON gerada, GIST |
| `usuarios` | nome, e-mail, papel, central, consentimentos | telefone cifrado |
| `grupos_familiares` | integrantes e contatos (1 por usuário) | cifrados |
| `locais_monitorados` | apelido, lat/lon, município | `geom` POINT gerada, GIST |
| `avisos` | INMET ou demonstrativo, vigência, polígono, riscos | `geom` gerada; `CodigoExterno` único |
| `notificacoes` | título, pergunta, resposta, entregas por canal (jsonb) | único (`AvisoId`,`UsuarioId`) |
| `assinaturas_push` | endpoint, chaves p256dh/auth | endpoint único |
| `ocorrencias` | categoria, situação, canal, status, lat/lon, precisão, dispositivo, histórico (jsonb) | id gerado no cliente; complemento e trilha cifrados; row version |
| `situacoes_declaradas` | "Estou seguro" e similares | |
| `analises_ocorrencias` | sinalizações das regras, contexto, fontes, status e resultado da IA | resultado da IA cifrado |
| `instituicoes` | centrais simuladas e organizações, URL, papel | nome precisa conter "SIMULAD" |
| `regras_encaminhamento` | município × categoria × condições → instituição | |
| `encaminhamentos` | estado, tentativas, próxima tentativa, protocolo, retornos | único (`OcorrenciaId`,`InstituicaoId`) |
| `areas_risco` | áreas documentadas (Recife) | `geom` POINT gerada, GIST |
| `abrigos` | abrigos oficiais (COP) e fictícios | sempre "disponibilidade não verificada" |
| `necessidades_publicadas`, `ofertas_ajuda` | apoio comunitário | contato da oferta cifrado |
| `auditoria`, `chaves_protecao` | auditoria e chaves do Data Protection | |

## Schema `simulador`
`centrais` (código, nome, modo de falha, atraso) e `recebimentos` (protocolo, idempotency key, conteúdo, estado).

## Decisões
- **Ids gerados no cliente** (Guid) para ocorrências e complementos, garantindo idempotência offline.
- Geometrias como colunas `geometry` **geradas** (`ST_GeomFromGeoJSON`/`ST_MakePoint`) a partir de GeoJSON ou lat/lon, com índices GIST. O domínio não depende de bibliotecas geoespaciais.
- Estruturas pequenas em `jsonb` com comparadores de valor; datas sempre em UTC.
- Campos sensíveis cifrados no formato `v1:` + base64(nonce‖tag‖cifra).
- Leituras de chuva da APAC ficam em cache de memória (não são persistidas).

## Migrações
`backend/src/AlertaEmAcao.Infraestrutura/Persistencia/Migracoes` e `backend/src/AlertaEmAcao.CentraisSimuladas/Migracoes`, aplicadas na inicialização.

## Tabelas novas (migração ResidenciasVinculosConsentimentos)
- `locais_monitorados` + `Tipo`, `MoroAqui`, `Origem`, `DescricaoPrecisao`, `Endereco` (cifrado), `Moradores` (cifrado), `AtualizadoEm`.
- `vinculos_familiares` (ResidenciaId, MoradorId, FamiliarId, ApelidoParaFamiliar, Status, Permissoes jsonb, datas).
- `convites_vinculo` (CodigoHash único, ExpiraEm, UsadoEm/UsadoPor, Cancelado).
- `registros_consentimento` (UsuarioId, Finalidade, Concedido, VersaoTexto, Origem, Em) — só inserções.
- `notificacoes_familiares` (FamiliarId + Chave únicos, OcorrenciaId, VinculoId, Entregas jsonb, LidaEm).
- `ocorrencias.ResidenciaId`; `Compartilhamento.origem`.
