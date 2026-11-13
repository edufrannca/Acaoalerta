# Deploy

Uma imagem Docker serve a API, o PWA (`wwwroot`) e as centrais simuladas. É preciso um PostgreSQL com PostGIS.

## Render (gratuito), via `render.yaml`
1. Envie o repositório ao GitHub.
2. Render → **New → Blueprint** → selecione o repositório. O blueprint cria:
   - `alerta-em-acao` (web service Docker, plano free);
   - `alerta-em-acao-db` (PostgreSQL free);
   - `Centrais__SegredoMestre` e `Dados__ChaveCifragem` gerados automaticamente.
3. Opcional (painel do Render → Environment):
   - `IA__Provedor`, `IA__ChaveApi`, `IA__Modelo` para a análise por IA;
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
| `IA__*` | não | Provedor de IA |
| `WebPush__*` | não | VAPID para Web Push |
| `Demo__Habilitado` | não (true) | Login rápido por perfil e `/demo` |

## Situação na entrega
`Dockerfile` e blueprint escritos. **O build da imagem não foi executado** (o ambiente de desenvolvimento não tinha daemon Docker); as etapas equivalentes foram verificadas separadamente: build do front, `dotnet publish -c Release` e execução do pacote publicado em modo Production com `DATABASE_URL` num banco vazio (migrações, 14 municípios, um pedido de socorro recebeu o protocolo `SIM-DC-RECIFE-…` da central simulada). **Nenhuma URL pública foi verificada**: a publicação depende do login do usuário no GitHub e no Render.
