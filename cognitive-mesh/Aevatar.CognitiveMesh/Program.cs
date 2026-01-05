using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Cognitive.DependencyInjection;
using Aevatar.Agents.CreativeReasoning;
using Aevatar.Agents.Maker;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using Aevatar.CognitiveMesh.Abstractions;
using Aevatar.CognitiveMesh.Abstractions.Tasks;
using Aevatar.CognitiveMesh.Services;
using Aevatar.CognitiveMesh.Strategies;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// ============================================================
//  COGNITIVE MESH
//  认知网格 - 统一的思维策略执行平台
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

// ─────────────────────────────────────────────────────────────
//  OpenTelemetry (Aspire 集成)
// ─────────────────────────────────────────────────────────────
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var serviceName = "cognitive-mesh";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation(options =>
            {
                // 过滤 LLM API 调用（使用流式传输，不适合 trace）
                options.FilterHttpRequestMessage = request =>
                {
                    var host = request.RequestUri?.Host ?? "";
                    if (host.Contains("deepseek.com") ||
                        host.Contains("dashscope.aliyuncs.com") ||
                        host.Contains("openai.com") ||
                        host.Contains("anthropic.com"))
                    {
                        return false;
                    }
                    return true;
                };
            })
            .AddSource("Aevatar.Agents.*")
            .AddSource("Aevatar.CognitiveMesh.*");

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
            .AddMeter("Aevatar.CognitiveMesh.*");

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

// MassTransit Stream Plugin (注册所有Agent程序集)
builder.Services.AddMassTransitStreamPlugin(
    builder.Configuration,
    typeof(Aevatar.Agents.Maker.Agents.MakerCoordinatorGAgent).Assembly,
    typeof(Aevatar.Agents.CreativeReasoning.Agents.UoTCoordinatorGAgent).Assembly,  // 包含 C/E/T-UoT
    typeof(Aevatar.Agents.Cognitive.Agents.CognitiveCoordinatorGAgent).Assembly      // DSL 驱动的认知系统
);

// Local Runtime
builder.Services.AddAevatarLocalRuntime();

// LLM Infrastructure (MEAI)
builder.Services.AddMEAI();

// MAKER System
builder.Services.AddMakerSystem();

// UoT Creative Reasoning
builder.Services.AddUoTCreativeReasoning(AevatarAgentsConstants.DefaultProviderName);

// Cognitive DSL System
builder.Services.AddCognitiveAgents();

// ─────────────────────────────────────────────────────────────
//  Cognitive Mesh Services
// ─────────────────────────────────────────────────────────────

// 策略注册
builder.Services.AddSingleton<DirectStrategy>();    // Direct: 最简单的直接调用
builder.Services.AddSingleton<MakerStrategy>();     // MAKER: 分解-共识-合成 (v1)
builder.Services.AddSingleton<UoTStrategy>();       // C-UoT: 组合式 (v1)
builder.Services.AddSingleton<EUoTStrategy>();      // E-UoT: 探索式 (v1)
builder.Services.AddSingleton<TUoTStrategy>();      // T-UoT: 变革式 (v1)
builder.Services.AddSingleton<CognitiveStrategy>(); // Cognitive: DSL 驱动 (v2)
builder.Services.AddSingleton<StrategyRegistry>();

// 内容加载器
builder.Services.AddSingleton<Aevatar.CognitiveMesh.Abstractions.Content.IContentLoader, Aevatar.CognitiveMesh.Services.ContentLoader>();

// 项目存储（YAML）
builder.Services.AddSingleton<ProjectStore>();

// 主服务
builder.Services.AddSingleton<CognitiveMeshService>();

// ─────────────────────────────────────────────────────────────
//  Logging
// ─────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

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

// 策略列表
app.MapGet("/api/strategies", (StrategyRegistry registry) =>
    Results.Json(registry.GetAll().Select(s => new
    {
        kind = s.Kind.ToString(),
        displayName = s.DisplayName,
        description = s.Description,
        // Cognitive 策略额外返回可用工作流
        availableWorkflows = s is CognitiveStrategy cs ? cs.GetAvailableWorkflows() : null
    })));

// 项目列表
app.MapGet("/api/projects", (CognitiveMeshService svc) =>
    Results.Json(svc.GetProjects()));

// 创建项目
app.MapPost("/api/projects", async (HttpContext ctx, CognitiveMeshService svc) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var configJson = await reader.ReadToEndAsync();
    return Results.Json(svc.CreateProject(configJson));
});

// 删除项目（实际是归档）
app.MapDelete("/api/projects/{projectId}", (string projectId, CognitiveMeshService svc) =>
    Results.Json(new { success = svc.DeleteProject(projectId) }));

// 获取归档项目列表
app.MapGet("/api/archive", (ProjectStore store) =>
    Results.Json(store.GetArchivedProjects().Select(x => new
    {
        fileName = x.FileName,
        id = x.Project.Id,
        name = x.Project.Name,
        description = x.Project.Description,
        icon = x.Project.Icon,
        strategy = x.Project.Strategy,
        archivedAt = x.Project.UpdatedAt
    })));

// 恢复归档项目
app.MapPost("/api/archive/{fileName}/restore", (string fileName, ProjectStore store) =>
    Results.Json(new { success = store.RestoreProject(fileName) }));

// ─────────────────────────────────────────────────────────────
//  文件上传与内容预览
// ─────────────────────────────────────────────────────────────

// 上传文件
app.MapPost("/api/upload", async (HttpContext ctx, CognitiveMeshService svc, CancellationToken ct) =>
{
    if (!ctx.Request.HasFormContentType)
    {
        return Results.BadRequest(new { success = false, error = "Expected multipart/form-data" });
    }

    var form = await ctx.Request.ReadFormAsync(ct);
    return Results.Json(await svc.UploadFilesAsync(form.Files, ct));
}).DisableAntiforgery();

// 预览内容（不执行）
app.MapPost("/api/preview-content", async (HttpContext ctx, CognitiveMeshService svc, CancellationToken ct) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var configJson = await reader.ReadToEndAsync();
    return Results.Json(await svc.PreviewContentAsync(configJson, ct));
});

// 获取任务模板列表
app.MapGet("/api/task-templates", () =>
{
    var templates = Enum.GetValues<Aevatar.CognitiveMesh.Abstractions.Tasks.TaskTemplate>()
        .Select(t => new
        {
            value = t.ToString(),
            displayName = t.GetDisplayName(),
            description = t.GetDescription(),
            requiresParameters = t.RequiresAdditionalParameters(),
            recommendedStrategy = t.GetRecommendedStrategy().ToString()
        });
    return Results.Json(templates);
});

// ─────────────────────────────────────────────────────────────
//  Ad-hoc Reasoning (for integration, e.g. TradeSystem)
// ─────────────────────────────────────────────────────────────

// Run a single reasoning task without creating a project.
// This endpoint is intentionally minimal: trade system can POST { task, strategyKind, cognitiveWorkflow }.
app.MapPost("/api/reason", async (
    ReasonRequest req,
    StrategyRegistry registry,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Task))
    {
        return Results.BadRequest(new { success = false, error = "Task is required." });
    }

    var kindText = string.IsNullOrWhiteSpace(req.StrategyKind) ? "Cognitive" : req.StrategyKind;
    if (!Enum.TryParse<StrategyKind>(kindText, ignoreCase: true, out var kind))
    {
        kind = StrategyKind.Cognitive;
    }

    var strategy = registry.Get(kind);
    if (strategy == null)
    {
        return Results.NotFound(new { success = false, error = $"Strategy not available: {kind}" });
    }

    var timeoutSeconds = req.TimeoutSeconds is > 0 ? req.TimeoutSeconds.Value : 30;

    var options = new ReasoningOptions
    {
        ProviderName = req.ProviderName,
        MaxLlmCalls = req.MaxLlmCalls ?? 200,
        MaxDuration = TimeSpan.FromSeconds(timeoutSeconds),
        StepTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        Context = req.Context,
        CognitiveWorkflow = req.CognitiveWorkflow
    };

    var result = await strategy.ExecuteAsync(req.Task, options, progress: null, ct);
    return Results.Json(result);
});

// ─────────────────────────────────────────────────────────────
//  运行管理
// ─────────────────────────────────────────────────────────────

// 启动运行
app.MapPost("/api/projects/{projectId}/run", async (string projectId, CognitiveMeshService svc, CancellationToken ct) =>
    Results.Json(await svc.StartRunAsync(projectId, ct)));

// 获取项目配置详情
app.MapGet("/api/projects/{projectId}/config", (string projectId, CognitiveMeshService svc) =>
    Results.Json(svc.GetProjectConfig(projectId)));

// 停止运行
app.MapPost("/api/projects/{projectId}/stop", (string projectId, CognitiveMeshService svc) =>
    Results.Json(svc.StopRun(projectId)));

// 获取状态
app.MapGet("/api/projects/{projectId}/status", (string projectId, CognitiveMeshService svc) =>
    Results.Json(svc.GetStatus(projectId)));

// 获取快照
app.MapGet("/api/projects/{projectId}/snapshot", (string projectId, CognitiveMeshService svc) =>
    Results.Json(svc.GetSnapshot(projectId)));

// 获取时间线
app.MapGet("/api/projects/{projectId}/timeline", (string projectId, CognitiveMeshService svc) =>
    Results.Json(svc.GetTimeline(projectId)));

// 获取运行历史
app.MapGet("/api/projects/{projectId}/runs", (string projectId, CognitiveMeshService svc) =>
    Results.Json(svc.GetRuns(projectId)));

// 获取特定运行的文件列表
app.MapGet("/api/projects/{projectId}/runs/{runId}/files", (string projectId, string runId, CognitiveMeshService svc) =>
    Results.Json(svc.GetRunFiles(projectId, runId)));

// 获取特定运行的文件内容
app.MapGet("/api/projects/{projectId}/runs/{runId}/files/{category}/{name}", (string projectId, string runId, string category, string name, CognitiveMeshService svc) =>
{
    var content = svc.GetRunFileContent(projectId, runId, category, name);
    if (content == null) return Results.NotFound();
    return Results.Text(content, "text/markdown");
});

// 获取特定运行的时间线
app.MapGet("/api/projects/{projectId}/runs/{runId}/timeline", (string projectId, string runId, CognitiveMeshService svc) =>
    Results.Json(svc.GetRunTimeline(projectId, runId)));

// 获取生成的文件列表（当前运行）
app.MapGet("/api/projects/{projectId}/files", (string projectId, CognitiveMeshService svc) =>
    Results.Json(svc.GetFiles(projectId)));

// 获取文件内容
app.MapGet("/api/projects/{projectId}/files/{category}/{name}", (string projectId, string category, string name, CognitiveMeshService svc) =>
{
    var content = svc.GetFileContent(projectId, category, name);
    if (content == null) return Results.NotFound();
    return Results.Text(content, "text/markdown");
});

// SSE 实时事件流
app.MapGet("/api/projects/{projectId}/events", async (
    string projectId,
    CognitiveMeshService svc,
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
    
    await foreach (var evt in svc.GetEventStreamAsync(projectId, ct))
    {
        // 使用 evt.GetType() 确保序列化派生类的所有属性
        var json = System.Text.Json.JsonSerializer.Serialize(evt, evt.GetType(), jsonOptions);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }
});

// 示例问题（UoT）
app.MapGet("/api/sample-problems", (CognitiveMeshService svc) =>
    Results.Json(svc.GetSampleProblems()));

app.Run();

// ============================================================
//  DTOs
// ============================================================

public sealed record ReasonRequest
{
    public string? StrategyKind { get; init; }
    public required string Task { get; init; }
    public string? CognitiveWorkflow { get; init; }
    public string? ProviderName { get; init; }
    public int? MaxLlmCalls { get; init; }
    public int? TimeoutSeconds { get; init; }
    public Dictionary<string, string>? Context { get; init; }
}
