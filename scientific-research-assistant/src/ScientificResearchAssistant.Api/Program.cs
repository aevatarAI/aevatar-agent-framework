using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Api;
using ScientificResearchAssistant.Api.Materials;
using ScientificResearchAssistant.Api.Sessions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ==========================================
// Agent Framework Setup
// ==========================================
builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.Configure<MaterialsOptions>(builder.Configuration.GetSection(MaterialsOptions.SectionName));

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

var app = builder.Build();

app.MapGet("/health", () => Results.Text("ok"));

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
