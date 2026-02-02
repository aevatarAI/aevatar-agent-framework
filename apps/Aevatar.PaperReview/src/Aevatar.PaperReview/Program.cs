using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Cognitive.DependencyInjection;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using Aevatar.PaperReview.Prompty;
using Aevatar.PaperReview.Services;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// ============================================================
//  PAPER REVIEW
//  学术论文评审平台 - 基于 Cognitive Mesh
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
//  Configuration
// ─────────────────────────────────────────────────────────────
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddAevatarUserConfig()
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));
builder.Services.Configure<SupabaseConfig>(builder.Configuration.GetSection(SupabaseConfig.SectionName));

// ─────────────────────────────────────────────────────────────
//  OpenTelemetry (Aspire 集成)
// ─────────────────────────────────────────────────────────────
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var serviceName = "paper-review";

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
            .AddSource("Aevatar.PaperReview.*");

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
            .AddMeter("Aevatar.PaperReview.*");

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

// MassTransit Stream Plugin (仅 Cognitive Agents)
builder.Services.AddMassTransitStreamPlugin(
    builder.Configuration,
        typeof(Aevatar.Agents.Cognitive.Agents.WorkflowCoordinatorAgent).Assembly
);

// Local Runtime
builder.Services.AddAevatarLocalRuntime();

// LLM Infrastructure (MEAI)
builder.Services.AddMEAI();

// Cognitive DSL System
builder.Services.AddCognitiveAgents();
builder.Services.AddSingleton<Aevatar.Agents.Cognitive.Core.Strategies.CognitiveStrategy>();

// ─────────────────────────────────────────────────────────────
//  Paper Review Services
// ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<SupabaseService>();
builder.Services.AddSingleton<PaperUploadService>();
builder.Services.AddSingleton<PromptyLoader>();          // Prompty 解析器
builder.Services.AddSingleton<ReviewPromptProvider>();   // 混合 Prompty + Scriban
builder.Services.AddSingleton<ReviewEventBridge>();
builder.Services.AddSingleton<PaperReviewService>();

// ─────────────────────────────────────────────────────────────
//  Logging
// ─────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// ─────────────────────────────────────────────────────────────
//  Initialize Supabase
// ─────────────────────────────────────────────────────────────
var supabaseService = app.Services.GetRequiredService<SupabaseService>();
await supabaseService.InitializeAsync();

// ─────────────────────────────────────────────────────────────
//  Middleware
// ─────────────────────────────────────────────────────────────
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.UseDefaultFiles();
app.UseStaticFiles();

// ─────────────────────────────────────────────────────────────
//  API Endpoints
// ─────────────────────────────────────────────────────────────

// 获取评审会话列表
app.MapGet("/api/sessions", (PaperReviewService svc) =>
    Results.Json(svc.GetSessions()));

// 创建评审会话
app.MapPost("/api/sessions", async (HttpContext ctx, PaperReviewService svc) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var configJson = await reader.ReadToEndAsync();
    return Results.Json(await svc.CreateSessionAsync(configJson));
});

// 上传论文
app.MapPost("/api/upload", async (HttpContext ctx, PaperReviewService svc, CancellationToken ct) =>
{
    if (!ctx.Request.HasFormContentType)
        return Results.BadRequest(new { success = false, error = "Expected multipart/form-data" });

    var form = await ctx.Request.ReadFormAsync(ct);
    return Results.Json(await svc.UploadPaperAsync(form.Files, ct));
}).DisableAntiforgery();

// 开始评审
app.MapPost("/api/sessions/{sessionId}/review", async (string sessionId, PaperReviewService svc, CancellationToken ct) =>
    Results.Json(await svc.StartReviewAsync(sessionId, ct)));

// 停止评审
app.MapPost("/api/sessions/{sessionId}/stop", (string sessionId, PaperReviewService svc) =>
    Results.Json(svc.StopReview(sessionId)));

// 获取评审状态
app.MapGet("/api/sessions/{sessionId}/status", (string sessionId, PaperReviewService svc) =>
    Results.Json(svc.GetStatus(sessionId)));

// 获取评审结果
app.MapGet("/api/sessions/{sessionId}/result", (string sessionId, PaperReviewService svc) =>
    Results.Json(svc.GetResult(sessionId)));

// ─────────────────────────────────────────────────────────────
//  Deliverables (交付物下载)
// ─────────────────────────────────────────────────────────────

// 列出当前 session 已生成的文件
app.MapGet("/api/sessions/{sessionId}/artifacts", (string sessionId, PaperReviewService svc) =>
    Results.Json(svc.GetArtifacts(sessionId)));

// 下载 compose 后的最终报告（Markdown）
app.MapGet("/api/sessions/{sessionId}/artifacts/report", (string sessionId, PaperReviewService svc) =>
{
    if (!svc.TryGetFileContent(sessionId, "reports", "review_report.md", out var content))
        return Results.NotFound(new { success = false, error = "Report not found" });

    var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? "");
    var fileName = $"{sessionId}-review_report.md";
    return Results.File(bytes, "text/markdown; charset=utf-8", fileDownloadName: fileName);
});

// 下载评审明细（Atomic Points task + consensus）
app.MapGet("/api/sessions/{sessionId}/artifacts/details", (string sessionId, PaperReviewService svc) =>
{
    if (!svc.TryGetFileContent(sessionId, "details", "review_details.json", out var content))
        return Results.NotFound(new { success = false, error = "Details not found" });

    var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? "");
    var fileName = $"{sessionId}-review_details.json";
    return Results.File(bytes, "application/json; charset=utf-8", fileDownloadName: fileName);
});

// SSE 实时事件流
app.MapGet("/api/sessions/{sessionId}/events", async (
    string sessionId,
    PaperReviewService svc,
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

// 获取评审历史
app.MapGet("/api/sessions/{sessionId}/history", (string sessionId, PaperReviewService svc) =>
    Results.Json(svc.GetHistory(sessionId)));

// ─────────────────────────────────────────────────────────────
//  Supabase API Endpoints
// ─────────────────────────────────────────────────────────────

// 获取云端历史记录
app.MapGet("/api/cloud/reviews", async (SupabaseService svc, int limit = 50) =>
    Results.Json(await svc.GetReviewsAsync(limit)));

// 检查 Supabase 状态（含诊断信息）
app.MapGet("/api/cloud/status", (SupabaseService svc) =>
    Results.Json(svc.GetDiagnostics()));

var supabaseStatus = supabaseService.IsEnabled ? "✓ Enabled" : "✗ Disabled";
Console.WriteLine($"""
    
    ╔══════════════════════════════════════════════════════════╗
    ║                    PAPER REVIEW                          ║
    ║          学术论文评审平台 · Cognitive Mesh               ║
    ╠══════════════════════════════════════════════════════════╣
    ║  🌐 Web UI:  http://localhost:5002                       ║
    ║  📄 API:     http://localhost:5002/api/sessions          ║
    ║  ☁️  Supabase: {supabaseStatus,-42}║
    ╚══════════════════════════════════════════════════════════╝
    
    """);

app.Run();
