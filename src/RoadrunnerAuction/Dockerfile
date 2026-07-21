# PR preview image (ADR 19) — used ONLY by the non-prod preview host.
# Production stays Docker-free on systemd LXCs (ADR 02); this Dockerfile
# exists so each open PR gets an isolated compose stack (docs/11).
# Build context is the repo root:
#   docker build -f src/RoadrunnerAuction/Dockerfile -t roadrunner:pr .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching (central package management lives at root).
COPY global.json Directory.Packages.props ./
COPY src/RoadrunnerAuction/RoadrunnerAuction.csproj src/RoadrunnerAuction/
RUN dotnet restore src/RoadrunnerAuction/RoadrunnerAuction.csproj

COPY src/RoadrunnerAuction/ src/RoadrunnerAuction/
RUN dotnet publish src/RoadrunnerAuction/RoadrunnerAuction.csproj \
      -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Connection strings are injected by the compose stack as
# ConnectionStrings__roadrunnerdb / __cache / __messaging (fail-fast if absent).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "RoadrunnerAuction.dll"]
