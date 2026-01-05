using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.CQRS;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Notebook.Api.Contracts;
using Aevatar.Notebook.Api.Sessions;
using Aevatar.Notebook;
using Aevatar.Notebook.Agents;
using Aevatar.Notebook.Context;
using Aevatar.Notebook.Cqrs;
using Aevatar.Notebook.Persistence;
using Aevatar.Notebook.Reports;
using Aevatar.Notebook.Sources;
using Aevatar.Notebook.Streaming;
using Aevatar.Notebook.Tracing;
using Microsoft.Extensions.Options;

// ============================================================
//  Aevatar.Notebook (MVP)
//
//  UI layout (NotebookLM-like):
//  - Left: sources/memory management
//  - Middle: Q&A chat
//  - Right: report generation
//
//  Memory layers usage:
//  - L1/L2: State.History + history_summary (AIGAgentBase)
//  - L3: CQRS read model (in-memory for now)
//  - L4: IMemoryStore (sources + conversation entries)
//  - L4.1: IMemoryVectorIndex (optional embeddings)
//  - L4.2: IMemoryGraphStore (ExecutionTrace -> MemoryGraph projection is wired in core)
// ============================================================

var builder = WebApplication.CreateBuilder(args);

// Load local secrets file (gitignored) for quick setup.
builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: true);

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Persistence selection (config-driven)
var persistence = NotebookPersistence.Configure(builder.Services, builder.Configuration);

// Aevatar core + Local runtime
builder.Services.AddAevatarAgentSystem(persistence.ConfigureStores, b => b.UseLocalRuntime());
builder.Services.AddMEAI();

// CQRS (MVP): in-memory index + projector + query service
builder.Services.AddSingleton<IStateIndexService, InMemoryStateIndexService>();
builder.Services.AddSingleton<IStateProjector, InMemoryStateProjector>();
builder.Services.AddSingleton<IStateQueryService, StateQueryService>();

builder.Services.AddSingleton<NotebookRuntime>();
builder.Services.AddSingleton<NotebookSessionManager>();

// Notebook domain services
builder.Services.AddSingleton<SourceChunker>();
builder.Services.AddSingleton<SourceIndexer>();
builder.Services.AddSingleton<NotebookContextBuilder>();
builder.Services.AddSingleton<ReportPipeline>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ============================================================
//  APIs
// ============================================================

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/api/info", async (
    NotebookRuntime runtime,
    IOptions<LLMProvidersConfig> llm,
    NotebookPersistence.NotebookPersistenceSelection selection,
    CancellationToken ct) =>
{
    var status = await runtime.GetStatusAsync(ct);

    var defaultProvider = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
    llm.Value.Providers.TryGetValue(defaultProvider, out var defaultCfg);

    return Results.Json(new
    {
        agentId = status.AgentId,
        isReady = status.IsReady,
        lastError = status.LastError,
        llmDefaultProvider = llm.Value.Default,
        llm = new
        {
            @default = defaultProvider,
            providers = llm.Value.Providers.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            provider = defaultCfg == null
                ? null
                : new
                {
                    name = defaultProvider,
                    providerType = defaultCfg.ProviderType,
                    model = defaultCfg.Model,
                    endpoint = defaultCfg.Endpoint ?? "",
                    timeoutMilliseconds = defaultCfg.TimeoutMilliseconds
                }
        },
        persistence = new
        {
            providers = new
            {
                memoryStore = selection.MemoryStoreProvider,
                memoryVectorIndex = selection.MemoryVectorIndexProvider,
                memoryGraph = selection.MemoryGraphProvider
            },
            types = new
            {
                memoryStore = selection.MemoryStoreType ?? "default(file)",
                memoryVectorIndex = selection.MemoryVectorIndexType ?? "default(file)",
                memoryGraphStore = selection.MemoryGraphStoreType ?? "default(file)"
            }
        }
    });
});

// Sessions API (AG-UI)
app.MapNotebookSessionsApi();

// Sources API (create/upload/list/get)
app.MapSourceApi();

// Reports query API (list/get)
app.MapReportApi();

// Reports generation API (streaming)
app.MapReportStreamApi();

// ============================================================
//  Chat API (streaming)
//
//  Response format: NDJSON (one JSON object per line)
//    - { type: "meta", ... }   (first line)
//    - { type: "delta", content: "..." } (many lines)
//    - { type: "done" }        (last line)
//    - { type: "error", error: "..." }  (best-effort)
// ============================================================
app.MapPost("/api/chat/stream", async (
    HttpContext http,
    ChatInDto input,
    NotebookRuntime runtime,
    NotebookContextBuilder contextBuilder,
    IExecutionTraceStore traceStore,
    IOptions<LLMProvidersConfig> llm,
    ILogger<NotebookRuntime> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Message))
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        await http.Response.WriteAsJsonAsync(new { error = "message is required" }, cancellationToken: ct);
        return;
    }

    var startedAtUtc = DateTime.UtcNow;
    var executionId = $"notebook-chat-{Guid.NewGuid():N}";

    NotebookContextBuildResult? ctxResult = null;
    ChatResponse? resp = null;
    string? error = null;

    var requestId = input.RequestId ?? Guid.NewGuid().ToString("N");
    string? agentId = null;

    // Streamed content buffer (for trace + best-effort persistence).
    var assistant = new StringBuilder(capacity: 1024);

    // Pre-create JSON options (camelCase) so frontend can parse consistent fields.
    var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    try
    {
        var (agent, aid) = await runtime.GetAgentAsync(ct);
        agentId = aid;

        ctxResult = await contextBuilder.BuildAsync(
            new NotebookContextBuildRequest
            {
                Query = input.Message.Trim(),
                SelectedSourceIds = input.SelectedSourceIds
            },
            ct: ct);

        var request = new ChatRequest
        {
            Message = input.Message.Trim(),
            RequestId = requestId,
            StageHint = input.StageHint ?? ""
        };

        // Attach execution_id for correlation (even if MemoryStore scope isn't execution by default).
        request.Context["execution_id"] = executionId;

        // Feed full notebook context into LLM each turn.
        request.Context[NotebookAgent.NotebookContextKey] = ctxResult.Rendered;

        var citations = ctxResult.Context.Slices
            .Where(s => s.Kind == Aevatar.Notebook.Contracts.NotebookContextSliceKind.SourceChunk)
            .Select(s => new
            {
                sourceId = s.SourceId,
                chunkId = s.ChunkId,
                score = s.Score,
                reason = s.Reason,
                preview = string.IsNullOrWhiteSpace(s.Content) ? "" : (s.Content.Length <= 180 ? s.Content : s.Content[..180])
            })
            .ToList();

        // ------------------------------------------------------------
        //  Start streaming response
        // ------------------------------------------------------------
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.Headers.ContentType = "application/x-ndjson; charset=utf-8";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        // Ensure headers are sent ASAP.
        await http.Response.StartAsync(ct);

        await using var writer = new StreamWriter(
            http.Response.Body,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 16 * 1024,
            leaveOpen: true);

        // 1) meta
        await writer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            type = "meta",
            agentId = agentId ?? string.Empty,
            requestId,
            executionId,
            context = new
            {
                sourceIds = ctxResult.SourceIds,
                strategy = ctxResult.Context.Tags.TryGetValue("strategy", out var st) ? st : "",
                chunkSlices = citations.Count,
                renderedChars = (ctxResult.Rendered ?? string.Empty).Length
            },
            citations
        }, json));
        await writer.FlushAsync();

        // Bind tool progress events to this streaming request (best-effort).
        var prevSink = NotebookStreamEventContext.Current;
        NotebookStreamEventContext.Current = new NdjsonNotebookStreamEventSink(writer, json);
        try
        {
            // 2) delta stream
            var supportsStreaming = await agent.SupportsStreamingAsync(ct);
            if (!supportsStreaming)
            {
                // Fallback: emit as single delta.
                resp = await agent.ChatAsync(request, ct);
                assistant.Append(resp.Content ?? string.Empty);

                await writer.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    type = "delta",
                    content = resp.Content ?? string.Empty
                }, json));
                await writer.FlushAsync();
            }
            else
            {
                await foreach (var chunk in agent.ChatStreamAsync(request, ct))
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(chunk))
                        continue;

                    assistant.Append(chunk);

                    await writer.WriteLineAsync(JsonSerializer.Serialize(new
                    {
                        type = "delta",
                        content = chunk
                    }, json));
                    await writer.FlushAsync();
                }
            }
        }
        finally
        {
            NotebookStreamEventContext.Current = prevSink;
        }

        // 3) done
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "done" }, json));
        await writer.FlushAsync();

        // Synthesized response for tracing.
        resp ??= new ChatResponse { Content = assistant.ToString(), RequestId = requestId };
    }
    catch (OperationCanceledException ex)
    {
        // If the HTTP request was aborted (browser closed/reloaded), treat as normal cancellation
        // and avoid noisy "timeout" errors.
        if (ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "[Notebook] Chat(stream) canceled by client: {Message}", ex.Message);
            return;
        }

        var providerName = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
        llm.Value.Providers.TryGetValue(providerName, out var cfg);
        var timeoutMs = cfg?.TimeoutMilliseconds ?? 0;

        error =
            $"LLM 请求超时/被取消（provider={providerName}, model={cfg?.Model ?? ""}, timeoutMs={timeoutMs}）。" +
            "请检查网络/Endpoint 是否可达，或在配置里调大 `LLMProviders:Providers:<provider>:TimeoutMilliseconds`。";

        logger.LogWarning(ex, "[Notebook] Chat(stream) canceled/timeout: {Message}", ex.Message);

        // Best-effort: if response already started, send an error event.
        if (http.Response.HasStarted)
        {
            try
            {
                await using var writer = new StreamWriter(
                    http.Response.Body,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 4 * 1024,
                    leaveOpen: true);

                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "error", error }, json));
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "done" }, json));
                await writer.FlushAsync();
            }
            catch
            {
                // ignore
            }
        }
        else
        {
            http.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await http.Response.WriteAsJsonAsync(new { error = "chat failed", detail = error }, cancellationToken: ct);
        }
    }
    catch (Exception ex)
    {
        error = ex.Message;
        logger.LogError(ex, "[Notebook] Chat(stream) failed: {Message}", ex.Message);

        // Best-effort: if response already started, send an error event.
        if (http.Response.HasStarted)
        {
            try
            {
                await using var writer = new StreamWriter(
                    http.Response.Body,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 4 * 1024,
                    leaveOpen: true);

                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "error", error }, json));
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { type = "done" }, json));
                await writer.FlushAsync();
            }
            catch
            {
                // ignore
            }
        }
        else
        {
            http.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await http.Response.WriteAsJsonAsync(new { error = "chat failed", detail = error }, cancellationToken: ct);
        }
    }
    finally
    {
        var endedAtUtc = DateTime.UtcNow;

        try
        {
            // Use streamed buffer as best-effort response for trace.
            var trace = NotebookTraceBuilder.BuildChatTrace(new NotebookChatTraceData
            {
                ExecutionId = executionId,
                AgentId = agentId ?? string.Empty,
                RequestId = requestId,
                Query = input.Message.Trim(),
                SourceIds = ctxResult?.SourceIds ?? new List<string>(),
                Context = ctxResult?.Context,
                RenderedContext = ctxResult?.Rendered,
                Response = resp ?? new ChatResponse { Content = assistant.ToString(), RequestId = requestId },
                Error = error,
                LlmProvider = llm.Value.Default,
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc
            });

            await traceStore.SaveAsync(trace, ct);
        }
        catch (Exception ex)
        {
            // Best-effort: never fail chat because trace persistence/projection failed.
            logger.LogDebug(ex, "[Notebook] Trace save failed (best-effort).");
        }
    }
});

app.MapPost("/api/chat", async (
    ChatInDto input,
    NotebookRuntime runtime,
    NotebookContextBuilder contextBuilder,
    IExecutionTraceStore traceStore,
    IOptions<LLMProvidersConfig> llm,
    ILogger<NotebookRuntime> logger,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Message))
        return Results.BadRequest(new { error = "message is required" });

    var startedAtUtc = DateTime.UtcNow;
    var executionId = $"notebook-chat-{Guid.NewGuid():N}";

    ChatResponse? resp = null;
    NotebookContextBuildResult? ctxResult = null;
    string? error = null;
    var errorStatus = StatusCodes.Status500InternalServerError;

    string? requestId = null;
    string? agentId = null;

    try
    {
        var (agent, aid) = await runtime.GetAgentAsync(ct);
        agentId = aid;

        ctxResult = await contextBuilder.BuildAsync(
            new NotebookContextBuildRequest
            {
                Query = input.Message.Trim(),
                SelectedSourceIds = input.SelectedSourceIds
            },
            ct: ct);

        var request = new ChatRequest
        {
            Message = input.Message.Trim(),
            RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
            StageHint = input.StageHint ?? ""
        };
        requestId = request.RequestId;

        // Attach execution_id for correlation (even if MemoryStore scope isn't execution by default).
        request.Context["execution_id"] = executionId;

        // Feed full notebook context into LLM each turn.
        request.Context[NotebookAgent.NotebookContextKey] = ctxResult.Rendered;

        resp = await agent.ChatAsync(request, ct);
    }
    catch (OperationCanceledException ex)
    {
        // Includes timeouts (provider/network) and client disconnects.
        errorStatus = StatusCodes.Status504GatewayTimeout;
        var providerName = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
        llm.Value.Providers.TryGetValue(providerName, out var cfg);
        var timeoutMs = cfg?.TimeoutMilliseconds ?? 0;

        error =
            $"LLM 请求超时/被取消（provider={providerName}, model={cfg?.Model ?? ""}, timeoutMs={timeoutMs}）。" +
            "请检查网络/Endpoint 是否可达，或在配置里调大 `LLMProviders:Providers:<provider>:TimeoutMilliseconds`。";

        logger.LogWarning(ex, "[Notebook] Chat canceled/timeout: {Message}", ex.Message);
    }
    catch (Exception ex)
    {
        error = ex.Message;
        logger.LogError(ex, "[Notebook] Chat failed: {Message}", ex.Message);
    }
    finally
    {
        var endedAtUtc = DateTime.UtcNow;

        try
        {
            var trace = NotebookTraceBuilder.BuildChatTrace(new NotebookChatTraceData
            {
                ExecutionId = executionId,
                AgentId = agentId ?? string.Empty,
                RequestId = requestId ?? string.Empty,
                Query = input.Message.Trim(),
                SourceIds = ctxResult?.SourceIds ?? new List<string>(),
                Context = ctxResult?.Context,
                RenderedContext = ctxResult?.Rendered,
                Response = resp,
                Error = error,
                LlmProvider = llm.Value.Default,
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc
            });

            await traceStore.SaveAsync(trace, ct);
        }
        catch (Exception ex)
        {
            // Best-effort: never fail chat because trace persistence/projection failed.
            logger.LogDebug(ex, "[Notebook] Trace save failed (best-effort).");
        }
    }

    if (!string.IsNullOrWhiteSpace(error))
    {
        return Results.Problem(
            title: "Chat failed",
            detail: error,
            statusCode: errorStatus);
    }

    var citations = ctxResult!.Context.Slices
        .Where(s => s.Kind == Aevatar.Notebook.Contracts.NotebookContextSliceKind.SourceChunk)
        .Select(s => new
        {
            sourceId = s.SourceId,
            chunkId = s.ChunkId,
            score = s.Score,
            reason = s.Reason,
            preview = string.IsNullOrWhiteSpace(s.Content) ? "" : (s.Content.Length <= 180 ? s.Content : s.Content[..180])
        })
        .ToList();

    return Results.Json(new
    {
        agentId = agentId ?? string.Empty,
        requestId = requestId ?? string.Empty,
        executionId,
        content = resp!.Content,
        toolCalled = resp.ToolCalled,
        usage = resp.Usage == null
            ? null
            : new
            {
                promptTokens = resp.Usage.PromptTokens,
                completionTokens = resp.Usage.CompletionTokens,
                totalTokens = resp.Usage.TotalTokens,
                estimatedCost = resp.Usage.EstimatedCost
            },
        context = new
        {
            sourceIds = ctxResult.SourceIds,
            strategy = ctxResult.Context.Tags.TryGetValue("strategy", out var st) ? st : "",
            chunkSlices = citations.Count,
            renderedChars = (ctxResult.Rendered ?? string.Empty).Length
        },
        citations
    });
});

app.MapPost("/api/report", async (
    ReportInDto input,
    NotebookRuntime runtime,
    NotebookContextBuilder contextBuilder,
    ReportPipeline pipeline,
    IExecutionTraceStore traceStore,
    IOptions<LLMProvidersConfig> llm,
    ILogger<NotebookRuntime> logger,
    CancellationToken ct) =>
{
    var topic = string.IsNullOrWhiteSpace(input.Topic) ? "Untitled Report" : input.Topic.Trim();
    var startedAtUtc = DateTime.UtcNow;
    var executionId = $"notebook-report-{Guid.NewGuid():N}";

    ReportPipelineResult? report = null;
    NotebookContextBuildResult? ctxResult = null;
    string? error = null;
    var errorStatus = StatusCodes.Status500InternalServerError;

    string? agentId = null;

    try
    {
        var (agent, aid) = await runtime.GetAgentAsync(ct);
        agentId = aid;

        ctxResult = await contextBuilder.BuildAsync(
            new NotebookContextBuildRequest
            {
                Query = topic,
                SelectedSourceIds = input.SelectedSourceIds
            },
            ct: ct);

        var chunkIds = ctxResult.Context.Slices
            .Where(s => s.Kind == Aevatar.Notebook.Contracts.NotebookContextSliceKind.SourceChunk)
            .Select(s => (s.ChunkId ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        report = await pipeline.GenerateAsync(
            new ReportPipelineRequest
            {
                ChatAsync = agent.ChatAsync,
                NotebookContext = ctxResult.Rendered,
                SourceIds = ctxResult.SourceIds,
                CitationChunkIds = chunkIds,
                Topic = topic,
                ReportId = input.ReportId,
                ExecutionId = executionId
            },
            ct);
    }
    catch (OperationCanceledException ex)
    {
        errorStatus = StatusCodes.Status504GatewayTimeout;
        var providerName = string.IsNullOrWhiteSpace(llm.Value.Default) ? "default" : llm.Value.Default;
        llm.Value.Providers.TryGetValue(providerName, out var cfg);
        var timeoutMs = cfg?.TimeoutMilliseconds ?? 0;

        error =
            $"LLM 请求超时/被取消（provider={providerName}, model={cfg?.Model ?? ""}, timeoutMs={timeoutMs}）。" +
            "请检查网络/Endpoint 是否可达，或在配置里调大 `LLMProviders:Providers:<provider>:TimeoutMilliseconds`。";

        logger.LogWarning(ex, "[Notebook] Report canceled/timeout: {Message}", ex.Message);
    }
    catch (Exception ex)
    {
        error = ex.Message;
        logger.LogError(ex, "[Notebook] Report failed: {Message}", ex.Message);
    }
    finally
    {
        var endedAtUtc = DateTime.UtcNow;

        // Best-effort trace save (auto graph projection)
        try
        {
            var trace = NotebookTraceBuilder.BuildReportTrace(new NotebookReportTraceData
            {
                ExecutionId = executionId,
                AgentId = agentId ?? string.Empty,
                ReportId = report?.ReportId ?? (input.ReportId ?? string.Empty),
                Version = report?.Version ?? 0,
                Topic = topic,
                SourceIds = ctxResult?.SourceIds ?? new List<string>(),
                Context = ctxResult?.Context,
                RenderedContext = ctxResult?.Rendered,
                Outline = report?.Outline ?? string.Empty,
                Draft = report?.Draft ?? string.Empty,
                Content = report?.Content ?? string.Empty,
                Error = error,
                LlmProvider = llm.Value.Default,
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc
            });

            await traceStore.SaveAsync(trace, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[Notebook] Report trace save failed (best-effort).");
        }
    }

    if (!string.IsNullOrWhiteSpace(error))
    {
        return Results.Problem(
            title: "Report generation failed",
            detail: error,
            statusCode: errorStatus);
    }

    var citations = ctxResult!.Context.Slices
        .Where(s => s.Kind == Aevatar.Notebook.Contracts.NotebookContextSliceKind.SourceChunk)
        .Select(s => new
        {
            sourceId = s.SourceId,
            chunkId = s.ChunkId,
            score = s.Score,
            reason = s.Reason,
            preview = string.IsNullOrWhiteSpace(s.Content) ? "" : (s.Content.Length <= 180 ? s.Content : s.Content[..180])
        })
        .ToList();

    return Results.Json(new
    {
        agentId = agentId ?? string.Empty,
        executionId,
        reportId = report!.ReportId,
        version = report.Version,
        topic = report.Topic,
        content = report.Content,
        context = new
        {
            sourceIds = ctxResult.SourceIds,
            strategy = ctxResult.Context.Tags.TryGetValue("strategy", out var st) ? st : "",
            chunkSlices = citations.Count,
            renderedChars = (ctxResult.Rendered ?? string.Empty).Length
        },
        citations
    });
});

app.MapPost("/api/reset", async (NotebookRuntime runtime, CancellationToken ct) =>
{
    var status = await runtime.ResetAsync(ct);
    return Results.Json(status);
});

app.Run();
