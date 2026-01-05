using MemoryDemo;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Persistence.Supabase.DependencyInjection;
using Aevatar.Agents.Runtime.Local;
using Microsoft.Extensions.Options;

// ============================================================
//  MemoryDemo - Web UI + APIs
//
//  Demo focus:
//  - State.History (short-term window) replay
//  - Compaction: sliding window + State.Context["history_summary"]
//  - Long-term memory: IAevatarAIMemory (InMemory by default; optional Mongo/Supabase)
//  - Tool recall: built-in tool `search_memory`
// ============================================================

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.Configure<LLMProvidersConfig>(builder.Configuration.GetSection("LLMProviders"));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Aevatar runtime + LLM provider factory
builder.Services.AddAevatarLocalRuntime();
builder.Services.AddMEAI();

// Long-term memory (default: in-proc, runnable out-of-the-box)
builder.Services.AddSingleton<IAevatarAIMemoryFactory, InMemoryAIMemoryFactory>();

// Optional DB-backed IAevatarAIMemory (overrides InMemory if configured)
var memoryStoreKind = "InMemory";
var mongoConn = builder.Configuration.GetConnectionString("MongoDB");
var supabaseConn = builder.Configuration.GetConnectionString("SupabasePostgres");

if (!string.IsNullOrWhiteSpace(mongoConn))
{
    builder.Services.AddAevatarMongoDB(
        connectionString: mongoConn!,
        databaseName: builder.Configuration["MongoDB:Database"] ?? "aevatar");
    builder.Services.AddMongoDBAIMemory();
    memoryStoreKind = "MongoDB";
}
else if (!string.IsNullOrWhiteSpace(supabaseConn))
{
    builder.Services.AddAevatarSupabase(
        connectionString: supabaseConn!,
        configure: o =>
        {
            // Keep default schema/table names unless you want to override in code.
            // o.Schema = "aevatar";
        });
    builder.Services.AddSupabaseAIMemory();
    memoryStoreKind = "Supabase";
}

builder.Services.AddSingleton(new MemoryDemoRuntimeOptions { MemoryStoreKind = memoryStoreKind });
builder.Services.AddSingleton<MemoryDemoRuntime>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ============================================================
//  APIs
// ============================================================

app.MapGet("/api/info", async (
    MemoryDemoRuntime runtime,
    MemoryDemoRuntimeOptions options,
    IOptions<LLMProvidersConfig> llm,
    CancellationToken ct) =>
{
    var status = await runtime.GetStatusAsync(ct);
    return Results.Json(new
    {
        agentId = status.AgentId,
        isReady = status.IsReady,
        lastError = status.LastError,
        llmDefaultProvider = llm.Value.Default,
        memoryStore = options.MemoryStoreKind,
        hasLongTermMemory = status.HasLongTermMemory,
        longTermMemoryType = status.LongTermMemoryType,
        settings = new
        {
            enableHistory = status.EnableChatHistoryInState,
            enableCompaction = status.EnableChatHistoryCompaction,
            chatHistoryMaxMessages = status.ChatHistoryMaxMessages,
            chatHistorySummaryMaxChars = status.ChatHistorySummaryMaxChars
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
        }
    });
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

app.MapGet("/api/longterm/history", async (
    int? limit,
    MemoryDemoRuntime runtime,
    CancellationToken ct) =>
{
    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    var items = await agent.GetLongTermHistoryAsync(limit ?? 200, ct);
    return Results.Json(new
    {
        agentId,
        count = items.Count,
        items = items.Select(x => new
        {
            role = x.Role,
            content = x.Content,
            timestamp = x.Timestamp?.ToDateTime().ToString("O") ?? ""
        })
    });
});

app.MapGet("/api/longterm/search", async (
    string query,
    int? topK,
    MemoryDemoRuntime runtime,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(query))
        return Results.BadRequest(new { error = "query is required" });

    var (agent, agentId) = await runtime.GetAgentAsync(ct);
    var hits = await agent.SearchLongTermAsync(query.Trim(), topK ?? 5, ct);
    return Results.Json(new
    {
        agentId,
        count = hits.Count,
        hits
    });
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
}

public sealed class MemoryDemoRuntimeOptions
{
    public string MemoryStoreKind { get; init; } = "InMemory";
}

public sealed class MemoryDemoStatus
{
    public required string AgentId { get; init; }
    public required bool IsReady { get; init; }
    public string? LastError { get; init; }
    public bool HasLongTermMemory { get; init; }
    public string LongTermMemoryType { get; init; } = "none";
    public bool EnableChatHistoryInState { get; init; }
    public bool EnableChatHistoryCompaction { get; init; }
    public int ChatHistoryMaxMessages { get; init; }
    public int ChatHistorySummaryMaxChars { get; init; }
}

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
            HasLongTermMemory = _agent?.HasLongTermMemory ?? false,
            LongTermMemoryType = _agent?.LongTermMemoryType ?? "none",
            EnableChatHistoryInState = _agent?.EnableChatHistoryInState ?? false,
            EnableChatHistoryCompaction = _agent?.EnableChatHistoryCompaction ?? false,
            ChatHistoryMaxMessages = _agent?.ChatHistoryMaxMessages ?? 0,
            ChatHistorySummaryMaxChars = _agent?.ChatHistorySummaryMaxChars ?? 0
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

            _logger.LogInformation("[MemoryDemo] Ready. Long-term memory: {Has} ({Type})",
                _agent.HasLongTermMemory, _agent.LongTermMemoryType);

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


