# Stage 1: Build (amd64 forced — Grpc.Tools protoc segfaults on linux_arm64)
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy central package management and build props
COPY Directory.Packages.props Directory.Build.props ./

# Copy framework source projects (referenced by sisyphus-maker)
COPY src/ ./src/

# Copy the microservice project
COPY apps/Aevatar.VibeResearching/sisyphus-maker/ ./apps/Aevatar.VibeResearching/sisyphus-maker/

WORKDIR /src/apps/Aevatar.VibeResearching/sisyphus-maker
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Run
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "sisyphus-maker.dll"]
