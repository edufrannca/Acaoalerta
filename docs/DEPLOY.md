# Deploy

Uma imagem Docker serve a API, o PWA (`wwwroot`) e as centrais simuladas. É preciso um PostgreSQL com PostGIS.

## Render (gratuito), via `render.yaml`
1. Envie o repositório ao GitHub.
2. Render → **New → Blueprint** → selecione o repositório. O blueprint cria:
   - `alerta-em-acao` (web service Docker, plano free);
   - `alerta-em-acao-db` (PostgreSQL free);
   - `Centrais__SegredoMestre` e `Dados__ChaveCifragem` gerados automaticamente.
3. Opcional (painel do Render → Environment):
   - **`IA__ChaveApi`** = chave da Groq (o blueprint já define `IA__Provedor=groq` e `IA__Modelo=openai/gpt-oss-20b`);
   - `WebPush__ChavePublica`, `WebPush__ChavePrivada`, `WebPush__Assunto` (gere com `node scripts/gerar-chaves.mjs`) para o Web Push.
4. O primeiro acesso aplica as migrações (`CREATE EXTENSION postgis`), carrega os 14 municípios e os dados demonstrativos.

**Limites do plano gratuito** (confira a página de preços do Render antes da apresentação): o serviço hiberna após inatividade e leva cerca de 1 minuto para acordar; o banco gratuito expira depois de um período. **Abra a URL alguns minutos antes da apresentação.** Nenhum plano pago é necessário nem foi contratado.

## Alternativas
- Banco: Neon ou Supabase (PostGIS disponível). Defina `DATABASE_URL`.
- App: qualquer serviço que rode Docker e defina `PORT` (Fly.io, Railway, Koyeb).

## Local
```bash
cp .env.example .env    # ajuste se quiser
docker compose up --build
# http://localhost:8080  (modo demonstração em /demo)
```

## Variáveis
| Variável | Obrigatória | Uso |
|---|---|---|
| `DATABASE_URL` (formato `postgresql://…`) ou `ConnectionStrings__Postgres` | sim | PostgreSQL/PostGIS |
| `Centrais__SegredoMestre` | sim | Deriva as chaves das centrais e do gateway |
| `Dados__ChaveCifragem` | sim em produção | AES-256 (32 bytes em base64; outro segredo é derivado por SHA-256) |
| `Trabalhos__FontesExternas` | não (true) | Coleta APAC/INMET/COP/Recife |
| `IA__Provedor`, `IA__Modelo`, `IA__ChaveApi` | não | Groq (`groq`, `openai/gpt-oss-20b`, chave) |
| `Proximidade__CodigoGrupo` | não (`ALERTA-DEMO-2026`) | Grupo do protocolo de proximidade (o mesmo nos apps) |
| `WebPush__*` | não | VAPID para Web Push |
| `Demo__Habilitado` | não (true) | Login rápido por perfil e `/demo` |

## Situação na entrega
**Publicado em 13/11/2026: https://alerta-em-acao.onrender.com** — `/health` ok (banco ok, PostGIS 3.6.2); validado às 02:30: gateway v2 até nível 5, IA Groq real concluída (839 ms), alerta demonstrativo com a pergunta correta, `/instalar` e manifest com 4 ícones.

### Histórico
`Dockerfile` e blueprint escritos. **O build da imagem não foi executado** (o ambiente de desenvolvimento não tinha daemon Docker); as etapas equivalentes foram verificadas separadamente: build do front, `dotnet publish -c Release` e execução do pacote publicado em modo Production com `DATABASE_URL` num banco vazio (migrações, 14 municípios, um pedido de socorro recebeu o protocolo `SIM-DC-RECIFE-…` da central simulada). **Nenhuma URL pública foi verificada**: a publicação depende do login do usuário no GitHub e no Render.

## Validação após publicar (checklist)
1. `GET /health` → 200. 2. `/` abre com HTTPS; `/instalar` mostra o QR da URL pública. 3. Perfil → Administrador → `/demo` → publicar aviso;
4. pedido de socorro → painel mostra níveis 4 e 5 e protocolos `SIM-…`; 5. painel → IA "Concluida" com modelo `openai/gpt-oss-20b` (se a chave foi colada);
6. `ALERTA_URL=https://… python3 simuladores/nearby-ponta-a-ponta.py` → OK. 7. Abra a URL ~15 min antes do pitch (o plano gratuito hiberna).

## Contingência (sem depender do servidor hibernado)
Notebook na mesma rede Wi-Fi dos celulares: `docker compose up --build` → `http://<ip-do-notebook>:8080`. Limitações: sem HTTPS, o celular não instala a PWA
nem libera GPS/notificações (use o navegador do notebook para essas partes); os apps nativos funcionam (cleartext permitido para rede local).
