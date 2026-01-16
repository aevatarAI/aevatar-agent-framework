using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.CreativeReasoning;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using CreativeSystem.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddAevatarUserConfig()
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

// -------------------------------------------------------------------------
// Ports (repo policy)
// -------------------------------------------------------------------------
// Repo policy: do not bind to :5000 by default (avoid conflicts/misleading docs).
// If no URLs are configured (ASPNETCORE_URLS / --urls), use 5678 as a
// recommended local port. Any available port is fine.
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    builder.WebHost.UseUrls("http://localhost:5678");
}

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

// Add MassTransit Stream Plugin (includes all UoT agents: C/E/T)
builder.Services.AddMassTransitStreamPlugin(
    builder.Configuration,
    typeof(Aevatar.Agents.CreativeReasoning.Agents.UoTCoordinatorGAgent).Assembly  // Contains UoT, EUoT, TUoT
);

// Add Aevatar Local Runtime
builder.Services.AddAevatarAgentSystem(aevatar => aevatar.UseLocalRuntime());

// Add MEAI LLM infrastructure
builder.Services.AddMEAI();

// Add UoT Creative Reasoning (with default provider)
builder.Services.AddUoTCreativeReasoning(AevatarAgentsConstants.DefaultProviderName);

// Creative Project service
builder.Services.AddSingleton<CreativeProjectService>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Static files
app.UseDefaultFiles();
app.UseStaticFiles();

// API endpoints
app.MapGet("/api/problems", (CreativeProjectService svc) =>
    Results.Json(svc.GetSampleProblems()));

app.MapPost("/api/solve", async (CreativeSolveRequest request, CreativeProjectService svc, CancellationToken ct) =>
    Results.Json(await svc.StartSolveAsync(request, ct)));

app.MapGet("/api/runs/{runId}/status", (string runId, CreativeProjectService svc) =>
    Results.Json(svc.GetStatus(runId)));

app.MapGet("/api/runs/{runId}/result", (string runId, CreativeProjectService svc) =>
    Results.Json(svc.GetResult(runId)));

// Unified execution trace (ExecutionTrace JSON)
app.MapGet("/api/runs/{runId}/trace", async (string runId, CreativeProjectService svc, CancellationToken ct) =>
{
    var json = await svc.GetExecutionTraceJsonAsync(runId, ct);
    return json == null ? Results.NotFound() : Results.Text(json, "application/json");
});

// SSE endpoint for real-time streaming
app.MapGet("/api/runs/{runId}/events", async (
    string runId, 
    CreativeProjectService svc,
    HttpContext ctx,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    logger.LogInformation("[SSE] Client connected for run {RunId}", runId);
    
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    
    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };
    
    var eventCount = 0;
    await foreach (var evt in svc.GetEventStreamAsync(runId, ct))
    {
        eventCount++;
        var json = System.Text.Json.JsonSerializer.Serialize(evt, jsonOptions);
        logger.LogDebug("[SSE] Sending event #{Count}: {Type}", eventCount, evt.Type);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
    
    logger.LogInformation("[SSE] Stream completed for run {RunId}, sent {Count} events", runId, eventCount);
});

app.Run();

