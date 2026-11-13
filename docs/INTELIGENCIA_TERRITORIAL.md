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

## Indicador de atenção a alagamentos (critérios `alagamento-2026.11.1`)

`GET /api/territorio/alagamento?lat&lon[&regiao]` — motor determinístico (`MotorIndicadorAlagamento`); a IA (Groq) só redige a explicação e é descartada se trouxer número fora dos fatos, percentual, probabilidade ou afirmação de alagamento não verificado.

| Nível | Critério (qualquer um) |
|---|---|
| Crítico | aviso INMET de chuva "Grande Perigo"; chuva > 50 mm/h; chuva forte + ocorrência verificada a ≤ 1 km (6 h) |
| Elevado | aviso "Perigo"; chuva forte (10–50 mm/h, classes OMM); ocorrência verificada a ≤ 1 km (6 h) |
| Atenção | aviso "Perigo Potencial"; chuva moderada; chuva fraca em alta; estação vizinha (≤ 5 km) ≥ 2 classes acima |
| Sem evidência suficiente | nenhum dos anteriores, fonte indisponível ou leitura antiga |

Estação usada: a mais próxima com leitura ≤ 75 min, até 10 km (confiança alta ≤ 2 km, média ≤ 5 km). Áreas de risco documentadas do Recife (encostas) entram só como contexto.

**Estimativa temporal:** não há modelo calibrado nem validação histórica; a resposta sempre traz "Há condições que exigem atenção, mas não existem dados suficientes para estimar quando poderá ocorrer um alagamento." Faltam maré, relevo e drenagem; chuva medida na estação não comprova alagamento no ponto.

**Atualização e avisos:** `TrabalhoIndicadorAlagamento` (5 min) reutiliza o cache da APAC, emite `condicoesAtualizadas` (SignalR) quando há nova leitura e, para locais monitorados com consentimento de alertas, avisa dono e familiares autorizados (vínculo ativo + consentimento D) só quando o nível sobe a Elevado/Crítico — uma vez por episódio (`notificacoes_preventivas`, chave única). Nunca abre SOS. Web Push é tentativa extra: a PWA não garante execução em segundo plano nem entrega sem conexão.
