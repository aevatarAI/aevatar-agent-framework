using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.AI.WithTool.MCP.Configuration;
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

// Local skill packs sync (Git clone/pull) - best-effort
// - New config: SkillPacks:Packs (recommended)
builder.Services.Configure<SkillPacksOptions>(builder.Configuration.GetSection(SkillPacksOptions.SectionName));
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

app.MapGet("/api/info", (IOptions<LLMProvidersConfig> llm, IConfiguration cfg) =>
{
    var defaultProvider = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
    llm.Value.Providers.TryGetValue(defaultProvider, out var providerCfg);

    var mcpResolved = MCPServersConfigReader.Resolve(cfg);

    return Results.Json(new
    {
        system = new
        {
            name = "scientific-research-assistant",
            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            runtimeType = "Local"
        },
        llm = new
        {
            @default = defaultProvider,
            providers = llm.Value.Providers.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            provider = providerCfg == null
                ? null
                : new
                {
                    name = defaultProvider,
                    providerType = providerCfg.ProviderType,
                    model = providerCfg.Model,
                    endpoint = providerCfg.Endpoint ?? "",
                    timeoutMilliseconds = providerCfg.TimeoutMilliseconds
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

// Sessions API (AG-UI)
app.MapResearchSessionsApi();

app.Run();
