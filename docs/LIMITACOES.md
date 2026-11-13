# Limitações conhecidas

O Alerta em Ação **não substitui** os telefones 193, 199, 192 e 190. Toda tela de socorro mostra os números para ligação.

## Institucionais
- **Nenhum órgão real recebe pedidos.** Defesa Civil, Bombeiros, SAMU, PM e organizações são centrais simuladas. Uma integração real depende de autorização formal, contrato de interface e homologação com cada órgão.
- Não há verificação de identidade do solicitante; pedidos falsos são possíveis (mitigação: limite de requisições, id de dispositivo, triagem humana).

## Dados públicos
- Áreas de risco documentadas: só a parte do Recife publicada em dados abertos (Regional Sul). Nos outros 13 municípios o sistema informa "condição desconhecida".
- Os abrigos da COP Recife não informam a lotação em tempo real; sempre "disponibilidade não verificada".
- Cemaden: sem integração direta; os pluviômetros do Cemaden na RMR chegam pela rede da APAC (estações "[CEMADEN]"), como descrito em INTEGRACOES.
- Os limites municipais são uma cópia simplificada (~300 m) da camada da APAC; perto das divisas, o município pode ser identificado errado (tolerância e confirmação humana no painel).
- Fontes podem sair do ar; o sistema mostra a data da última leitura e "informações desatualizadas".

## Técnicas
- IA: nenhum modelo real foi avaliado nesta entrega (sem chave). O sistema funciona só com as regras.
- Nearby: código Android/iOS não compilado nem testado em aparelhos. Sem multissalto. LoRa apenas documentado.
- PWA: sem Background Sync nem localização em segundo plano; envio pendente ocorre com o app aberto. No iOS, push só com o app instalado na tela inicial.
- Web Push implementado, mas não validado num aparelho real nesta entrega.
- Exclusão de conta não implementada (exportação sim).
- Instância única: os trabalhos de fundo rodam no mesmo processo; escalar horizontalmente exige um lock distribuído ou fila.
- Plano gratuito de hospedagem hiberna e tem banco com prazo de validade.
- Sem testes de carga, de intrusão ou de acessibilidade com leitor de tela.
