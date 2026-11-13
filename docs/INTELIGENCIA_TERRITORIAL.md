# Inteligência territorial

`AvaliadorContextoTerritorial` (domínio, testado) combina, para um ponto autorizado:

| Insumo | Critério | Fonte |
|---|---|---|
| Áreas de risco documentadas | Até 150 m do ponto, no mesmo município | Defesa Civil do Recife (Regional Sul) |
| Avisos vigentes | Polígono contém o ponto, ou o município está na lista | INMET (oficial) e demonstrativos (rotulados) |
| Chuva | Estação mais próxima com leitura atual (≤ 2 h), até 10 km | APAC/SIRH |
| Ocorrências | Até 1 km, nas últimas 6 h, relatadas ou verificadas por operador | Alerta em Ação |

## Estados informativos
- **Suscetibilidade conhecida:** há registro documentado perto do ponto (com distância e fonte).
- **Aviso oficial vigente** / **Aviso DEMONSTRATIVO vigente (não oficial)**.
- **Ocorrência relatada:** relatos de cidadãos ainda não confirmados.
- **Ocorrência verificada:** confirmada por operador (no ambiente demonstrativo).
- **Informações desatualizadas:** fonte fora do ar, sem estação próxima ou leitura antiga.
- **Condição desconhecida:** sem dados suficientes. O texto é sempre: *"A ausência de registros não comprova ausência de perigo."*

O sistema **nunca** afirma "esta área é segura" (há teste unitário e de integração verificando isso).

## Orientação preventiva
Textos pré-aprovados (`OrientacoesPreventivas`), adaptados de orientações públicas de Defesa Civil, escolhidos conforme os estados (encosta, alagamento, gerais). A IA não gera ordens de evacuação.

## Consultas espaciais
- Município de um ponto: ray casting em memória sobre os polígonos da APAC (rápido e testado), com tolerância de 300 m na borda.
- Aviso × locais monitorados: PostGIS (`ST_Intersects` entre colunas `geometry` geradas, com índices GIST).
- Relatos comunitários: agregados em células de ~1,1 km sem dados pessoais.

## Limitações
Bases abertas de risco geológico e hidrológico só foram encontradas para parte do Recife. Setores do CPRM/SGB e mapeamentos municipais podem ser integrados com o mesmo adaptador (`IFonteAreasRisco`).
