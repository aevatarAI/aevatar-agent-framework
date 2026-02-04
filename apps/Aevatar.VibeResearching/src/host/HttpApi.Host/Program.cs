using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.VibeResearching.HttpApi.Host;
using Aevatar.VibeResearching.HttpApi.Host.HostedServices;
using Aevatar.VibeResearching.HttpApi.Host.MinimalApis;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Agents.MinimalApis;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Infrastructure.MongoDB.SkillPacks;
using Microsoft.Extensions.Options;
using Aevatar.Agents.AI.Abstractions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Global user-level secrets (encrypted, per-user) - best-effort.
builder.Configuration.AddAevatarUserConfig();

// Optional per-app secrets (local overrides, do not commit)
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("mcp.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("skillpacks.json", optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Keep skills embedding index in a project-local (gitignored) directory by default.
try
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEVATAR_AGENT_SKILLS_INDEX_DIR")))
    {
        var root = new DirectoryInfo(builder.Environment.ContentRootPath).Parent?.Parent?.Parent?.FullName;
        if (!string.IsNullOrWhiteSpace(root))
        {
            var indexDir = Path.Combine(root, ".skillpacks", ".index");
            Directory.CreateDirectory(indexDir);
            Environment.SetEnvironmentVariable("AEVATAR_AGENT_SKILLS_INDEX_DIR", indexDir);
        }
    }
}
catch
{
    // best-effort only
}

var syncOnly = args.Any(a => string.Equals(a, "--sync-skills", StringComparison.OrdinalIgnoreCase));

// ABP Host Configuration
builder.Host.UseAutofac();
await builder.AddApplicationAsync<VibeResearchingHttpApiHostModule>();

// Conditionally register SkillPacksSyncHostedService (skip if --sync-skills)
if (!syncOnly)
{
    builder.Services.AddHostedService<SkillPacksSyncHostedService>();
}

var app = builder.Build();

if (syncOnly)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("skills-sync");
    logger.LogInformation("Running skills sync then exiting...");
    var sync = app.Services.GetRequiredService<SkillPacksSyncService>();
    var result = await sync.TryEnsureSyncedAsync(SkillPackSyncMode.Manual, CancellationToken.None);
    logger.LogInformation("skills sync ok={Ok} packs={Count} error={Error}", result.Ok, result.Packs.Count, result.Error ?? "");
    return;
}

await app.InitializeApplicationAsync();

// ==========================================
// Module SSE Endpoints (registered via extension methods)
// ==========================================
app.MapSessionSseEndpoints();       // from Sessions.HttpApi (SSE: /api/sessions/{id}/agui/events)
// NOTE: MapAgentSseEndpoints() removed — same route as MapSessionSseEndpoints().
// Sessions module is the canonical home for session SSE events.
app.MapPivotEndpoints();            // from Agents.HttpApi (pivot rollback/snapshots)
app.MapReviewAgentEndpoints();      // from Agents.HttpApi (review agent status/trigger/events)

// ==========================================
// Auth & Profile Endpoints
// ==========================================
app.MapAuthEndpoints();             // GET /api/account/my-permissions
app.MapProfileEndpoints();          // GET/PUT /api/account/my-profile, POST /api/account/change-password
app.MapProfilePictureEndpoints();   // PUT/GET/DELETE /api/account/profile-picture

// ==========================================
// Host-Specific Minimal API Endpoints
// ==========================================

// Workflow list for current frontend
app.MapGet("/api/workflows", (IWorkflowRegistry workflows) =>
{
    var list = workflows.List()
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();
    return Results.Json(list);
}).AllowAnonymous();

// Manual sync (no restart)
app.MapPost("/api/skills/sync", async (SkillPacksSyncService sync, CancellationToken ct) =>
{
    var result = await sync.TryEnsureSyncedAsync(SkillPackSyncMode.Manual, ct);
    return Results.Json(result);
}).RequireAuthorization();

// Live status for frontend polling
app.MapGet("/api/skills/sync/status", (SkillPacksSyncProgress progress) =>
{
    return Results.Json(progress.GetSnapshot());
}).AllowAnonymous();

// System info endpoint (public)
app.MapGet("/api/info", (IOptionsMonitor<LLMProvidersConfig> llm, IConfiguration cfg, Aevatar.Agents.Core.Secrets.IAevatarUserSecretsStore secrets) =>
{
    var cur = llm.CurrentValue;
    var defaultProvider = LlmConfigDefaults.ResolveEffectiveDefaultProviderName(cur);
    cur.Providers.TryGetValue(defaultProvider, out var providerCfg);

    var mcpResolved = MCPServersConfigReader.Resolve(cfg);

    var providersFromConfig = cur.Providers
        .Where(kv => kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ApiKey))
        .Select(kv => kv.Key);

    var providersFromSecrets = secrets.GetAll()
        .Where(kv => kv.Key.StartsWith("LLMProviders:Providers:", StringComparison.OrdinalIgnoreCase) &&
                     kv.Key.EndsWith(":ApiKey", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(kv.Value))
        .Select(kv =>
        {
            var parts = kv.Key.Split(':');
            return parts.Length >= 3 ? parts[2] : null;
        })
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Cast<string>();

    var providersWithKey = providersFromConfig
        .Union(providersFromSecrets, StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x, StringComparer.Ordinal)
        .ToList();

    return Results.Json(new
    {
        system = new
        {
            name = "vibe-researching",
            version = typeof(VibeResearchingHttpApiHostModule).Assembly.GetName().Version?.ToString() ?? "unknown",
            runtimeType = "Local"
        },
        llm = new
        {
            @default = defaultProvider,
            providers = providersWithKey,
            providersAll = cur.Providers.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            provider = providerCfg == null
                ? null
                : new
                {
                    name = defaultProvider,
                    providerType = providerCfg.ProviderType,
                    model = providerCfg.Model,
                    endpoint = providerCfg.Endpoint ?? "",
                    timeoutMilliseconds = providerCfg.TimeoutMilliseconds,
                    hasApiKey = !string.IsNullOrWhiteSpace(providerCfg.ApiKey)
                }
        },
        mcp = new
        {
            source = mcpResolved.Source,
            autoConnect = mcpResolved.AutoConnect,
            namespaceTools = mcpResolved.NamespaceTools,
            servers = mcpResolved.Servers
                .Where(s => s.Enabled)
                .Select(s => new
                {
                    key = s.Key,
                    name = s.Config.Name,
                    transport = s.Config.TransportType.ToString(),
                    url = s.Config.ServerUrl ?? "",
                    command = s.Config.Command ?? "",
                    args = s.Config.Arguments ?? new List<string>(),
                    timeoutMs = s.Config.TimeoutMs
                })
                .ToList()
        }
    });
}).AllowAnonymous();

// LLM Diagnostics (local-only, best-effort)
app.MapGet("/api/llm/test", async (
    HttpContext http,
    IOptionsMonitor<LLMProvidersConfig> llm,
    string? providerName,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

    var resolved = LlmProbe.Resolve(llm.CurrentValue, providerName);
    if (!resolved.Ok)
        return Results.BadRequest(new { ok = false, error = resolved.Error ?? "invalid provider" });

    var result = await LlmProbe.TestAsync(resolved.Provider!, ct);
    return Results.Json(result);
});

app.MapGet("/api/llm/models", async (
    HttpContext http,
    IOptionsMonitor<LLMProvidersConfig> llm,
    string? providerName,
    int? limit,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

    var resolved = LlmProbe.Resolve(llm.CurrentValue, providerName);
    if (!resolved.Ok)
        return Results.BadRequest(new { ok = false, error = resolved.Error ?? "invalid provider" });

    var result = await LlmProbe.FetchModelsAsync(resolved.Provider!, limit ?? 200, ct);
    return Results.Json(result);
});

app.MapGet("/api/llm/status", (
    HttpContext http,
    IOptionsMonitor<LLMProvidersConfig> llm,
    string? providerName) =>
{
    if (!IsLocal(http))
        return Results.Json(new { ok = false, error = "Forbidden: local access only" }, statusCode: 403);

    var resolved = LlmProbe.Resolve(llm.CurrentValue, providerName);
    if (!resolved.Ok)
        return Results.BadRequest(new { ok = false, error = resolved.Error ?? "invalid provider" });

    var p = resolved.Provider!;
    return Results.Json(new
    {
        ok = true,
        providerName = p.ProviderName,
        providerType = p.ProviderType,
        endpoint = p.Endpoint,
        hasApiKey = p.ApiKeyConfigured
    });
});

// LLM Secrets API
app.MapLlmSecretsApi();

// SkillsMP proxy API
app.MapSkillsMpApi();

await app.RunAsync();

static bool IsLocal(HttpContext ctx)
{
    var allowRemote = Environment.GetEnvironmentVariable("ALLOW_REMOTE_LLM_API");
    if (string.Equals(allowRemote, "true", StringComparison.OrdinalIgnoreCase))
        return true;

    var ip = ctx.Connection.RemoteIpAddress;
    return ip == null || System.Net.IPAddress.IsLoopback(ip);
}

// ============================================================
//  Helper Classes
// ============================================================

static class LlmProbe
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public sealed record ResolvedProvider(bool Ok, string? Error, ProviderRuntime? Provider)
    {
        public static ResolvedProvider Fail(string error) => new(false, error, null);
        public static ResolvedProvider Success(ProviderRuntime provider) => new(true, null, provider);
    }

    public sealed record ProviderRuntime(
        string ProviderName,
        string ProviderType,
        string Endpoint,
        bool ApiKeyConfigured,
        string ApiKey);

    public static ResolvedProvider Resolve(LLMProvidersConfig cfg, string? providerName)
    {
        var name = (providerName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
            name = LlmConfigDefaults.ResolveEffectiveDefaultProviderName(cfg);

        if (!cfg.Providers.TryGetValue(name, out var p) || p == null)
        {
            return ResolvedProvider.Fail($"Provider '{name}' is not configured.");
        }

        var apiKeyConfigured = !string.IsNullOrWhiteSpace(p.ApiKey);
        var endpoint = string.IsNullOrWhiteSpace(p.Endpoint) ? "https://api.openai.com" : p.Endpoint.Trim();
        var type = string.IsNullOrWhiteSpace(p.ProviderType) ? "openai" : p.ProviderType.Trim();

        var runtime = new ProviderRuntime(
            ProviderName: name,
            ProviderType: type,
            Endpoint: endpoint,
            ApiKeyConfigured: apiKeyConfigured,
            ApiKey: apiKeyConfigured ? p.ApiKey!.Trim() : string.Empty);

        return ResolvedProvider.Success(runtime);
    }

    public static async Task<object> TestAsync(ProviderRuntime provider, CancellationToken ct)
    {
        if (!provider.ApiKeyConfigured)
        {
            return new { ok = false, providerName = provider.ProviderName, providerType = provider.ProviderType, endpoint = provider.Endpoint, error = "API key not configured" };
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var models = await FetchModelsCoreAsync(provider, max: 20, ct);
            sw.Stop();

            if (models.Ok)
            {
                return new { ok = true, providerName = provider.ProviderName, providerType = provider.ProviderType, endpoint = provider.Endpoint, latencyMs = sw.ElapsedMilliseconds, modelsCount = models.Models.Count, sampleModels = models.Models };
            }

            return new { ok = false, providerName = provider.ProviderName, providerType = provider.ProviderType, endpoint = provider.Endpoint, latencyMs = sw.ElapsedMilliseconds, error = models.Error ?? "unknown error" };
        }
        catch (Exception ex)
        {
            return new { ok = false, providerName = provider.ProviderName, providerType = provider.ProviderType, endpoint = provider.Endpoint, error = ex.Message };
        }
    }

    public static async Task<object> FetchModelsAsync(ProviderRuntime provider, int max, CancellationToken ct)
    {
        max = Math.Clamp(max, 1, 500);

        if (!provider.ApiKeyConfigured)
        {
            return new { ok = false, providerName = provider.ProviderName, providerType = provider.ProviderType, endpoint = provider.Endpoint, error = "API key not configured" };
        }

        var models = await FetchModelsCoreAsync(provider, max, ct);
        if (models.Ok)
        {
            return new { ok = true, providerName = provider.ProviderName, providerType = provider.ProviderType, endpoint = provider.Endpoint, models = models.Models };
        }

        return new { ok = false, providerName = provider.ProviderName, providerType = provider.ProviderType, endpoint = provider.Endpoint, error = models.Error ?? "unknown error" };
    }

    private static async Task<(bool Ok, List<string> Models, string? Error)> FetchModelsCoreAsync(
        ProviderRuntime provider, int max, CancellationToken ct)
    {
        var url = BuildOpenAiModelsUrl(provider.Endpoint);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", provider.ApiKey);
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await ReadBodyBestEffortAsync(resp, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, new List<string>(), $"HTTP {(int)resp.StatusCode}: {TrimForUi(body)}");
        }

        var parsedModels = ParseModelsFromJson(body);
        return (true, parsedModels.Take(max).ToList(), null);
    }

    private static string BuildOpenAiModelsUrl(string endpoint)
    {
        var baseUrl = (endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{baseUrl}/models";
        return $"{baseUrl}/v1/models";
    }

    private static List<string> ParseModelsFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return data.EnumerateArray()
                    .Select(x =>
                    {
                        if (x.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
                        if (!x.TryGetProperty("id", out var id)) return null;
                        return id.GetString();
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch { }

        return new List<string>();
    }

    private static async Task<string> ReadBodyBestEffortAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private static string TrimForUi(string text, int max = 800)
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }
}

static class LlmConfigDefaults
{
    public static string ResolveEffectiveDefaultProviderName(LLMProvidersConfig cfg)
    {
        var def = (cfg.Default ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(def) && cfg.Providers.ContainsKey(def))
            return def;

        var withKey = cfg.Providers
            .Where(kv => kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ApiKey))
            .Select(kv => kv.Key)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(withKey))
            return withKey;

        var any = cfg.Providers.Keys
            .OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(any) ? "default" : any;
    }
}
