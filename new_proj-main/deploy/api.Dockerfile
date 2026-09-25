# syntax=docker/dockerfile:1
# EduCenter API — build context is ../backend (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/EduCenter.Api/EduCenter.Api.csproj -c Release -o /out /p:UseAppHost=false --nologo

# Only the production configuration ships. Development settings switch on demo seeding and the fake AI provider, and
# appsettings.Local.json (API keys) is kept out by .dockerignore — the last line fails the build if one slipped in anyway.
RUN rm -f /out/appsettings.Development.json /out/appsettings.Local.example.json \
    && test ! -e /out/appsettings.Local.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .

# Uploaded files (chat, lesson resources, student documents, knowledge base, speaking audio) live here and only here:
# the compose file mounts a named volume on it and makes the rest of the filesystem read-only. Created and owned by the
# unprivileged user first, so the fresh volume inherits that ownership.
RUN mkdir -p /app/App_Data && chown -R app:app /app/App_Data

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    HOME=/tmp

USER app
EXPOSE 8080

# The image has no curl/wget on purpose; the app answers its own probe (see Deployment/HealthProbe.cs).
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 \
    CMD ["dotnet", "EduCenter.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "EduCenter.Api.dll"]
