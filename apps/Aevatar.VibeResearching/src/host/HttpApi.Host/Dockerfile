# ============================================================
# Aevatar.VibeResearching.HttpApi.Host Dockerfile
#
# Build context: 从 repo root (aevatar-agent-framework/) 运行
# 命令: docker build -f apps/Aevatar.VibeResearching/src/host/HttpApi.Host/Dockerfile -t vibe-researching-api .
#
# 注意: 使用 linux/amd64 平台以避免 gRPC Tools 在 ARM64 上的兼容性问题
# ============================================================

# Stage 1: Build (使用 amd64 避免 protoc ARM64 崩溃问题)
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files first (for layer caching)
COPY Directory.Packages.props ./
COPY Directory.Build.props ./
COPY *.sln ./

# Copy all project files
COPY src/ src/
COPY apps/ apps/
COPY plugins/ plugins/

# Restore dependencies
RUN dotnet restore apps/Aevatar.VibeResearching/src/host/HttpApi.Host/Aevatar.VibeResearching.HttpApi.Host.csproj

# Build and publish
# Note: ErrorOnDuplicatePublishOutputFiles=false to handle duplicate files from referenced projects
RUN dotnet publish apps/Aevatar.VibeResearching/src/host/HttpApi.Host/Aevatar.VibeResearching.HttpApi.Host.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    -p:ErrorOnDuplicatePublishOutputFiles=false

# Stage 2: Runtime (同样使用 amd64 保持一致)
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Copy workflows (required by the application)
COPY apps/Aevatar.VibeResearching/src/host/HttpApi.Host/workflows/ ./workflows/

# Create required directories for session data
RUN mkdir -p /app/data/workspace /app/data/.aevatar

# Environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
# Workspace root for Docker (avoids path escaping issues)
ENV VIBE_WORKSPACE_ROOT=/app/data

EXPOSE 8080

ENTRYPOINT ["dotnet", "Aevatar.VibeResearching.HttpApi.Host.dll"]
