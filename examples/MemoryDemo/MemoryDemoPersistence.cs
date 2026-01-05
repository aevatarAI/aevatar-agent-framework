using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.Memory.Options;
using Aevatar.Agents.Persistence.MongoDB.Memory.Stores;
using Aevatar.Agents.Persistence.Neo4j;
using Aevatar.Agents.Persistence.Neo4j.DependencyInjection;
using Aevatar.Agents.Persistence.Neo4j.MemoryGraph.Stores;
using Aevatar.Agents.Persistence.Supabase.Memory.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.Memory.Options;
using Aevatar.Agents.Persistence.Supabase.Memory.Stores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MemoryDemo;

// ============================================================
//  MemoryDemoPersistence
//
//  Goal:
//  - Make MemoryDemo "config-driven":
//    - IMemoryStore:       file | mongodb | supabase
//    - IMemoryVectorIndex: file | mongodb | supabase
//    - IMemoryGraphStore:  file | neo4j
//
//  Notes:
//  - "file" provider uses core defaults (env-var aware).
//  - For DB providers, we register infra here, and then use GAgentOptions
//    to replace the default implementations.
// ============================================================
internal static class MemoryDemoPersistence
{
    private const string SectionPath = "Aevatar:Persistence";

    private const string EnvTraceDir = "AEVATAR_TRACE_DIR";
    private const string EnvMemoryDir = "AEVATAR_MEMORY_DIR";
    private const string EnvVectorDir = "AEVATAR_MEMORY_VECTOR_DIR";

    public static MemoryDemoPersistenceResult Configure(
        IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = BindOptions(configuration);
        ApplyFileRootEnvironmentOverrides(options.File);

        var normalized = NormalizeSelection(options);

        // Register backend infrastructure (only when selected).
        RegisterMongoDbInfraIfNeeded(services, configuration, options, normalized);
        RegisterSupabaseInfraIfNeeded(services, configuration, options, normalized);
        RegisterNeo4jInfraIfNeeded(services, options, normalized);

        // Store selection for /api/info diagnostics (avoid instantiating stores).
        var selection = new MemoryDemoPersistenceSelection
        {
            MemoryStoreProvider = normalized.MemoryStoreProvider,
            MemoryVectorIndexProvider = normalized.MemoryVectorIndexProvider,
            MemoryGraphProvider = normalized.MemoryGraphProvider,
            MemoryStoreType = ResolveMemoryStoreType(normalized.MemoryStoreProvider)?.FullName,
            MemoryVectorIndexType = ResolveMemoryVectorIndexType(normalized.MemoryVectorIndexProvider)?.FullName,
            MemoryGraphStoreType = ResolveMemoryGraphStoreType(normalized.MemoryGraphProvider)?.FullName
        };
        services.AddSingleton(selection);

        return new MemoryDemoPersistenceResult(ConfigureStores: go =>
        {
            ArgumentNullException.ThrowIfNull(go);

            go.MemoryStoreType = ResolveMemoryStoreType(normalized.MemoryStoreProvider);
            go.MemoryVectorIndexType = ResolveMemoryVectorIndexType(normalized.MemoryVectorIndexProvider);
            go.MemoryGraphStoreType = ResolveMemoryGraphStoreType(normalized.MemoryGraphProvider);
        }, selection);
    }

    private static MemoryDemoPersistenceOptions BindOptions(IConfiguration configuration)
    {
        var opt = new MemoryDemoPersistenceOptions();
        configuration.GetSection(SectionPath).Bind(opt);
        return opt;
    }

    private static void ApplyFileRootEnvironmentOverrides(MemoryDemoFileRootOptions file)
    {
        // ------------------------------------------------------------
        //  NOTE:
        //  - Core stores are env-var aware.
        //  - This demo allows overriding roots via appsettings.json,
        //    by setting env vars early at startup.
        // ------------------------------------------------------------
        if (!string.IsNullOrWhiteSpace(file.TraceRoot))
            Environment.SetEnvironmentVariable(EnvTraceDir, file.TraceRoot.Trim());

        if (!string.IsNullOrWhiteSpace(file.MemoryRoot))
            Environment.SetEnvironmentVariable(EnvMemoryDir, file.MemoryRoot.Trim());

        if (!string.IsNullOrWhiteSpace(file.VectorRoot))
            Environment.SetEnvironmentVariable(EnvVectorDir, file.VectorRoot.Trim());
    }

    private static NormalizedSelection NormalizeSelection(MemoryDemoPersistenceOptions options)
    {
        var store = NormalizeProviderKey(options.MemoryStore);
        var vector = NormalizeProviderKey(options.MemoryVectorIndex);
        var graph = NormalizeProviderKey(options.MemoryGraph);

        return new NormalizedSelection(store, vector, graph);
    }

    private static string NormalizeProviderKey(string? raw)
    {
        raw ??= string.Empty;
        var k = raw.Trim();
        if (k.Length == 0)
            return "file";

        return k.ToLowerInvariant();
    }

    private static void RegisterMongoDbInfraIfNeeded(
        IServiceCollection services,
        IConfiguration configuration,
        MemoryDemoPersistenceOptions options,
        NormalizedSelection normalized)
    {
        if (!normalized.UsesMongoDb)
            return;

        var conn = configuration.GetConnectionString("MongoDB");
        if (string.IsNullOrWhiteSpace(conn))
        {
            throw new InvalidOperationException(
                "MongoDB selected but ConnectionStrings:MongoDB is empty. " +
                "Please set it in examples/MemoryDemo/appsettings.secrets.json (or env var).");
        }

        var databaseName = configuration.GetSection("MongoDB").GetValue("Database", "aevatar");
        services.AddAevatarMongoDB(conn.Trim(), databaseName);

        // Bind memory module options (collections/index switches).
        services.AddOptions<MongoDbMemoryOptions>()
            .Configure(o =>
            {
                o.MemoryEntriesCollection = options.MongoDbMemory.MemoryEntriesCollection;
                o.MemoryVectorsCollection = options.MongoDbMemory.MemoryVectorsCollection;
                o.EnsureTextIndexOnContent = options.MongoDbMemory.EnsureTextIndexOnContent;
            });
    }

    private static void RegisterSupabaseInfraIfNeeded(
        IServiceCollection services,
        IConfiguration configuration,
        MemoryDemoPersistenceOptions options,
        NormalizedSelection normalized)
    {
        if (!normalized.UsesSupabase)
            return;

        var conn = configuration.GetConnectionString("SupabasePostgres");
        if (string.IsNullOrWhiteSpace(conn))
        {
            throw new InvalidOperationException(
                "Supabase selected but ConnectionStrings:SupabasePostgres is empty. " +
                "Please set it in examples/MemoryDemo/appsettings.secrets.json (or env var).");
        }

        services.AddAevatarSupabaseMemory(conn.Trim(), o =>
        {
            // NOTE: keep connection string consistent (options object may contain empty ConnectionString).
            o.ConnectionString = conn.Trim();

            o.Schema = options.SupabaseMemory.Schema;
            o.MemoryEntriesTable = options.SupabaseMemory.MemoryEntriesTable;
            o.MemoryVectorsTable = options.SupabaseMemory.MemoryVectorsTable;

            o.VectorDimensions = options.SupabaseMemory.VectorDimensions;
            o.DistanceMetric = options.SupabaseMemory.DistanceMetric;
            o.AutoCreateVectorExtension = options.SupabaseMemory.AutoCreateVectorExtension;

            o.EnableFullTextSearch = options.SupabaseMemory.EnableFullTextSearch;
            o.FtsRegConfig = options.SupabaseMemory.FtsRegConfig;

            o.AutoCreateSchema = options.SupabaseMemory.AutoCreateSchema;
            o.AutoCreateTables = options.SupabaseMemory.AutoCreateTables;
            o.AutoCreateIndexes = options.SupabaseMemory.AutoCreateIndexes;

            o.LockDownPublicAccess = options.SupabaseMemory.LockDownPublicAccess;
            o.EnableRowLevelSecurity = options.SupabaseMemory.EnableRowLevelSecurity;
            o.ForceRowLevelSecurity = options.SupabaseMemory.ForceRowLevelSecurity;
            o.CreateServiceRolePolicies = options.SupabaseMemory.CreateServiceRolePolicies;
        });
    }

    private static void RegisterNeo4jInfraIfNeeded(
        IServiceCollection services,
        MemoryDemoPersistenceOptions options,
        NormalizedSelection normalized)
    {
        if (!normalized.UsesNeo4j)
            return;

        if (string.IsNullOrWhiteSpace(options.Neo4j.Uri) ||
            string.IsNullOrWhiteSpace(options.Neo4j.Username) ||
            string.IsNullOrWhiteSpace(options.Neo4j.Password))
        {
            throw new InvalidOperationException(
                "Neo4j selected but Aevatar:Persistence:Neo4j { Uri/Username/Password } is not fully configured.");
        }

        services.AddAevatarNeo4j(o =>
        {
            o.Uri = options.Neo4j.Uri.Trim();
            o.Username = options.Neo4j.Username.Trim();
            o.Password = options.Neo4j.Password;
            o.Database = string.IsNullOrWhiteSpace(options.Neo4j.Database) ? "neo4j" : options.Neo4j.Database.Trim();
        });
    }

    private static Type? ResolveMemoryStoreType(string provider) => provider switch
    {
        "file" => null,
        "mongodb" => typeof(MongoDbMemoryStore),
        "supabase" => typeof(SupabaseMemoryStore),
        _ => throw new InvalidOperationException($"Unknown MemoryStore provider: '{provider}'. Expected: file|mongodb|supabase.")
    };

    private static Type? ResolveMemoryVectorIndexType(string provider) => provider switch
    {
        "file" => null,
        "mongodb" => typeof(MongoDbMemoryVectorIndex),
        "supabase" => typeof(SupabaseMemoryVectorIndex),
        _ => throw new InvalidOperationException($"Unknown MemoryVectorIndex provider: '{provider}'. Expected: file|mongodb|supabase.")
    };

    private static Type? ResolveMemoryGraphStoreType(string provider) => provider switch
    {
        "file" => null,
        "neo4j" => typeof(Neo4jMemoryGraphStore),
        _ => throw new InvalidOperationException($"Unknown MemoryGraph provider: '{provider}'. Expected: file|neo4j.")
    };

    internal sealed record MemoryDemoPersistenceResult(
        Action<GAgentOptions> ConfigureStores,
        MemoryDemoPersistenceSelection Selection);

    internal sealed class MemoryDemoPersistenceSelection
    {
        public required string MemoryStoreProvider { get; init; }
        public required string MemoryVectorIndexProvider { get; init; }
        public required string MemoryGraphProvider { get; init; }

        public string? MemoryStoreType { get; init; }
        public string? MemoryVectorIndexType { get; init; }
        public string? MemoryGraphStoreType { get; init; }
    }

    private sealed record NormalizedSelection(string MemoryStoreProvider, string MemoryVectorIndexProvider, string MemoryGraphProvider)
    {
        public bool UsesMongoDb =>
            MemoryStoreProvider == "mongodb" || MemoryVectorIndexProvider == "mongodb";

        public bool UsesSupabase =>
            MemoryStoreProvider == "supabase" || MemoryVectorIndexProvider == "supabase";

        public bool UsesNeo4j =>
            MemoryGraphProvider == "neo4j";
    }

    internal sealed class MemoryDemoPersistenceOptions
    {
        public string MemoryStore { get; set; } = "file";
        public string MemoryVectorIndex { get; set; } = "file";
        public string MemoryGraph { get; set; } = "file";

        public MemoryDemoFileRootOptions File { get; set; } = new();

        public MongoDbMemoryOptions MongoDbMemory { get; set; } = new();

        public SupabaseMemoryOptions SupabaseMemory { get; set; } = new();

        public Neo4jPersistenceOptions Neo4j { get; set; } = new();
    }

    internal sealed class MemoryDemoFileRootOptions
    {
        public string TraceRoot { get; set; } = string.Empty;
        public string MemoryRoot { get; set; } = string.Empty;
        public string VectorRoot { get; set; } = string.Empty;
    }
}


