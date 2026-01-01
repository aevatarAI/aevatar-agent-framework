using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Cognitive.DependencyInjection;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.MongoDB.GAgent.DependencyInjection;
using Aevatar.Agents.Persistence.Neo4j.Graph.DependencyInjection;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using Aevatar.AxiomReasoning.Services;
using Aevatar.AxiomReasoning.LlmRecorder;
using Aevatar.AxiomReasoning.Graph;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Aevatar.Agents.Persistence.Neo4j;

// ============================================================
//  AXIOM REASONING
//  多智能体公理推理平台 - 基于 Cognitive Mesh + axiom_reasoning.yaml
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
//  Configuration
// ─────────────────────────────────────────────────────────────
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.Configure<SupabaseConfig>(builder.Configuration.GetSection(SupabaseConfig.SectionName));
builder.Services.Configure<LlmTranscriptOptions>(builder.Configuration.GetSection(LlmTranscriptOptions.SectionName));

// ─────────────────────────────────────────────────────────────
//  OpenTelemetry (Aspire 集成)
// ─────────────────────────────────────────────────────────────
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var serviceName = "axiom-reasoning";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation(options =>
            {
                options.FilterHttpRequestMessage = request =>
                {
                    var host = request.RequestUri?.Host ?? "";
                    return !host.Contains("deepseek.com") &&
                           !host.Contains("dashscope.aliyuncs.com") &&
                           !host.Contains("openai.com") &&
                           !host.Contains("anthropic.com");
                };
            })
            .AddSource("Aevatar.Agents.*")
            .AddSource("Aevatar.AxiomReasoning.*");

        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Aevatar.Agents.*")
            .AddMeter("Aevatar.AxiomReasoning.*");

        if (!string.IsNullOrEmpty(otlpEndpoint))
            metrics.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeScopes = true;
    logging.IncludeFormattedMessage = true;

    if (!string.IsNullOrEmpty(otlpEndpoint))
        logging.AddOtlpExporter();
});

// ─────────────────────────────────────────────────────────────
//  Health Checks
// ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ─────────────────────────────────────────────────────────────
//  Agent Framework Infrastructure
// ─────────────────────────────────────────────────────────────
builder.Services.AddMassTransitStreamPlugin(
    builder.Configuration,
    typeof(Aevatar.Agents.Cognitive.Agents.CognitiveCoordinatorGAgent).Assembly
);
builder.Services.AddAevatarLocalRuntime();
builder.Services.AddMEAI();
builder.Services.AddCognitiveAgents();
builder.Services.AddSingleton<Aevatar.CognitiveMesh.Strategies.CognitiveStrategy>();

// ─────────────────────────────────────────────────────────────
//  Optional: MongoDB persistence (StateStore)
//
//  WHY:
//  - State.History must survive process restart to support truly stateless frontend.
// ─────────────────────────────────────────────────────────────
var mongoConn =
    builder.Configuration["MongoDB:ConnectionString"] ??
    builder.Configuration["MONGODB_CONNECTION_STRING"] ??
    builder.Configuration["AEVATAR_MONGODB_CONNECTION_STRING"];

var mongoDb =
    builder.Configuration["MongoDB:Database"] ??
    builder.Configuration["MONGODB_DATABASE"] ??
    "aevatar";

if (!string.IsNullOrWhiteSpace(mongoConn))
{
    builder.Services.AddAevatarMongoDB(mongoConn!, mongoDb);
    builder.Services.AddMongoDBStateStore<AevatarAIAgentState>();
}

// ─────────────────────────────────────────────────────────────
//  Axiom Reasoning Services
// ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AxiomReasoningEventBridge>();
builder.Services.AddSingleton<AxiomReasoningService>();
builder.Services.AddSingleton<LlmTranscriptRecorder>();
builder.Services.AddSingleton<InMemoryGraphStore>();        // InMemory fallback

// Optional: Neo4j graph persistence (unified IGraphClient)
var neo4jEnabled = builder.Configuration.GetValue<bool?>("Neo4j:Enabled") ?? false;
var neo4jUri = builder.Configuration["Neo4j:Uri"];
var neo4jUser = builder.Configuration["Neo4j:Username"];
var neo4jPass = builder.Configuration["Neo4j:Password"];
var neo4jDb = builder.Configuration["Neo4j:Database"] ?? "neo4j";

var useNeo4jGraph =
    neo4jEnabled &&
    !string.IsNullOrWhiteSpace(neo4jUri) &&
    !string.IsNullOrWhiteSpace(neo4jUser) &&
    !string.IsNullOrWhiteSpace(neo4jPass);

if (useNeo4jGraph)
{
    builder.Services.AddAevatarGraphNeo4j(neo4jUri!, neo4jUser!, neo4jPass!, neo4jDb);
    builder.Services.AddSingleton<Neo4jGraphStore>();
}

builder.Services.AddSingleton<IGraphStore>(sp =>
{
    if (useNeo4jGraph)
        return sp.GetRequiredService<Neo4jGraphStore>();
    return sp.GetRequiredService<InMemoryGraphStore>();
});
builder.Services.AddSingleton<SupabaseService>();

// ─────────────────────────────────────────────────────────────
//  Logging
// ─────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Supabase (best-effort init; no-op if not enabled)
await app.Services.GetRequiredService<SupabaseService>().InitializeAsync();
// await app.Services.GetRequiredService<SupabaseGraphStore>().InitializeAsync();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.UseDefaultFiles();
app.UseStaticFiles();

// ─────────────────────────────────────────────────────────────
//  API Endpoints
// ─────────────────────────────────────────────────────────────

app.MapGet("/api/sessions", (AxiomReasoningService svc) =>
    Results.Json(svc.GetSessions()));

// Available Cognitive DSL workflows (for UI dropdown)
app.MapGet("/api/workflows", (Aevatar.CognitiveMesh.Strategies.CognitiveStrategy strategy) =>
    Results.Json(strategy.GetAvailableWorkflows()));

// GraphStore diagnostics (which backend is active + table status)
app.MapGet("/api/graphstore/diagnostics", (IGraphStore store) =>
{
    return store switch
    {
        Neo4jGraphStore n => Results.Json(n.GetDiagnostics()),
        InMemoryGraphStore m => Results.Json(m.GetDiagnostics()),
        _ => Results.Json(new { type = store.GetType().Name })
    };
});

// Graph DB (DAG) APIs
app.MapGet("/api/sessions/{sessionId}/dag", async (string sessionId, IGraphStore store, CancellationToken ct) =>
    Results.Json(await store.GetSnapshotAsync(sessionId, ct)));

app.MapGet("/api/sessions/{sessionId}/dag/{nodeId}", async (string sessionId, string nodeId, IGraphStore store, CancellationToken ct) =>
    Results.Json(await store.ExplainAsync(sessionId, nodeId, ct)));

app.MapPost("/api/sessions", async (HttpContext ctx, AxiomReasoningService svc) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync();
    return Results.Json(await svc.CreateSessionAsync(json));
});

app.MapPost("/api/sessions/{sessionId}/run", async (string sessionId, AxiomReasoningService svc, CancellationToken ct) =>
    Results.Json(await svc.StartAsync(sessionId, ct)));

app.MapPost("/api/sessions/{sessionId}/stop", (string sessionId, AxiomReasoningService svc) =>
    Results.Json(svc.Stop(sessionId)));

app.MapGet("/api/sessions/{sessionId}/status", (string sessionId, AxiomReasoningService svc) =>
    Results.Json(svc.GetStatus(sessionId)));

app.MapGet("/api/sessions/{sessionId}/result", (string sessionId, AxiomReasoningService svc) =>
    Results.Json(svc.GetResult(sessionId)));

app.MapGet("/api/sessions/{sessionId}/artifacts", (string sessionId, AxiomReasoningService svc) =>
    Results.Json(svc.GetArtifacts(sessionId)));

app.MapGet("/api/sessions/{sessionId}/artifacts/state", (string sessionId, AxiomReasoningService svc) =>
{
    if (!svc.TryGetFileContent(sessionId, "artifacts", "state.json", out var content))
        return Results.NotFound(new { success = false, error = "state.json not found" });

    var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? "");
    var fileName = $"{sessionId}-state.json";
    return Results.File(bytes, "application/json; charset=utf-8", fileDownloadName: fileName);
});

app.MapGet("/api/sessions/{sessionId}/artifacts/theorems", (string sessionId, AxiomReasoningService svc) =>
{
    if (!svc.TryGetFileContent(sessionId, "artifacts", "theorems.json", out var content))
        return Results.NotFound(new { success = false, error = "theorems.json not found" });

    var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? "");
    var fileName = $"{sessionId}-theorems.json";
    return Results.File(bytes, "application/json; charset=utf-8", fileDownloadName: fileName);
});

// LLM transcript artifacts (local files)
app.MapGet("/api/sessions/{sessionId}/llm/transcript", (string sessionId, AxiomReasoningService svc) =>
{
    if (!svc.TryReadOutputFileBytes(sessionId, "llm", "transcript.jsonl", out var bytes))
        return Results.NotFound(new { success = false, error = "transcript.jsonl not found" });

    var fileName = $"{sessionId}-transcript.jsonl";
    return Results.File(bytes, "application/x-ndjson; charset=utf-8", fileDownloadName: fileName);
});

app.MapGet("/api/sessions/{sessionId}/llm/review", (string sessionId, AxiomReasoningService svc) =>
{
    if (!svc.TryReadOutputFileBytes(sessionId, "llm", "review.md", out var bytes))
        return Results.NotFound(new { success = false, error = "review.md not found" });

    var fileName = $"{sessionId}-llm-review.md";
    return Results.File(bytes, "text/markdown; charset=utf-8", fileDownloadName: fileName);
});

// SSE 实时事件流
app.MapGet("/api/sessions/{sessionId}/events", async (
    string sessionId,
    AxiomReasoningService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    await foreach (var evt in svc.GetEventStreamAsync(sessionId, ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});

// AG-UI SSE 事件流（协议标准化）
// NOTE:
// - 保留 /events 作为 legacy（项目内 UI 兼容）
// - /agui/events 输出 AG-UI 事件 + CUSTOM 扩展，便于外部 UI/SDK 直接对接
app.MapGet("/api/sessions/{sessionId}/agui/events", async (
    string sessionId,
    AxiomReasoningService svc,
    HttpContext ctx,
    CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var jsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    // Flush throttling:
    // - Streaming tokens may produce high-frequency TEXT_MESSAGE_CONTENT events.
    // - Flushing on every event is expensive; flush frequently enough for UX, but not per token.
    var lastFlushAt = DateTime.UtcNow;
    const int flushIntervalMs = 50;

    await foreach (var evt in svc.GetAgUiEventStreamAsync(sessionId, ct))
    {
        var json = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);

        var now = DateTime.UtcNow;
        var isStreamingText = string.Equals(evt.Type, "TEXT_MESSAGE_CONTENT", StringComparison.Ordinal);
        var shouldFlush = !isStreamingText || (now - lastFlushAt).TotalMilliseconds >= flushIntervalMs;
        if (shouldFlush)
        {
            await ctx.Response.Body.FlushAsync(ct);
            lastFlushAt = now;
        }
    }

    // Always flush at the end (best-effort).
    await ctx.Response.Body.FlushAsync(ct);
});

Console.WriteLine($"""

    ╔══════════════════════════════════════════════════════════╗
    ║                   AXIOM REASONING                        ║
    ║                                                          ║
    ║  Web UI:  http://localhost:5001                          ║
    ║  Health:  http://localhost:5001/health                   ║
    ╚══════════════════════════════════════════════════════════╝
    """);

app.Run();


