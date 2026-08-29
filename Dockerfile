# Servidor MCP.Harness como imagem de container (stdio).
#   docker run -i --rm -e GITHUB_TOKEN ghcr.io/semog-projects/mcp-harness:latest
ARG TARGETARCH=amd64

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.1.0-dev
ARG TARGETARCH
WORKDIR /src

COPY . .
RUN RID=$([ "$TARGETARCH" = "arm64" ] && echo linux-arm64 || echo linux-x64) && \
    dotnet publish src/MCP.Harness/MCP.Harness.csproj \
        -c Release -r "$RID" -p:Version="$VERSION" -o /app

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /app/mcp-harness /app/appsettings.json ./
ENTRYPOINT ["/app/mcp-harness"]
