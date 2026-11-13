# Alerta em Ação — imagem única: API ASP.NET Core 10 + PWA (React) + centrais simuladas.
# 1) Frontend
FROM node:22-alpine AS web
WORKDIR /src
COPY apps/web/package*.json apps/web/
RUN cd apps/web && npm ci --no-audit --no-fund
COPY apps/web apps/web
RUN mkdir -p backend/src/AlertaEmAcao.Api && cd apps/web && npx vite build

# 2) Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/ backend/
RUN dotnet restore backend/src/AlertaEmAcao.Api/AlertaEmAcao.Api.csproj
COPY --from=web /src/backend/src/AlertaEmAcao.Api/wwwroot backend/src/AlertaEmAcao.Api/wwwroot
RUN dotnet publish backend/src/AlertaEmAcao.Api/AlertaEmAcao.Api.csproj -c Release -o /app --no-restore

# 3) Execução
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# Voz de reserva em pt-BR sintetizada no próprio servidor (software livre, sem serviço externo).
RUN apt-get update && apt-get install -y --no-install-recommends espeak-ng && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=api /app .
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
# Render/Heroku informam a porta em $PORT. As centrais simuladas rodam no mesmo processo e são chamadas por HTTP real em localhost.
CMD ["sh", "-c", "export P=${PORT:-8080}; ASPNETCORE_HTTP_PORTS=$P Centrais__UrlBaseCentrais=${Centrais__UrlBaseCentrais:-http://localhost:$P} Centrais__UrlRetorno=${Centrais__UrlRetorno:-http://localhost:$P} exec dotnet AlertaEmAcao.Api.dll"]
