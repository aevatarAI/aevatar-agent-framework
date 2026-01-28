using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.DependencyInjection;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.Cognitive.DependencyInjection;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.GAgent;
using Aevatar.Agents.Persistence.Neo4j.Graph.DependencyInjection;
using Aevatar.Agents.Persistence.SQLite.GAgent.DependencyInjection;
using Aevatar.Agents.Persistence.SQLite.GAgent.Stores;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Sessions;
using Aevatar.VibeResearching.HttpApi.Host.HostedServices;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.MongoDB;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Knowledge;
using Aevatar.VibeResearching.Knowledge.Neo4j;
using Aevatar.VibeResearching.Agents;
using Aevatar.VibeResearching.Agents.MongoDB;
using Aevatar.VibeResearching.Agents.ReviewAgent;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Infrastructure.MongoDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Data.Sqlite;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.HttpApi.Host;

/// <summary>
/// ABP Host Module for Vibe Researching HttpApi.Host.
/// Aggregates all business module ABP modules and configures cross-cutting host concerns.
/// </summary>
[DependsOn(
    // ABP Infrastructure
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMvcModule),

    // Sessions Module
    typeof(VibeSessionsHttpApiModule),
    typeof(VibeSessionsApplicationModule),
    typeof(VibeSessionsMongoDbModule),

    // Knowledge Module
    typeof(VibeKnowledgeHttpApiModule),
    typeof(VibeKnowledgeApplicationModule),
    typeof(VibeKnowledgeNeo4jModule),

    // Agents Module
    typeof(VibeAgentsHttpApiModule),
    typeof(VibeAgentsApplicationModule),
    typeof(VibeAgentsMongoDbModule),

    // Infrastructure Module
    typeof(VibeInfrastructureHttpApiModule),
    typeof(VibeInfrastructureApplicationModule),
    typeof(VibeInfrastructureMongoDbModule)
)]
public class VibeResearchingHttpApiHostModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = services.GetConfiguration();
        var hostEnvironment = services.GetHostingEnvironment();

        // ==========================================
        // Configuration Options (host-level)
        // ==========================================
        services.Configure<LLMProvidersConfig>(configuration.GetSection("LLMProviders"));
        services.Configure<Aevatar.VibeResearching.Agents.Pivot.PivotOptions>(
            configuration.GetSection(Aevatar.VibeResearching.Agents.Pivot.PivotOptions.SectionName));
        services.Configure<Aevatar.VibeResearching.Agents.Mesh.MeshOrchestrationOptions>(
            configuration.GetSection(Aevatar.VibeResearching.Agents.Mesh.MeshOrchestrationOptions.SectionName));

        // ==========================================
        // JSON Serialization (camelCase for AG-UI)
        // ==========================================
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        // ==========================================
        // CORS Configuration
        // ==========================================
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? ["http://localhost:3000", "http://localhost:5173"];

                policy.WithOrigins(allowedOrigins)
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });

        // ==========================================
        // User Secrets Store
        // ==========================================
        services.AddAevatarUserSecretsStore();

        // ==========================================
        // Persistence Configuration (MongoDB/SQLite)
        // ==========================================
        var mongoConn =
            configuration["MongoDB:ConnectionString"] ??
            configuration["MONGODB_CONNECTION_STRING"] ??
            configuration["AEVATAR_MONGODB_CONNECTION_STRING"];

        var mongoDb =
            configuration["MongoDB:Database"] ??
            configuration["MONGODB_DATABASE"] ??
            "aevatar";

        var sqliteConn =
            configuration["SQLite:ConnectionString"] ??
            configuration["SQLITE_CONNECTION_STRING"] ??
            configuration["AEVATAR_SQLITE_CONNECTION_STRING"];

        var sqlitePath =
            configuration["SQLite:Path"] ??
            configuration["SQLITE_PATH"] ??
            configuration["AEVATAR_SQLITE_PATH"];

        var sqliteEnabled = configuration.GetValue<bool?>("SQLite:Enabled") ?? false;

        if (string.IsNullOrWhiteSpace(sqliteConn) && string.IsNullOrWhiteSpace(sqlitePath) && sqliteEnabled)
        {
            var root = Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, "..", ".."));
            var dataDir = Path.Combine(root, "workspace", ".data");
            Directory.CreateDirectory(dataDir);
            sqlitePath = Path.Combine(dataDir, "vibe.db");
        }

        if (string.IsNullOrWhiteSpace(sqliteConn) && !string.IsNullOrWhiteSpace(sqlitePath))
        {
            var builderConn = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath!.Trim(),
                Cache = SqliteCacheMode.Shared
            };
            sqliteConn = builderConn.ToString();
        }

        var useMongo = !string.IsNullOrWhiteSpace(mongoConn);
        var useSqlite = !useMongo && (sqliteEnabled || !string.IsNullOrWhiteSpace(sqliteConn));

        if (useMongo)
        {
            services.AddAevatarMongoDB(mongoConn!, mongoDb);
            services.AddAevatarAgentSystem(options =>
            {
                options.StateStoreType = typeof(MongoDBStateStore<>);
                options.EventRouterStoreType = typeof(MongoDBEventRouterStore);
            }, b => b.UseLocalRuntime());
        }
        else if (useSqlite)
        {
            services.AddAevatarSQLiteGAgent(sqliteConn!);
            services.AddAevatarAgentSystem(options =>
            {
                options.StateStoreType = typeof(SQLiteStateStore<>);
                options.EventRouterStoreType = typeof(SQLiteEventRouterStore);
            }, b => b.UseLocalRuntime());
        }
        else
        {
            services.AddAevatarAgentSystem(b => b.UseLocalRuntime());
        }

        // ==========================================
        // Cognitive Workflows
        // ==========================================
        var workflowsDir = Path.Combine(hostEnvironment.ContentRootPath, "workflows");
        services.AddCognitiveAgents(options =>
        {
            options.WorkflowsDirectory = workflowsDir;
            options.LoadBuiltInWorkflows = true;
        });
        services.AddSingleton<Aevatar.Agents.Cognitive.Core.Strategies.CognitiveStrategy>();

        // ==========================================
        // LLM Providers
        // ==========================================
        services.AddAevatarLLMProviders();

        // ==========================================
        // Agent YAML Registry + Research Runtime
        // ==========================================
        services.AddSingleton<Aevatar.Agents.AI.Core.Configuration.GlobalAgentYamlRegistry>();
        services.AddSingleton<ResearchRuntime>();
        services.Configure<SkillPacksOptions>(configuration.GetSection(SkillPacksOptions.SectionName));

        // ==========================================
        // Knowledge Graph — defaults to Neo4j; set Neo4j:UseInMemory=true to fall back
        // ==========================================
        var useInMemoryGraph = configuration.GetValue<bool>("Neo4j:UseInMemory");
        if (useInMemoryGraph)
        {
            services.AddAevatarGraphInMemory();
        }
        else
        {
            var neo4jUri  = configuration["Neo4j:Uri"]      ?? Environment.GetEnvironmentVariable("NEO4J_URI")      ?? "bolt://localhost:7687";
            var neo4jUser = configuration["Neo4j:Username"]  ?? Environment.GetEnvironmentVariable("NEO4J_USERNAME") ?? "neo4j";
            var neo4jPass = configuration["Neo4j:Password"]  ?? Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "neo4j";
            var neo4jDb   = configuration["Neo4j:Database"]  ?? Environment.GetEnvironmentVariable("NEO4J_DATABASE") ?? "neo4j";
            services.AddAevatarGraphNeo4j(neo4jUri, neo4jUser, neo4jPass, neo4jDb);
        }
        services.AddKnowledgeGraph();

        // Review Agent (always registered; uses graph backend above)
        services.Configure<ReviewAgentOptions>(configuration.GetSection(ReviewAgentOptions.SectionName));
        services.AddSingleton<ReviewAgentHostedService>();
        services.AddSingleton<IReviewAgentTrigger>(sp => sp.GetRequiredService<ReviewAgentHostedService>());
        services.AddHostedService(sp => sp.GetRequiredService<ReviewAgentHostedService>());
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();

        // Enable CORS (must be before routing/endpoints)
        app.UseCors();

        // Map health check
        app.UseRouting();
        app.UseConfiguredEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapGet("/health", () => Results.Text("ok"));
        });
    }

    public override async Task OnPreApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // Best-effort: restore persisted session registry (if any)
        try
        {
            var app = context.GetApplicationBuilder();
            var services = app.ApplicationServices;
            var sessions = services.GetRequiredService<ResearchSessionManager>();
            await sessions.LoadPersistedSessionsAsync(CancellationToken.None);
        }
        catch
        {
            // best-effort only
        }

        await base.OnPreApplicationInitializationAsync(context);
    }
}
