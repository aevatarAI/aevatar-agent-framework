using System.Text.Json;
using Volo.Abp.AspNetCore.Mvc.AntiForgery;
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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AuditLogging.MongoDB;
using Volo.Abp.Autofac;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.BlobStoring.Database.MongoDB;
using Volo.Abp.Identity;
using Volo.Abp.Identity.AspNetCore;
using Volo.Abp.Identity.MongoDB;
using Volo.Abp.Modularity;
using Volo.Abp.ObjectExtending;
using Volo.Abp.ObjectExtending.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.MongoDB;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.PermissionManagement.MongoDB;
using Volo.Abp.PermissionManagement.OpenIddict;
using Volo.Abp.SettingManagement;
using Volo.Abp.SettingManagement.MongoDB;
using Volo.Abp.Threading;
using MongoDB.Driver;
using Volo.Abp.Data;
using Aevatar.VibeResearching.HttpApi.Host.Blobs;

namespace Aevatar.VibeResearching.HttpApi.Host;

/// <summary>
/// ABP Host Module for Vibe Researching HttpApi.Host.
/// Aggregates all business module ABP modules and configures cross-cutting host concerns.
/// </summary>
[DependsOn(
    // ABP Infrastructure
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreMvcModule),

    // ABP Identity & Account
    typeof(AbpAccountApplicationModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpIdentityMongoDbModule),
    typeof(AbpIdentityAspNetCoreModule),

    // ABP OpenIddict
    typeof(AbpOpenIddictAspNetCoreModule),
    typeof(AbpOpenIddictMongoDbModule),

    // ABP Permission Management
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpPermissionManagementMongoDbModule),
    typeof(AbpPermissionManagementDomainIdentityModule),
    typeof(AbpPermissionManagementDomainOpenIddictModule),

    // ABP Setting Management
    typeof(AbpSettingManagementApplicationModule),
    typeof(AbpSettingManagementHttpApiModule),
    typeof(AbpSettingManagementMongoDbModule),

    // ABP BlobStoring (Avatar)
    typeof(BlobStoringDatabaseDomainModule),
    typeof(BlobStoringDatabaseMongoDbModule),

    // ABP Audit Logging
    typeof(AbpAuditLoggingMongoDbModule),

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
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("VibeResearching");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        // Configure token lifetimes per spec Section 4.3
        PreConfigure<OpenIddictServerBuilder>(builder =>
        {
            builder.SetAccessTokenLifetime(TimeSpan.FromHours(1));
            builder.SetRefreshTokenLifetime(TimeSpan.FromDays(14));

            // Allow HTTP only when explicitly configured (for local development)
            // Production should NOT set this, enforcing HTTPS by default
            var disableHttps = context.Services.GetConfiguration().GetValue<bool>("OpenIddict:DisableHttpsRequirement");
            if (disableHttps)
            {
                builder.UseAspNetCore().DisableTransportSecurityRequirement();
            }
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = services.GetConfiguration();
        var hostEnvironment = services.GetHostingEnvironment();

        // ==========================================
        // OpenIddict Configuration
        // ==========================================
        Configure<AbpOpenIddictAspNetCoreOptions>(options =>
        {
            options.AddDevelopmentEncryptionAndSigningCertificate = true;
        });

        Configure<AbpAntiForgeryOptions>(options =>
        {
            options.AutoValidate = false;
        });

        // ==========================================
        // User Profile Extension (DisplayName, Bio)
        // ==========================================
        ObjectExtensionManager.Instance.Modules()
            .ConfigureIdentity(identity =>
            {
                identity.ConfigureUser(user =>
                {
                    user.AddOrUpdateProperty<string>("DisplayName",
                        property => { property.DefaultValue = ""; });
                    user.AddOrUpdateProperty<string>("Bio",
                        property => { property.DefaultValue = ""; });
                });
            });

        // ==========================================
        // BlobStoring (Avatar Upload)
        // ==========================================
        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.Configure<UserProfilePhotoContainer>(container =>
            {
                container.UseDatabase();
            });
        });

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
            // Configure ABP default connection string for all ABP MongoDB modules
            // (Identity, OpenIddict, PermissionManagement, SettingManagement, BlobStoring, AuditLogging).
            // ABP modules resolve their MongoDB connection via AbpDbConnectionOptions, not the
            // custom MongoDB:ConnectionString key. We must bridge the two.
            var abpConnString = mongoConn!;
            var mongoUrl = new MongoUrl(abpConnString);
            if (string.IsNullOrEmpty(mongoUrl.DatabaseName))
            {
                // Connection string has no database component — append it so ABP modules
                // know which database to use (e.g. mongodb+srv://...host/aevatar?params).
                var builder = new MongoUrlBuilder(abpConnString) { DatabaseName = mongoDb };
                abpConnString = builder.ToString();
            }

            Configure<AbpDbConnectionOptions>(options =>
            {
                options.ConnectionStrings.Default = abpConnString;
            });

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

        app.UseRouting();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();
        app.UseAuthorization();
        app.UseConfiguredEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapGet("/health", () => Results.Text("ok")).AllowAnonymous();
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

    public override async Task OnPostApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // Run ABP data seeder to pre-create MongoDB collections and seed roles/permissions/OpenIddict client.
        // This prevents transaction conflicts when collections don't exist yet.
        try
        {
            var services = context.GetApplicationBuilder().ApplicationServices;
            var dataSeeder = services.GetRequiredService<IDataSeeder>();
            await dataSeeder.SeedAsync();
        }
        catch (Exception ex)
        {
            var logger = context.GetApplicationBuilder().ApplicationServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<VibeResearchingHttpApiHostModule>();
            logger.LogWarning(ex, "Data seed failed (best-effort). Collections may need manual creation.");
        }

        await base.OnPostApplicationInitializationAsync(context);
    }
}
