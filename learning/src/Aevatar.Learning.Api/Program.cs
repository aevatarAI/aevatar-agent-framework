using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Learning.Chat;
using Aevatar.Learning.Context;
using Aevatar.Learning.Api.Notebooks;
using Aevatar.Learning.Api.Sources;
using Aevatar.Learning.Api.Sessions;
using Aevatar.Learning.Notebooks;
using Aevatar.Learning.Sources;
using Microsoft.Extensions.Options;

// ============================================================
//  Aevatar.Learning API Host (MVP skeleton)
//
//  Hard requirements:
//  - No :5000 in any defaults/examples
//  - AG-UI uses camelCase JSON
//  - LLMProviders must be configurable (no hardcode)
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// Load local secrets file (gitignored) for quick setup.
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);

// AG-UI JSON convention: camelCase.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Runtime selection (MVP defaults to Local).
var runtimeType = builder.Configuration["AgentRuntime:RuntimeType"] ?? "Local";
builder.Services.AddAevatarAgentSystem(b => b.UseLocalRuntime());

// MEAI Provider bridge (LLMProviders -> Microsoft.Extensions.AI).
builder.Services.AddMEAI();

// Sessions (AG-UI)
builder.Services.AddSingleton<LearningSessionsApi.LearningSessionManager>();

// Notebooks (directory-backed)
builder.Services.AddSingleton<NotebookDirectoryStore>();

// Sources (directory-backed)
builder.Services.AddSingleton<SourceStore>();

// Context + Chat (LLM-backed)
builder.Services.AddSingleton<LearningContextBuilder>();
builder.Services.AddSingleton<LearningChatService>();

var app = builder.Build();

// ============================================================
//  APIs
// ============================================================

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/api/info", (IOptions<LLMProvidersConfig> llm) =>
{
    var defaultProvider = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
    llm.Value.Providers.TryGetValue(defaultProvider, out var cfg);

    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

    return Results.Json(new
    {
        version,
        runtimeType,
        llmDefaultProvider = llm.Value.Default,
        llm = new
        {
            @default = defaultProvider,
            providers = llm.Value.Providers.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            provider = cfg == null
                ? null
                : new
                {
                    name = defaultProvider,
                    providerType = cfg.ProviderType,
                    model = cfg.Model,
                    endpoint = cfg.Endpoint ?? "",
                    timeoutMilliseconds = cfg.TimeoutMilliseconds
                }
        }
    });
});

// Sessions API (AG-UI SSE)
app.MapLearningSessionsApi();

// Notebooks API
app.MapNotebooksApi();

// Sources API
app.MapSourcesApi();

app.Run();


