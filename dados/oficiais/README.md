# Dados oficiais e públicos

| Dado | Fonte | Como entra | Cópia local |
|---|---|---|---|
| Limites dos 14 municípios | APAC, camada de limites municipais (ArcGIS, camada 7) | Coleta na inicialização; cópia local de reserva | `backend/src/AlertaEmAcao.Infraestrutura/Dados/limites-rmr-apac.geojson` (12/11/2026, simplificada a ~0,003°) |
| Chuva (estações) | APAC / SIRH (inclui estações do Cemaden) | Sob demanda, com cache de 5 min | Não (cache em memória) |
| Avisos meteorológicos | INMET, Alert-AS (avisos.inmet.gov.br) | A cada 10 min, só os municípios da RMR | Não |
| Áreas de risco documentadas | Dados Abertos Recife (CKAN), Defesa Civil, ODbL | A cada 30 min | Não |
| Abrigos | COP Recife (ArcGIS) | A cada 30 min, sem contatos de coordenadores | Não |
| Mapa base | OpenStreetMap (ODbL), via tiles públicos | No navegador | Tiles vistos ficam em cache |

Os endereços exatos estão em `UrlsFontes` (`Adaptadores/FontesPublicas.cs`) e em `docs/INTEGRACOES.md`.
Com `Trabalhos__FontesExternas=false` nada é coletado e o sistema usa a cópia local dos limites.
