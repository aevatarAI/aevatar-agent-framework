using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
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
builder.Configuration.AddJsonFile("skillpacks.json", optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var syncOnly = args.Any(a => string.Equals(a, "--sync-skills", StringComparison.OrdinalIgnoreCase));

// ==========================================
// Agent Framework Setup
// ==========================================
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.Configure<MaterialsOptions>(builder.Configuration.GetSection(MaterialsOptions.SectionName));

// Local skill packs sync (Git clone/pull) - best-effort
// - New config: SkillPacks:Packs (recommended)
// - Legacy config: ClaudeScientificSkills (fallback)
builder.Services.Configure<SkillPacksOptions>(builder.Configuration.GetSection(SkillPacksOptions.SectionName));
builder.Services.Configure<ClaudeScientificSkillsSyncOptions>(
    builder.Configuration.GetSection(ClaudeScientificSkillsSyncOptions.SectionName));
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

    var mcp = cfg.GetSection("MCP");

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
            type = mcp["Type"] ?? "Http",
            httpUrl = mcp["HttpUrl"] ?? "",
            dockerImage = mcp["DockerImage"] ?? "",
            requestTimeoutMs = mcp.GetValue<int?>("RequestTimeoutMs") ?? 0
        }
    });
});

// Sessions API (AG-UI)
app.MapResearchSessionsApi();

app.Run();
