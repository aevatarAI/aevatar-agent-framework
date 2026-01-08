using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.Core.Secrets;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Api.Infrastructure;
using ScientificResearchAssistant.Api;
using ScientificResearchAssistant.Api.Facts;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Paper;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Api.Workspace;

var builder = WebApplication.CreateBuilder(args);

// Global user-level secrets (encrypted, per-user) - best-effort.
// - Default: ~/.aevatar/secrets.json
// - Override: AEVATAR_SECRETS_PATH / AEVATAR_SECRETS_DIR
builder.Configuration.AddAevatarUserSecrets();

// Optional per-app secrets (local overrides, do not commit)
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);
// Optional Cursor-style MCP config (raw mcpServers map). Users can copy ~/.cursor/mcp.json here.
builder.Configuration.AddJsonFile("mcp.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile("skillpacks.json", optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Keep skills embedding index in a project-local (gitignored) directory by default.
// This ensures:
// - sync-time index build and query-time search use the same cache
// - no need to commit large index files into the repo
try
{
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AEVATAR_AGENT_SKILLS_INDEX_DIR")))
    {
        var root = new DirectoryInfo(builder.Environment.ContentRootPath).Parent?.Parent?.FullName;
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

// ==========================================
// Agent Framework Setup
// ==========================================
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.Configure<MaterialsOptions>(builder.Configuration.GetSection(MaterialsOptions.SectionName));

// User secrets store (encrypted, per-user) for runtime writes (UI/API/CLI).
builder.Services.AddAevatarUserSecretsStore();

// Local skill packs sync (Git clone/pull) - best-effort
// - New config: SkillPacks:Packs (recommended)
builder.Services.Configure<SkillPacksOptions>(builder.Configuration.GetSection(SkillPacksOptions.SectionName));
builder.Services.AddSingleton<SkillPacksSyncProgress>();
builder.Services.AddSingleton<SkillPacksSyncService>();
if (!syncOnly)
{
    builder.Services.AddHostedService<SkillPacksSyncHostedService>();
}

// Ensure camelCase JSON (align with AG-UI convention)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddAevatarAgentSystem(b => b.UseLocalRuntime());
builder.Services.AddMEAI();

builder.Services.AddSingleton<ResearchRuntime>();
builder.Services.AddSingleton<MaterialsService>();
builder.Services.AddSingleton<ResearchSessionManager>();
builder.Services.AddSingleton<ResearchRunExecutor>();

// File-SSoT collaboration primitives (paper + facts_proposed + mailbox)
builder.Services.AddSingleton<WorkspaceService>();
builder.Services.AddSingleton<FileMailboxService>();
builder.Services.AddSingleton<PaperService>();
builder.Services.AddSingleton<FactLifecycleService>();

// Vibe: file-backed goals (single source of truth)
builder.Services.AddSingleton<ScientificResearchAssistant.Api.Vibe.Goals.GoalsStore>();

// Vibe: safe uploads for attachment references
builder.Services.AddSingleton<ScientificResearchAssistant.Api.Vibe.Uploads.UploadsStore>();

// Vibe: per-round derivation trace (file-backed)
builder.Services.AddSingleton<ScientificResearchAssistant.Api.Vibe.Trace.TraceStore>();

// Vibe: DAG knowledge library (file-backed)
builder.Services.AddSingleton<ScientificResearchAssistant.Api.Vibe.Dag.DagStore>();

// Vibe: MAKER v2 consensus gate (CognitiveStrategy)
builder.Services.AddSingleton<Aevatar.CognitiveMesh.Strategies.CognitiveStrategy>();
builder.Services.AddSingleton<ScientificResearchAssistant.Api.Vibe.Dag.DagConsensusRunner>();

// Vibe: single-round orchestrator (multi-agent + DAG + trace)
builder.Services.AddSingleton<ScientificResearchAssistant.Api.Vibe.VibeOrchestrator>();

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

app.MapGet("/health", () => Results.Text("ok"));

// Manual sync (no restart)
app.MapPost("/api/skills/sync", async (SkillPacksSyncService sync, CancellationToken ct) =>
{
    var result = await sync.TryEnsureSyncedAsync(SkillPackSyncMode.Manual, ct);
    return Results.Json(result);
});

// Live status for frontend polling (best-effort)
app.MapGet("/api/skills/sync/status", (SkillPacksSyncProgress progress) =>
{
    return Results.Json(progress.GetSnapshot());
});

app.MapGet("/api/info", (IOptionsMonitor<LLMProvidersConfig> llm, IConfiguration cfg) =>
{
    var cur = llm.CurrentValue;
    var defaultProvider = string.IsNullOrWhiteSpace(cur.Default) ? "default" : cur.Default;
    cur.Providers.TryGetValue(defaultProvider, out var providerCfg);

    var mcpResolved = MCPServersConfigReader.Resolve(cfg);

    return Results.Json(new
    {
        system = new
        {
            name = "scientific-research-assistant",
            version = typeof(ResearchSessionsApi).Assembly.GetName().Version?.ToString() ?? "unknown",
            runtimeType = "Local"
        },
        llm = new
        {
            @default = defaultProvider,
            providers = cur.Providers.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
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
});

// ============================================================
//  LLM Diagnostics (local-only, best-effort)
//
//  Purpose:
//  - Let the frontend verify that a configured API key can actually work.
//  - Uses OpenAI-compatible "list models" endpoint for probing.
// ============================================================

app.MapGet("/api/llm/test", async (
    HttpContext http,
    IOptionsMonitor<LLMProvidersConfig> llm,
    string? providerName,
    CancellationToken ct) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

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
        return Results.Forbid();

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
        return Results.Forbid();

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

// ============================================================
//  Secrets API (local-only)
//
//  Purpose:
//  - Let the frontend configure ApiKey and persist it into user secrets (~/.aevatar).
//  - Keep secrets out of repo-local appsettings.secrets.json copies.
// ============================================================

app.MapPost("/api/secrets/llm/api-key", (
    SetLlmApiKeyRequest req,
    IAevatarUserSecretsStore secrets,
    IOptionsMonitor<LLMProvidersConfig> llm,
    HttpContext http) =>
{
    if (!IsLocal(http))
        return Results.Forbid();

    var providerName = (req.ProviderName ?? "").Trim();
    if (string.IsNullOrWhiteSpace(providerName))
    {
        providerName = string.IsNullOrWhiteSpace(llm.CurrentValue.Default) ? "default" : llm.CurrentValue.Default;
    }

    var apiKey = (req.ApiKey ?? "").Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
        return Results.BadRequest(new { error = "apiKey is required" });

    var keyPath = $"LLMProviders:Providers:{providerName}:ApiKey";
    secrets.Set(keyPath, apiKey);

    return Results.Json(new
    {
        ok = true,
        providerName,
        keyPath
    });
});

// Sessions API (AG-UI)
app.MapResearchSessionsApi();

app.Run();

static bool IsLocal(HttpContext ctx)
{
    var ip = ctx.Connection.RemoteIpAddress;
    return ip == null || System.Net.IPAddress.IsLoopback(ip);
}

sealed record SetLlmApiKeyRequest(string? ProviderName, string? ApiKey);

// ============================================================
//  LlmProbe (OpenAI-compatible)
//
//  Notes:
//  - Best-effort: list-models is used as a cheap connectivity probe.
//  - Never log/return secret values.
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
        if (string.IsNullOrWhiteSpace(name))
            name = string.IsNullOrWhiteSpace(cfg.Default) ? "default" : cfg.Default;

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
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                providerType = provider.ProviderType,
                endpoint = provider.Endpoint,
                error = "API key not configured"
            };
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var models = await FetchModelsCoreAsync(provider, max: 20, ct);
            sw.Stop();

            if (models.Ok)
            {
                return new
                {
                    ok = true,
                    providerName = provider.ProviderName,
                    providerType = provider.ProviderType,
                    endpoint = provider.Endpoint,
                    latencyMs = sw.ElapsedMilliseconds,
                    modelsCount = models.Models.Count,
                    sampleModels = models.Models
                };
            }

            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                providerType = provider.ProviderType,
                endpoint = provider.Endpoint,
                latencyMs = sw.ElapsedMilliseconds,
                error = models.Error ?? "unknown error"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                providerType = provider.ProviderType,
                endpoint = provider.Endpoint,
                error = ex.Message
            };
        }
    }

    public static async Task<object> FetchModelsAsync(ProviderRuntime provider, int max, CancellationToken ct)
    {
        max = Math.Clamp(max, 1, 500);

        if (!provider.ApiKeyConfigured)
        {
            return new
            {
                ok = false,
                providerName = provider.ProviderName,
                providerType = provider.ProviderType,
                endpoint = provider.Endpoint,
                error = "API key not configured"
            };
        }

        var models = await FetchModelsCoreAsync(provider, max, ct);
        if (models.Ok)
        {
            return new
            {
                ok = true,
                providerName = provider.ProviderName,
                providerType = provider.ProviderType,
                endpoint = provider.Endpoint,
                models = models.Models
            };
        }

        return new
        {
            ok = false,
            providerName = provider.ProviderName,
            providerType = provider.ProviderType,
            endpoint = provider.Endpoint,
            error = models.Error ?? "unknown error"
        };
    }

    private static async Task<(bool Ok, List<string> Models, string? Error)> FetchModelsCoreAsync(
        ProviderRuntime provider,
        int max,
        CancellationToken ct)
    {
        // Current SRA runtime uses MEAI which is OpenAI-compatible for non-Azure providers.
        // Probe via: GET {endpoint}/v1/models (or {endpoint}/models when endpoint ends with /v1).
        var url = BuildOpenAiModelsUrl(provider.Endpoint);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await ReadBodyBestEffortAsync(resp, ct);
        if (!resp.IsSuccessStatusCode)
        {
            return (false, new List<string>(), $"HTTP {(int)resp.StatusCode}: {TrimForUi(body)}");
        }

        var models = ParseModelsFromJson(body);
        return (true, models.Take(max).ToList(), null);
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
        if (string.IsNullOrWhiteSpace(json))
            return new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                return data.EnumerateArray()
                    .Select(x =>
                    {
                        if (x.ValueKind != JsonValueKind.Object) return null;
                        if (!x.TryGetProperty("id", out var id)) return null;
                        return id.GetString();
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
        catch
        {
            // ignore
        }

        return new List<string>();
    }

    private static async Task<string> ReadBodyBestEffortAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TrimForUi(string text, int max = 800)
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "…";
    }
}
