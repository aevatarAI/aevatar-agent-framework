# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy central package management and build props
COPY Directory.Packages.props Directory.Build.props ./

# Copy the Neo4j persistence project (dependency)
COPY src/Aevatar.Agents.Persistence.Neo4j/ ./src/Aevatar.Agents.Persistence.Neo4j/

# Copy the microservice project
COPY apps/Aevatar.VibeResearching/sisyphus-dag/ ./apps/Aevatar.VibeResearching/sisyphus-dag/

WORKDIR /src/apps/Aevatar.VibeResearching/sisyphus-dag
RUN dotnet restore
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "sisyphus-dag.dll"]
