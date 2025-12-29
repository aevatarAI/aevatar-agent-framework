using MemoryDemo;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Core.CQRS;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Core.Memory;
using Aevatar.Agents.Core.MemoryGraphs;
using Aevatar.Agents.Core.Tracing;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.Supabase.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;

// ============================================================
//  MemoryDemo - Web UI + APIs
//
//  Demo focus:
//  - State.History (short-term window) replay
//  - Compaction: sliding window + State.Context["history_summary"]
//  - CQRS read-model: in-memory projection (OnStateChanged → projector → index)
//  - Tool recall: built-in tool `search_memory` (CQRS + state snapshot)
// ============================================================

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Aevatar core + Local runtime (includes: default MemoryStore/VectorIndex/TraceStore/GraphStore registrations)
builder.Services.AddAevatarAgentSystem(b => b.UseLocalRuntime());
builder.Services.AddMEAI();

// ============================================================
//  CQRS (Demo): in-memory projection + query
//
//  - This simulates: OnStateChangedAsync → IStateProjector → IStateIndexService
//  - So built-in tool `search_memory` can query projected state via IStateQueryService
// ============================================================
builder.Services.AddSingleton<IStateIndexService, InMemoryStateIndexService>();
builder.Services.AddSingleton<IStateProjector, InMemoryStateProjector>();
builder.Services.AddSingleton<IStateQueryService, StateQueryService>();
builder.Services.AddSingleton<MemoryDemoRuntime>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ============================================================
//  APIs
// ============================================================

// Knowledge base (book) APIs
MemoryDemoKnowledgeBaseApi.Map(app);

app.MapGet("/api/info", async (
    MemoryDemoRuntime runtime,
    IOptions<LLMProvidersConfig> llm,
    CancellationToken ct) =>
{
    var status = await runtime.GetStatusAsync(ct);
    var paths = MemoryDemoPaths.Get();
    return Results.Json(new
    {
        agentId = status.AgentId,
        isReady = status.IsReady,
        lastError = status.LastError,
        llmDefaultProvider = llm.Value.Default,
        settings = new
        {
            enableHistory = status.EnableChatHistoryInState,
            enableCompaction = status.EnableChatHistoryCompaction,
            chatHistoryMaxMessages = status.ChatHistoryMaxMessages,
            chatHistorySummaryMaxChars = status.ChatHistorySummaryMaxChars,
            enableMemoryStoreAppend = status.EnableMemoryStoreAppend,
            enableMemoryVectorIndexAppend = status.EnableMemoryVectorIndexAppend,
            allowInternalTools = status.AllowInternalTools,
            allowDangerousTools = status.AllowDangerousTools
        },
        paths = new
        {
            traceRoot = paths.TraceRoot,
            memoryRoot = paths.MemoryRoot,
            vectorRoot = paths.VectorRoot
        }
    });
});

app.MapPost("/api/chat", async (
    ChatInDto input,
    MemoryDemoRuntime runtime,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Message))
        return Results.BadRequest(new { error = "message is required" });

    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    var request = new ChatRequest
    {
        Message = input.Message.Trim(),
        RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
        StageHint = input.StageHint ?? ""
    };

    var response = await agent.ChatAsync(request, ct);
    var state = agent.GetState();
    state.Context.TryGetValue("history_summary", out var summary);

    return Results.Json(new
    {
        agentId,
        requestId = request.RequestId,
        content = response.Content,
        toolCalled = response.ToolCalled,
        toolCall = response.ToolCalled
            ? new
            {
                name = response.ToolCall?.ToolName,
                arguments = response.ToolCall?.Arguments,
                result = response.ToolCall?.Result
            }
            : null,
        usage = response.Usage == null ? null : new
        {
            promptTokens = response.Usage.PromptTokens,
            completionTokens = response.Usage.CompletionTokens,
            totalTokens = response.Usage.TotalTokens
        },
        memory = new
        {
            historyCount = state.History?.Count ?? 0,
            summary = summary ?? ""
        },
        longTerm = new
        {
            enableMemoryStoreAppend = agent.EnableMemoryStoreAppend,
            enableMemoryVectorIndexAppend = agent.EnableMemoryVectorIndexAppend,
            defaultMemoryId = MemoryDemoPaths.BuildDefaultAgentMemoryId(agent.Id)
        }
    });
});

// ============================================================
//  Streaming chat (NDJSON)
//
//  Response format (one JSON object per line):
//  - { type: "start", agentId, requestId }
//  - { type: "delta", content }
//  - { type: "end", agentId, requestId, toolCalled, toolCall?, memory?, longTerm? }
//
//  WHY:
//  - Simple to parse with fetch streaming (ReadableStream).
//  - Keeps room for metadata without requiring SSE/EventSource.
// ============================================================
app.MapPost("/api/chat/stream", async (
    ChatInDto input,
    MemoryDemoRuntime runtime,
    HttpResponse response,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Message))
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        await response.WriteAsync(JsonSerializer.Serialize(new { error = "message is required" }), ct);
        return;
    }

    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    var request = new ChatRequest
    {
        Message = input.Message.Trim(),
        RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
        StageHint = input.StageHint ?? ""
    };

    response.StatusCode = StatusCodes.Status200OK;
    response.ContentType = "application/x-ndjson; charset=utf-8";
    response.Headers.CacheControl = "no-cache";

    var startedAtUtc = DateTime.UtcNow;

    await WriteNdjsonAsync(response, new
    {
        type = "start",
        agentId,
        requestId = request.RequestId
    }, ct);

    var assistantText = new System.Text.StringBuilder();

    try
    {
        await foreach (var chunk in agent.ChatStreamAsync(request, ct))
        {
            assistantText.Append(chunk);
            await WriteNdjsonAsync(response, new { type = "delta", content = chunk }, ct);
        }
    }
    catch (Exception ex)
    {
        // Best-effort: emit an error event (client may already have partial output).
        await WriteNdjsonAsync(response, new { type = "error", error = ex.Message }, ct);
        return;
    }

    // Build best-effort meta from current agent state (tool calls are stored into State.History when enabled).
    var state = agent.GetState();
    state.Context.TryGetValue("history_summary", out var summary);

    var toolCall = TryExtractToolCallFromHistory(state, startedAtUtc);

    await WriteNdjsonAsync(response, new
    {
        type = "end",
        agentId,
        requestId = request.RequestId,
        content = assistantText.ToString(),
        toolCalled = toolCall != null,
        toolCall,
        memory = new
        {
            historyCount = state.History?.Count ?? 0,
            summary = summary ?? ""
        },
        longTerm = new
        {
            enableMemoryStoreAppend = agent.EnableMemoryStoreAppend,
            enableMemoryVectorIndexAppend = agent.EnableMemoryVectorIndexAppend,
            defaultMemoryId = MemoryDemoPaths.BuildDefaultAgentMemoryId(agent.Id)
        }
    }, ct);
});

app.MapPost("/api/seed", async (
    SeedInDto input,
    MemoryDemoRuntime runtime,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Text))
        return Results.BadRequest(new { error = "text is required" });

    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    await agent.SeedAsync(input.Text, ct);
    return Results.Json(new { agentId, ok = true });
});

app.MapGet("/api/state", async (MemoryDemoRuntime runtime, CancellationToken ct) =>
{
    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    var state = agent.GetState();
    state.Context.TryGetValue("history_summary", out var summary);

    var messages = (state.History ?? new())
        .Select(m => new
        {
            id = m.Id,
            role = m.Role.ToString(),
            content = m.Content ?? "",
            timestamp = m.Timestamp?.ToDateTime().ToString("O") ?? ""
        })
        .ToList();

    return Results.Json(new
    {
        agentId,
        historyCount = state.History?.Count ?? 0,
        summary = summary ?? "",
        messages
    });
});

app.MapGet("/api/cqrs/state", async (
    MemoryDemoRuntime runtime,
    IStateQueryService stateQuery,
    CancellationToken ct) =>
{
    var (agent, _) = await runtime.GetAgentAsync(ct);
    var agentType = agent.GetType().FullName ?? agent.GetType().Name;
    var doc = await stateQuery.GetByIdAsync(agentType, agent.Id, ct);

    return Results.Json(new
    {
        agentId = agent.Id,
        agentType,
        found = doc != null,
        doc
    });
});

app.MapPost("/api/search_memory", async (
    SearchMemoryInDto input,
    MemoryDemoRuntime runtime,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Query))
        return Results.BadRequest(new { error = "query is required" });

    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    var result = await agent.SearchMemoryToolAsync(
        query: input.Query.Trim(),
        maxResults: input.MaxResults ?? 10,
        memoryType: input.MemoryType ?? "all",
        memoryId: input.MemoryId,
        ct: ct);

    if (!result.IsSuccess)
    {
        return Results.Json(new
        {
            agentId,
            ok = false,
            toolName = result.ToolName,
            error = result.ErrorMessage ?? "unknown"
        }, statusCode: 500);
    }

    // ToolExecutionResult.Content is already JSON (protobuf Struct formatted).
    return Results.Text(result.Content ?? "{}", "application/json");
});

app.MapPost("/api/settings", async (
    UpdateSettingsInDto input,
    MemoryDemoRuntime runtime,
    CancellationToken ct) =>
{
    var (agent, _) = await runtime.GetAgentAsync(ct);

    if (input.EnableMemoryStoreAppend.HasValue)
        agent.EnableMemoryStoreAppend = input.EnableMemoryStoreAppend.Value;

    if (input.EnableMemoryVectorIndexAppend.HasValue)
        agent.EnableMemoryVectorIndexAppend = input.EnableMemoryVectorIndexAppend.Value;

    if (input.AllowInternalTools.HasValue)
        agent.AllowInternalTools = input.AllowInternalTools.Value;

    if (input.AllowDangerousTools.HasValue)
        agent.AllowDangerousTools = input.AllowDangerousTools.Value;

    return Results.Json(new
    {
        ok = true,
        enableMemoryStoreAppend = agent.EnableMemoryStoreAppend,
        enableMemoryVectorIndexAppend = agent.EnableMemoryVectorIndexAppend,
        allowInternalTools = agent.AllowInternalTools,
        allowDangerousTools = agent.AllowDangerousTools,
        defaultMemoryId = MemoryDemoPaths.BuildDefaultAgentMemoryId(agent.Id)
    });
});

app.MapGet("/api/tools", async (
    MemoryDemoRuntime runtime,
    CancellationToken ct) =>
{
    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    var tools = await agent.GetRegisteredToolsAsync();

    var list = tools
        .OrderBy(t => t.Category)
        .ThenBy(t => t.Name, StringComparer.Ordinal)
        .Select(t =>
        {
            var denyReason = GetToolDenyReason(agent, t);
            return new
            {
                name = t.Name,
                description = t.Description,
                category = t.Category.ToString(),
                version = t.Version,
                tags = t.Tags,
                flags = new
                {
                    requiresInternalAccess = t.RequiresInternalAccess,
                    requiresConfirmation = t.RequiresConfirmation,
                    isDangerous = t.IsDangerous
                },
                policy = new
                {
                    allowInternalTools = agent.AllowInternalTools,
                    allowDangerousTools = agent.AllowDangerousTools,
                    allowed = denyReason == null,
                    denyReason
                }
            };
        })
        .ToList();

    return Results.Json(new
    {
        agentId,
        count = list.Count,
        tools = list
    });
});

app.MapGet("/api/memory/resources", async (
    IMemoryStore store,
    CancellationToken ct) =>
{
    var list = await store.ListResourcesAsync(limit: 100, ct: ct);
    return Results.Json(new { count = list.Count, resources = list });
});

app.MapGet("/api/memory/entries", async (
    string memoryId,
    int? limit,
    IMemoryStore store,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(memoryId))
        return Results.BadRequest(new { error = "memoryId is required" });

    var take = limit is null ? 50 : Math.Clamp(limit.Value, 1, 200);
    var entries = await store.ListEntriesAsync(memoryId.Trim(), take, ct);
    return Results.Json(new { memoryId = memoryId.Trim(), count = entries.Count, entries });
});

app.MapGet("/api/memory/stats", (string memoryId) =>
{
    if (string.IsNullOrWhiteSpace(memoryId))
        return Results.BadRequest(new { error = "memoryId is required" });

    var paths = MemoryDemoPaths.Get();
    var dir = FileMemoryStore.GetBundleDirectory(paths.MemoryRoot, memoryId.Trim());
    var entriesPath = Path.Combine(dir, FileMemoryStore.EntriesBinaryFileName);
    var manifestPath = Path.Combine(dir, FileMemoryStore.ManifestFileName);
    return Results.Json(new
    {
        memoryId = memoryId.Trim(),
        memoryRoot = paths.MemoryRoot,
        bundleDir = dir,
        entries = new { path = entriesPath, exists = File.Exists(entriesPath), bytes = File.Exists(entriesPath) ? new FileInfo(entriesPath).Length : 0 },
        manifest = new { path = manifestPath, exists = File.Exists(manifestPath), bytes = File.Exists(manifestPath) ? new FileInfo(manifestPath).Length : 0 }
    });
});

app.MapPost("/api/vector/search", async (
    VectorSearchInDto input,
    MemoryDemoRuntime runtime,
    IMemoryVectorIndex index,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Query))
        return Results.BadRequest(new { error = "query is required" });

    var (agent, _) = await runtime.GetAgentAsync(ct);
    var memoryId = string.IsNullOrWhiteSpace(input.MemoryId)
        ? MemoryDemoPaths.BuildDefaultAgentMemoryId(agent.Id)
        : input.MemoryId.Trim();

    var embedding = await agent.TryGenerateEmbeddingVectorAsync(input.Query.Trim(), ct);
    if (embedding == null || embedding.Count == 0)
    {
        return Results.BadRequest(new
        {
            error = "embedding generator not available (configure provider embeddings) or embedding failed",
            hint = "Check appsettings.secrets.json -> LLMProviders:Providers:<name>:Embeddings:Enabled",
            memoryId
        });
    }

    var matches = await index.SearchAsync(
        queryEmbedding: embedding.ToArray(),
        limit: input.Limit ?? 10,
        memoryId: memoryId,
        ct: ct);

    return Results.Json(new
    {
        ok = true,
        memoryId,
        count = matches.Count,
        matches = matches.Select(m => new
        {
            similarity = m.Similarity,
            record = new
            {
                entryId = m.Record.EntryId,
                memoryId = m.Record.MemoryId,
                role = m.Record.Role,
                content = m.Record.Content,
                scopeType = m.Record.Scope?.Type.ToString(),
                scopeId = m.Record.Scope?.ScopeId,
                createdAt = m.Record.CreatedAt?.ToDateTime().ToString("O")
            }
        })
    });
});

app.MapGet("/api/vector/stats", (string memoryId) =>
{
    if (string.IsNullOrWhiteSpace(memoryId))
        return Results.BadRequest(new { error = "memoryId is required" });

    var paths = MemoryDemoPaths.Get();
    var dir = FileMemoryStore.GetBundleDirectory(paths.VectorRoot, memoryId.Trim());
    var vectorsPath = Path.Combine(dir, FileMemoryVectorIndex.VectorsBinaryFileName);
    return Results.Json(new
    {
        memoryId = memoryId.Trim(),
        vectorRoot = paths.VectorRoot,
        bundleDir = dir,
        vectors = new { path = vectorsPath, exists = File.Exists(vectorsPath), bytes = File.Exists(vectorsPath) ? new FileInfo(vectorsPath).Length : 0 }
    });
});

app.MapPost("/api/trace/seed", async (
    TraceSeedInDto input,
    IExecutionTraceStore traceStore,
    CancellationToken ct) =>
{
    var executionId = string.IsNullOrWhiteSpace(input.ExecutionId)
        ? $"memorydemo-{Guid.NewGuid():N}"
        : input.ExecutionId.Trim();

    var started = DateTime.UtcNow;
    var trace = new ExecutionTrace
    {
        ExecutionId = executionId,
        Kind = ExecutionTraceKind.Custom,
        Status = ExecutionTraceStatus.Succeeded,
        Name = "MemoryDemo Trace",
        Description = "Seeded execution trace for MemoryGraph + execution-scoped memory search.",
        StartedAt = Timestamp.FromDateTime(started),
        EndedAt = Timestamp.FromDateTime(started.AddSeconds(2)),
        Root = new ExecutionTraceNode
        {
            NodeId = "root",
            Name = "root",
            Type = "workflow",
            Status = ExecutionTraceStatus.Succeeded,
            StartedAt = Timestamp.FromDateTime(started),
            EndedAt = Timestamp.FromDateTime(started.AddSeconds(2)),
            Output = $"trace-seed-keyword: aevatar-trace-graph · ts={DateTime.UtcNow:O}",
            Decisions =
            {
                new ExecutionTraceDecisionSession
                {
                    DecisionId = "d1",
                    Type = "select",
                    Rounds = 1,
                    WinnerCandidateId = "c1",
                    Candidates =
                    {
                        new ExecutionTraceCandidate { CandidateId = "c1", Content = "option A: use vector + graph", Score = 0.9, Votes = 3 },
                        new ExecutionTraceCandidate { CandidateId = "c2", Content = "option B: use lexical only", Score = 0.1, Votes = 0 }
                    }
                }
            },
            Alerts =
            {
                new ExecutionTraceAlert
                {
                    AlertId = "a1",
                    Type = "demo",
                    Message = "This is a demo alert generated by MemoryDemo.",
                    Recovered = true,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                }
            }
        }
    };

    trace.Events.Add(new ExecutionTraceEvent
    {
        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
        Phase = "seed",
        Message = "Seeded trace created",
        NodeId = "root"
    });

    await traceStore.SaveAsync(trace, ct);

    return Results.Json(new
    {
        ok = true,
        executionId,
        memoryId = $"execution::{executionId}"
    });
});

app.MapGet("/api/trace/list", async (
    int? limit,
    IExecutionTraceStore traceStore,
    CancellationToken ct) =>
{
    var take = limit is null ? 50 : Math.Clamp(limit.Value, 1, 200);
    var list = await traceStore.ListAsync(take, ct);
    return Results.Json(new { count = list.Count, traces = list });
});

app.MapGet("/api/trace/{executionId}", async (
    string executionId,
    IExecutionTraceStore traceStore,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(executionId))
        return Results.BadRequest(new { error = "executionId is required" });

    var trace = await traceStore.LoadAsync(executionId.Trim(), ct);
    if (trace == null)
        return Results.NotFound(new { error = "trace not found" });

    return Results.Text(trace.ToJsonString(), "application/json");
});

app.MapGet("/api/graph/{executionId}", async (
    string executionId,
    IMemoryGraphStore graphStore,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(executionId))
        return Results.BadRequest(new { error = "executionId is required" });

    var graph = await graphStore.LoadAsync(executionId.Trim(), ct);
    if (graph == null)
        return Results.NotFound(new { error = "graph not found" });

    return Results.Text(Google.Protobuf.JsonFormatter.Default.Format(graph), "application/json");
});

app.MapPost("/api/reset", async (MemoryDemoRuntime runtime, CancellationToken ct) =>
{
    var status = await runtime.ResetAsync(ct);
    return Results.Json(status);
});

app.Run();

static string? GetToolDenyReason(MemoryDemoAgent agent, ToolDefinition tool)
{
    if (tool.RequiresInternalAccess && !agent.AllowInternalTools)
        return "RequiresInternalAccess is disabled (AllowInternalTools=false).";

    if ((tool.IsDangerous || tool.RequiresConfirmation) && !agent.AllowDangerousTools)
        return "Dangerous/confirmation tools are disabled (AllowDangerousTools=false).";

    return null;
}

static async Task WriteNdjsonAsync(HttpResponse response, object payload, CancellationToken ct)
{
    var json = JsonSerializer.Serialize(payload);
    await response.WriteAsync(json, ct);
    await response.WriteAsync("\n", ct);
    await response.Body.FlushAsync(ct);
}

static object? TryExtractToolCallFromHistory(AevatarAIAgentState state, DateTime startedAtUtc)
{
    if (state?.History == null || state.History.Count == 0)
        return null;

    // Tool call transcript is appended as:
    // - Assistant message with ToolCalls[] (content: "Calling tool ...")
    // - Tool role message with ToolResult (content: tool JSON result)
    string? toolName = null;
    string? arguments = null;
    string? result = null;

    foreach (var msg in state.History)
    {
        var ts = msg.Timestamp?.ToDateTime();
        if (ts == null || ts.Value < startedAtUtc)
            continue;

        if (toolName == null && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
        {
            var tc = msg.ToolCalls[0];
            toolName = tc.ToolName;
            arguments = tc.Arguments;
            continue;
        }

        if (result == null && msg.Role == AevatarChatRole.Tool)
        {
            result = msg.ToolResult?.Content ?? msg.Content;
        }
    }

    if (string.IsNullOrWhiteSpace(toolName))
        return null;

    return new
    {
        name = toolName,
        arguments = arguments ?? "",
        result = result ?? ""
    };
}

