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
- IA: provedor Groq implementado; **inferência real ainda não executada** (rede do ambiente e CI bloqueados). O sistema funciona só com as regras.
- Nearby: apps Android/iOS reescritos com protocolo comum; APK e build iOS **não gerados** nesta entrega (CI da conta não inicia; sem Android SDK/Xcode no ambiente); **nenhum teste físico**. Sem multissalto. LoRa apenas documentado. Não é Cell Broadcast.
- PWA: sem Background Sync nem localização em segundo plano; envio pendente ocorre com o app aberto. No iOS, push só com o app instalado na tela inicial.
- Web Push implementado, mas não validado num aparelho real nesta entrega.
- Exclusão de conta não implementada (exportação sim).
- Leitura por voz usa a síntese do aparelho; sem voz pt-BR instalada, usa a voz padrão (o app avisa).
- Ampliação de texto usa zoom do documento (layout em px).
- Instância única: os trabalhos de fundo rodam no mesmo processo; escalar horizontalmente exige um lock distribuído ou fila.
- Plano gratuito de hospedagem hiberna e tem banco com prazo de validade.
- Sem testes de carga, de intrusão ou de acessibilidade com leitor de tela.
