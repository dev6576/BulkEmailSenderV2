# Angular production build (Angular 22 requires Node 22.12+)
FROM node:22-alpine AS frontend-build
WORKDIR /src/BulkEmailSenderFrontend
COPY BulkEmailSenderFrontend/package*.json ./
RUN npm ci
COPY BulkEmailSenderFrontend/ ./
RUN npm run build -- --configuration production

# ASP.NET Core publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
WORKDIR /src
COPY BulkEmailSender/BulkEmailSender.sln ./BulkEmailSender/
COPY BulkEmailSender/src/BulkEmailSender.Api/BulkEmailSender.Api.csproj ./BulkEmailSender/src/BulkEmailSender.Api/
RUN dotnet restore BulkEmailSender/src/BulkEmailSender.Api/BulkEmailSender.Api.csproj
COPY BulkEmailSender/src/BulkEmailSender.Api/ ./BulkEmailSender/src/BulkEmailSender.Api/
RUN dotnet publish BulkEmailSender/src/BulkEmailSender.Api/BulkEmailSender.Api.csproj -c Release --no-restore -o /publish

# Minimal production runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data /app/wwwroot \
    && chown -R app:app /app
COPY --from=backend-build --chown=app:app /publish/ ./
COPY --from=frontend-build --chown=app:app /src/BulkEmailSenderFrontend/dist/BulkEmailSender.Web/browser/ ./wwwroot/
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD ["sh", "-c", "curl --fail --silent \"http://127.0.0.1:${PORT:-10000}/health\" || exit 1"]
ENTRYPOINT ["sh", "-c", "export ASPNETCORE_URLS=\"http://0.0.0.0:${PORT:-10000}\"; exec dotnet BulkEmailSender.Api.dll"]
