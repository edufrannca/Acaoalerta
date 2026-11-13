# Visão do produto

## Problema
Na Região Metropolitana do Recife, chuvas intensas causam alagamentos e deslizamentos todos os anos. A informação existe, mas está espalhada: a APAC mede a chuva, o INMET emite avisos, as Defesas Civis mapeiam as áreas de risco, o COP do Recife lista abrigos. Na hora da emergência, o cidadão precisa juntar tudo isso sozinho, e o pedido de ajuda costuma depender de uma ligação que pode não completar.

## Proposta
O Alerta em Ação **não pretende ser o primeiro** a integrar órgãos, usar IA em ocorrências ou aceitar pedidos sem cadastro. Iniciativas como a Defesa Civil (SMS 40199), a APAC, o Cemaden, o COP Recife, o Sinesp CAD e as plataformas municipais já cobrem partes do problema.

A inovação está na **combinação**:
1. **Interoperabilidade:** um contrato HTTP versionado para qualquer central, roteado por município e tipo de risco, cobrindo os 14 municípios.
2. **Inteligência territorial:** o ponto do cidadão é cruzado com áreas de risco, avisos, chuva e relatos, sempre com a fonte e o horário de cada informação, e sem nunca afirmar "área segura".
3. **Comunicação bidirecional:** aviso → pergunta proativa "Você e sua família estão bem?" → resposta → pedido → confirmação por central → retorno do atendimento ao cidadão.
4. **Resiliência:** o pedido é salvo no aparelho primeiro, com estados verificáveis, sincronização sem duplicar e caminho para canais por proximidade (Nearby) e rádio (LoRa).
5. **Experiência unificada:** cidadão, voluntário, operador e organização na mesma plataforma, cada um vendo só o que precisa.

## Públicos
- **Cidadão:** prevenção, avisos, pedido de socorro, "estou seguro", família, locais monitorados.
- **Voluntário:** ofertas de ajuda ligadas a necessidades de organizações verificadas.
- **Operador institucional:** fila do município ou da especialidade, análise explicada, confirmação e reenvio.
- **Organização de apoio:** recebe as ofertas e confirma a participação.
- **Administrador:** configuração, auditoria e cenários de demonstração.

## Princípios
- Nenhum status sem evento comprovado: "recebido" só com protocolo emitido pela central depois de persistir.
- O que é real é real, e o que é simulado é rotulado em toda tela.
- A IA apoia a decisão humana e não decide prioridade de vidas.
- Coleta mínima, dados sensíveis cifrados e localização precisa só para quem atende.
