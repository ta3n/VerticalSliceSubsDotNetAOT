# syntax=docker/dockerfile:1
# Native AOT images for the API and the migrator. Build:
#   podman build --target api      -t subscription-management:latest .
#   podman build --target migrator -t subscription-management-migrator:latest .
# (docker works too). The probe below is also declared in compose.yaml, because podman's default OCI image format drops HEALTHCHECK.

# ---- build: SDK + the native toolchain ILCompiler needs (clang, zlib) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update \
 && apt-get install -y --no-install-recommends clang zlib1g-dev \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Restore first so this layer is cached until a project file changes.
COPY Directory.Build.props ./
COPY src/SubscriptionManagement/SubscriptionManagement.csproj src/SubscriptionManagement/
COPY src/SubscriptionManagement.Migrator/SubscriptionManagement.Migrator.csproj src/SubscriptionManagement.Migrator/
RUN dotnet restore src/SubscriptionManagement -r linux-x64 \
 && dotnet restore src/SubscriptionManagement.Migrator -r linux-x64

COPY src/ src/
RUN dotnet publish src/SubscriptionManagement -c Release -r linux-x64 --no-restore -o /out/api \
 && dotnet publish src/SubscriptionManagement.Migrator -c Release -r linux-x64 --no-restore -o /out/migrator

# ---- runtime base: chiseled Ubuntu (no shell, no package manager, no .NET runtime); runs as the non-root `app` user ----
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled AS runtime-base
WORKDIR /app
USER $APP_UID

FROM runtime-base AS migrator
COPY --from=build /out/migrator/SubscriptionManagement.Migrator ./
ENTRYPOINT ["./SubscriptionManagement.Migrator"]

FROM runtime-base AS api
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /out/api/SubscriptionManagement /out/api/appsettings.json ./
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["./SubscriptionManagement", "healthcheck"]
ENTRYPOINT ["./SubscriptionManagement"]
