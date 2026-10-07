# ── 1. Build Stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar archivo de proyecto y restaurar dependencias
COPY GeocercaNotificationsService.csproj .
RUN dotnet restore

# Copiar el código fuente completo y publicar en modo Release
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── 2. Runtime Stage ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Configurar zona horaria America/Lima e instalar utilitarios necesarios (como root)
USER root
ENV TZ=America/Lima
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/*

# Usar el usuario no-root estándar de .NET 8 (app, UID 1654)
USER app

# Copiar los artefactos publicados asignando la propiedad al usuario app
COPY --from=build --chown=app:app /app/publish .

# Puerto estándar de .NET 8 para usuario no-root (8080)
ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "GeocercaNotificationsService.dll"]
