using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.AI.LLMTornado;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Maker;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using HealthChecks.UI.Client;
using MakerSystem;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddAevatarUserSecrets()
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

// -------------------------------------------------------------------------
// OpenTelemetry Configuration - Enables Aspire Dashboard integration
// -------------------------------------------------------------------------
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var serviceName = "maker-system";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            // Filter out LLM API calls - they use HTTP streaming which doesn't close spans properly
            .AddHttpClientInstrumentation(options =>
            {
                options.FilterHttpRequestMessage = request =>
                {
                    var host = request.RequestUri?.Host ?? "";
                    // Skip LLM API endpoints (they use streaming which breaks span completion)
                    if (host.Contains("deepseek.com") || 
                        host.Contains("dashscope.aliyuncs.com") ||
                        host.Contains("openai.com") ||
                        host.Contains("anthropic.com"))
                    {
                        return false; // Don't trace these
                    }
                    return true;
                };
            })
            .AddSource("Aevatar.Agents.*")     // Capture agent traces
            .AddSource("Aevatar.Agents.LLM");  // Capture LLM streaming traces (our custom spans)
        
        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Aevatar.Agents.*")      // Capture agent metrics
            .AddMeter("Aevatar.Agents.LLM");   // Capture LLM metrics (TTFT, tokens, etc.)
        
        if (!string.IsNullOrEmpty(otlpEndpoint))
            metrics.AddOtlpExporter();
    });

// OpenTelemetry Logging
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;
    
    if (!string.IsNullOrEmpty(otlpEndpoint))
        logging.AddOtlpExporter();
});

// Health checks
builder.Services.AddHealthChecks();

// Add MassTransit Stream Plugin (supports Kafka/RabbitMQ/InMemory)
// Scans assemblies for [StreamTopic] attributes to auto-configure topic routing
builder.Services.AddMassTransitStreamPlugin(
    builder.Configuration,
    typeof(Aevatar.Agents.Maker.Agents.MakerCoordinatorGAgent).Assembly
);

// Add Aevatar Local Runtime (provides IGAgentActorFactory)
builder.Services.AddAevatarAgentSystem(aevatar => aevatar.UseLocalRuntime());

builder.Services.AddMEAI();

// Add MAKER - Agent-based execution with Worker Agents
builder.Services.AddMakerSystem();

// Project service
builder.Services.AddSingleton<MakerProjectService>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Health check endpoint for Aspire
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Static files
app.UseDefaultFiles();
app.UseStaticFiles();

// API endpoints
app.MapGet("/api/projects", (MakerProjectService svc) =>
    Results.Json(svc.GetProjects()));

// Create dynamic project from JSON config (zero-code)
app.MapPost("/api/projects/create", async (HttpContext ctx, MakerProjectService svc) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var configJson = await reader.ReadToEndAsync();
    return Results.Json(svc.CreateProjectFromConfig(configJson));
});

// Delete dynamic project
app.MapDelete("/api/projects/{projectId}", (string projectId, MakerProjectService svc) =>
    Results.Json(new { success = svc.DeleteProject(projectId) }));

// Get config templates for creating projects
app.MapGet("/api/projects/templates", () =>
    Results.Json(MakerProjectService.GetConfigTemplates()));

app.MapPost("/api/projects/{projectId}/run", async (string projectId, MakerProjectService svc, CancellationToken ct) =>
    Results.Json(await svc.StartRunAsync(projectId, ct)));

app.MapGet("/api/projects/{projectId}/status", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetStatus(projectId)));

app.MapGet("/api/projects/{projectId}/snapshot", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetSnapshot(projectId)));

app.MapGet("/api/projects/{projectId}/timeline", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetTimeline(projectId)));

// Files endpoint - list all generated files
app.MapGet("/api/projects/{projectId}/files", (string projectId, MakerProjectService svc) =>
    Results.Json(svc.GetFiles(projectId)));

// Get file content
app.MapGet("/api/projects/{projectId}/files/{category}/{name}", (string projectId, string category, string name, MakerProjectService svc) =>
{
    var content = svc.GetFileContent(projectId, category, name);
    if (content == null) return Results.NotFound();
    return Results.Text(content, "text/markdown");
});

// SSE endpoint for real-time streaming
app.MapGet("/api/projects/{projectId}/events", async (
    string projectId, 
    MakerProjectService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";
    
    await foreach (var evt in svc.GetEventStreamAsync(projectId, ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(evt);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});

app.Run();
