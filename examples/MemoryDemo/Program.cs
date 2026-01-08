using MemoryDemo;
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
    .AddAevatarUserSecrets()
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// ============================================================
//  Persistence selection (config-driven)
// ============================================================
var persistence = MemoryDemoPersistence.Configure(builder.Services, builder.Configuration);

// Aevatar core + Local runtime (stores are configured by persistence selection above)
builder.Services.AddAevatarAgentSystem(persistence.ConfigureStores, b => b.UseLocalRuntime());
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

app.MapGet("/api/info", async (
    MemoryDemoRuntime runtime,
    IOptions<LLMProvidersConfig> llm,
    MemoryDemoPersistence.MemoryDemoPersistenceSelection persistenceSelection,
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
            enableMemoryVectorIndexAppend = status.EnableMemoryVectorIndexAppend
        },
        paths = new
        {
            traceRoot = paths.TraceRoot,
            memoryRoot = paths.MemoryRoot,
            vectorRoot = paths.VectorRoot
        },
        persistence = new
        {
            providers = new
            {
                memoryStore = persistenceSelection.MemoryStoreProvider,
                memoryVectorIndex = persistenceSelection.MemoryVectorIndexProvider,
                memoryGraph = persistenceSelection.MemoryGraphProvider
            },
            types = new
            {
                memoryStore = persistenceSelection.MemoryStoreType ?? "default(file)",
                memoryVectorIndex = persistenceSelection.MemoryVectorIndexType ?? "default(file)",
                memoryGraphStore = persistenceSelection.MemoryGraphStoreType ?? "default(file)"
            }
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

    return Results.Json(new
    {
        ok = true,
        enableMemoryStoreAppend = agent.EnableMemoryStoreAppend,
        enableMemoryVectorIndexAppend = agent.EnableMemoryVectorIndexAppend,
        defaultMemoryId = MemoryDemoPaths.BuildDefaultAgentMemoryId(agent.Id)
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

app.MapGet("/api/memory/stats", (
    string memoryId,
    MemoryDemoPersistence.MemoryDemoPersistenceSelection persistenceSelection) =>
{
    if (string.IsNullOrWhiteSpace(memoryId))
        return Results.BadRequest(new { error = "memoryId is required" });

    if (!string.Equals(persistenceSelection.MemoryStoreProvider, "file", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            error = "memory stats is only available for file-based IMemoryStore",
            provider = persistenceSelection.MemoryStoreProvider
        });
    }

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

app.MapGet("/api/vector/stats", (
    string memoryId,
    MemoryDemoPersistence.MemoryDemoPersistenceSelection persistenceSelection) =>
{
    if (string.IsNullOrWhiteSpace(memoryId))
        return Results.BadRequest(new { error = "memoryId is required" });

    if (!string.Equals(persistenceSelection.MemoryVectorIndexProvider, "file", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new
        {
            error = "vector stats is only available for file-based IMemoryVectorIndex",
            provider = persistenceSelection.MemoryVectorIndexProvider
        });
    }

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

// ============================================================
//  DTOs + Runtime
// ============================================================

public sealed record ChatInDto(string Message)
{
    public string? RequestId { get; init; }
    public string? StageHint { get; init; }
}

public sealed record SearchMemoryInDto(string Query)
{
    public int? MaxResults { get; init; }
    public string? MemoryType { get; init; }
    public string? MemoryId { get; init; }
}

public sealed record SeedInDto(string Text);

public sealed record UpdateSettingsInDto
{
    public bool? EnableMemoryStoreAppend { get; init; }
    public bool? EnableMemoryVectorIndexAppend { get; init; }
}

public sealed record VectorSearchInDto(string Query)
{
    public string? MemoryId { get; init; }
    public int? Limit { get; init; }
}

public sealed record TraceSeedInDto
{
    public string? ExecutionId { get; init; }
}

public sealed class MemoryDemoStatus
{
    public required string AgentId { get; init; }
    public required bool IsReady { get; init; }
    public string? LastError { get; init; }
    public bool EnableChatHistoryInState { get; init; }
    public bool EnableChatHistoryCompaction { get; init; }
    public int ChatHistoryMaxMessages { get; init; }
    public int ChatHistorySummaryMaxChars { get; init; }
    public bool EnableMemoryStoreAppend { get; init; }
    public bool EnableMemoryVectorIndexAppend { get; init; }
}

internal static class MemoryDemoPaths
{
    public static MemoryDemoPathInfo Get()
    {
        // NOTE: demo reads default roots from the same helpers as core stores (env-var aware).
        var traceRoot = FileExecutionTraceStore.GetTraceRootFromEnvironmentOrDefault();
        var memoryRoot = FileMemoryStore.GetMemoryRootFromEnvironmentOrDefault();
        var vectorRoot = FileMemoryVectorIndex.GetVectorRootFromEnvironmentOrDefault();

        return new MemoryDemoPathInfo(traceRoot, memoryRoot, vectorRoot);
    }

    public static string BuildDefaultAgentMemoryId(string agentId)
        => $"privateagent::{agentId}";
}

internal sealed record MemoryDemoPathInfo(string TraceRoot, string MemoryRoot, string VectorRoot);

public sealed class MemoryDemoRuntime
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<MemoryDemoRuntime> _logger;
    private readonly IOptions<LLMProvidersConfig> _llm;

    private readonly SemaphoreSlim _lock = new(1, 1);

    private IGAgentActor? _actor;
    private MemoryDemoAgent? _agent;
    private string _agentId = $"memory-demo-{Guid.NewGuid():N}";
    private string? _lastError;
    private bool _isReady;

    public MemoryDemoRuntime(
        IGAgentActorFactory actorFactory,
        ILogger<MemoryDemoRuntime> logger,
        IOptions<LLMProvidersConfig> llm)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _llm = llm;
    }

    public async Task<(MemoryDemoAgent Agent, string AgentId)> GetAgentAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        if (_agent == null || _actor == null)
            throw new InvalidOperationException(_lastError ?? "agent not initialized");
        return (_agent, _agentId);
    }

    public async Task<MemoryDemoStatus> GetStatusAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        return new MemoryDemoStatus
        {
            AgentId = _agentId,
            IsReady = _isReady,
            LastError = _lastError,
            EnableChatHistoryInState = _agent?.EnableChatHistoryInState ?? false,
            EnableChatHistoryCompaction = _agent?.EnableChatHistoryCompaction ?? false,
            ChatHistoryMaxMessages = _agent?.ChatHistoryMaxMessages ?? 0,
            ChatHistorySummaryMaxChars = _agent?.ChatHistorySummaryMaxChars ?? 0,
            EnableMemoryStoreAppend = _agent?.EnableMemoryStoreAppend ?? false,
            EnableMemoryVectorIndexAppend = _agent?.EnableMemoryVectorIndexAppend ?? false
        };
    }

    public async Task<MemoryDemoStatus> ResetAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _actor = null;
            _agent = null;
            _isReady = false;
            _lastError = null;
            _agentId = $"memory-demo-{Guid.NewGuid():N}";
        }
        finally
        {
            _lock.Release();
        }

        return await GetStatusAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isReady)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_isReady)
                return;

            _lastError = null;

            _logger.LogInformation("[MemoryDemo] Creating agent actor: {AgentId}", _agentId);
            _actor = await _actorFactory.CreateGAgentActorAsync<MemoryDemoAgent>(_agentId);
            _agent = (MemoryDemoAgent)_actor.GetAgent();

            // Initialize LLM provider (use config default if present).
            var providerName = string.IsNullOrWhiteSpace(_llm.Value.Default) ? "default" : _llm.Value.Default;
            _logger.LogInformation("[MemoryDemo] Initializing LLM provider: {Provider}", providerName);

            await _agent.InitializeAsync(
                providerName,
                cfg =>
                {
                    // Keep defaults unless caller wants to override in appsettings.
                    cfg.Temperature = 0.3f;
                    cfg.MaxOutputTokens = 800;
                },
                ct);

            _logger.LogInformation("[MemoryDemo] Ready.");

            _isReady = true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogError(ex, "[MemoryDemo] Initialization failed: {Message}", ex.Message);
            _isReady = false;
        }
        finally
        {
            _lock.Release();
        }
    }
}


