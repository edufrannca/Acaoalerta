# Municípios da RMR

Códigos IBGE **conferidos em 12/11/2026** contra o campo `cd_mun` da camada oficial "Limite municipal" (id 7) do serviço `met_monitoramento_chuvas_pe` da APAC.

| Município | IBGE | Central demonstrativa | Áreas de risco abertas | Abrigos |
|---|---|---|---|---|
| Recife | 2611606 | DC-RECIFE | Sim, Regional Sul (Dados Abertos Recife, ODbL) | COP Recife (ArcGIS) |
| Jaboatão dos Guararapes | 2607901 | DC-JABOATAO-GUARARAPES | Não identificada | fictício |
| Olinda | 2609600 | DC-OLINDA | Não identificada | fictício |
| Paulista | 2610707 | DC-PAULISTA | Não identificada | — |
| Camaragibe | 2603454 | DC-CAMARAGIBE | Não identificada | — |
| São Lourenço da Mata | 2613701 | DC-SAO-LOURENCO-MATA | Não identificada | — |
| Cabo de Santo Agostinho | 2602902 | DC-CABO-SANTO-AGOSTINHO | Não identificada | — |
| Ipojuca | 2607208 | DC-IPOJUCA | Não identificada | fictício |
| Moreno | 2609402 | DC-MORENO | Não identificada | — |
| Abreu e Lima | 2600054 | DC-ABREU-E-LIMA | Não identificada | — |
| Igarassu | 2606804 | DC-IGARASSU | Não identificada | — |
| Itapissuma | 2607752 | DC-ITAPISSUMA | Não identificada | — |
| Ilha de Itamaracá | 2607604 | DC-ILHA-ITAMARACA | Não identificada | — |
| Araçoiaba | 2601052 | DC-ARACOIABA | Não identificada | — |

(Os códigos das centrais são gerados por `DadosIniciais.CodigoDefesaCivil`; veja a lista exata em `GET /simulador/centrais`.)

**Chuva:** as estações da APAC/SIRH (que agregam CEMADEN e INMET) cobrem a RMR inteira.
**Avisos:** o INMET (Alert-AS) cobre todos os municípios por código IBGE e polígono.

## Limites geográficos
- **Primário:** consulta ao vivo da camada 7 da APAC (`TrabalhoFontesReferencia`, a cada 6 h), simplificada em ~50 m.
- **Cópia local embarcada:** `backend/src/AlertaEmAcao.Infraestrutura/Dados/limites-rmr-apac.geojson`, da mesma camada, obtida em 12/11/2026 e simplificada em ~300 m (`maxAllowableOffset=0.003`). É usada ao iniciar e quando a APAC estiver fora.
- Pontos a até 300 m da borda simplificada são atribuídos ao município mais próximo (`porTolerancia=true`).

## Transparência
Quando não há base pública aberta para um município, o app mostra a limitação: "Não identificamos base pública aberta de setores de risco… ausência de registro não significa ausência de risco". Não inventamos polígonos, sensores, unidades oficiais nem abrigos.
