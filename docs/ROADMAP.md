# Roadmap

## Já entregue (MVP, 13/11/2026)
SOS com ação imediata e complemento, 14 municípios, regras + IA validada, alertas proativos com "Você e sua família estão bem?", "Estou seguro", "Quero ajudar", fila offline testada, centrais simuladas com falhas, painel em tempo real, abrigos, mapa, exportação LGPD, gateway Nearby (servidor).

## Próximos 30 dias
- Compilar e testar a PoC Nearby em dois Androids e depois Android ↔ iOS.
- Validar Web Push em aparelhos reais; configurar VAPID e IA na hospedagem.
- Exclusão de conta e política de retenção aplicada.
- Teste de acessibilidade (leitor de tela, contraste, alvos de toque) com usuários.

## 3 meses
- Conversa com as Defesas Civis da RMR: contrato de interface, ambiente de homologação e termo de cooperação.
- Integrar setores de risco do SGB/CPRM e mapeamentos municipais dos outros 13 municípios.
- Background Sync e app nativo leve para localização em segundo plano (com consentimento).
- Multissalto store-and-forward via Nearby (TTL, deduplicação, registro de saltos).

## 6–12 meses
- Gateway comunitário LoRa/Meshtastic em pontos de apoio (escolas, associações).
- Integração com o cell broadcast e o SMS 40199 como canais complementares.
- Painel de gestão para as Defesas Civis (relatórios pós-evento, mapa de calor).
- RIPD, encarregado, auditoria externa de segurança e escalabilidade horizontal.
