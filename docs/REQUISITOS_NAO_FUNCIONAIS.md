# Requisitos não funcionais

| Categoria | Requisito | Como é atendido |
|---|---|---|
| Disponibilidade da emergência | O pedido nunca depende da IA nem de fontes externas | A IA roda em trabalho separado; falhas no contexto territorial são capturadas (`ServicoAnalise.AvaliarRegrasAsync`) |
| Confiabilidade de entrega | Nenhum "recebido" sem confirmação | Máquina de estados de `EncaminhamentoInstitucional`; o protocolo é obrigatório; há teste de unidade |
| Idempotência | Reenvio não duplica | Id do pedido gerado no cliente (PK); `Idempotency-Key` em cada central (índice único) |
| Resiliência | Retentativa e recuperação após reinício | Backoff de 2 a 60 s, até 12 tentativas; `Transmitindo` → recuperado na subida |
| Offline | Funcionalidades essenciais sem rede | App shell em cache, dados de referência em cache, fila IndexedDB |
| Latência | Despacho em ~1 s; painel ao vivo | Trabalho a cada 1 s; SignalR com reconexão automática |
| Bateria | Sem GPS contínuo descontrolado | `watchPosition` só com o app aberto e filtro de 15 s |
| Segurança | OWASP básico | Rate limit, CSP, nosniff, X-Frame-Options, PBKDF2 (210 mil iterações), HMAC, AES-GCM, autorização por papel |
| Privacidade | LGPD: minimização e finalidade | Ver `SEGURANCA_LGPD.md` |
| Acessibilidade | WCAG 2.2 AA quando aplicável | Contraste da paleta, foco visível, alvos ≥ 44 px, `aria-live` nos estados, `prefers-reduced-motion`, rótulos em todos os campos. **Não auditado formalmente.** |
| Observabilidade | Saber o que falhou | Logs estruturados, `/health` (banco + PostGIS), `/api/estado` (fontes, IA, push), histórico de tentativas HTTP por destino |
| Portabilidade | Rodar em qualquer lugar | Docker (imagem única), `DATABASE_URL`, porta via `$PORT` |
| Escalabilidade (futuro) | Extração de serviços | Módulos sem dependências cruzadas de infraestrutura; centrais já separadas por contrato |
| Fontes públicas | Respeitar ViaCEP e Nominatim | Consulta só por ação explícita, 1 req/s global ao Nominatim, cache (1 dia/7 dias), User-Agent, atribuição, 20 consultas/min por IP e só com conta |
| Notificações | Não perder aviso de familiar | Registro no servidor antes de qualquer canal; deduplicação por índice único; falha de push registrada sem apagar |
| Privacidade por padrão | Nada compartilhado sem escolha | Consentimentos desmarcados; vínculo só por convite + aprovação; permissões sensíveis desligadas por padrão |
