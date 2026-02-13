using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.Agents.AI.DependencyInjection;
using Aevatar.Agents.Cognitive.Core.Strategies;
using Aevatar.Agents.Cognitive.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using SisyphusMaker.Services;

var builder = WebApplication.CreateBuilder(args);

// Writable in-memory config layer (required for per-request NyxID gateway provider injection)
builder.Configuration.AddInMemoryCollection();

// Configuration binding (consensus defaults only — LLM config is in LLMProviders section)
builder.Services.Configure<MakerOptions>(
    builder.Configuration.GetSection("Maker"));

// NyxID LLM Gateway options
builder.Services.Configure<NyxGatewayOptions>(
    builder.Configuration.GetSection("NyxGateway"));
builder.Services.AddScoped<NyxIdConfigurationInjector>();

// ==========================================
// Aevatar Agent Framework
// ==========================================

// Agent system with local runtime (no Orleans needed)
builder.Services.AddAevatarAgentSystem(b => b.UseLocalRuntime());

// Cognitive workflow engine + maker.yaml
var workflowsDir = Path.Combine(builder.Environment.ContentRootPath, "workflows");
builder.Services.AddCognitiveAgents(options =>
{
    options.WorkflowsDirectory = workflowsDir;
    options.LoadBuiltInWorkflows = true;
});
builder.Services.AddSingleton<CognitiveStrategy>();

// LLM providers (MEAI + LLMTornado)
builder.Services.AddAevatarLLMProviders();

// ==========================================
// Services
// ==========================================

builder.Services.AddSingleton<IPromptRenderer, PromptRenderer>();
builder.Services.AddScoped<IVerificationService, VerificationService>();

// Controllers with JSON serialization options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

var app = builder.Build();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "sisyphus-maker",
    engine = "CognitiveStrategy + maker.yaml",
    timestamp = DateTime.UtcNow.ToString("O"),
}));

// OpenAPI spec endpoint
app.MapGet("/openapi.json", async (IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "openapi.json");
    if (!File.Exists(path))
        return Results.NotFound(new { error = "openapi.json not found." });

    var json = await File.ReadAllTextAsync(path);
    return Results.Content(json, "application/json");
});

app.MapControllers();

app.Run();
