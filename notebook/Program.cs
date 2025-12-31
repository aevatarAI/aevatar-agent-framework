using System.Text;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.Core.CQRS;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Notebook;
using Aevatar.Notebook.Agents;
using Aevatar.Notebook.Cqrs;
using Aevatar.Notebook.Persistence;
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

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.secrets.json", optional: true)
    .AddEnvironmentVariables();

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

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ============================================================
//  APIs
// ============================================================

app.MapGet("/api/info", async (
    NotebookRuntime runtime,
    IOptions<LLMProvidersConfig> llm,
    NotebookPersistence.NotebookPersistenceSelection selection,
    CancellationToken ct) =>
{
    var status = await runtime.GetStatusAsync(ct);
    return Results.Json(new
    {
        agentId = status.AgentId,
        isReady = status.IsReady,
        lastError = status.LastError,
        llmDefaultProvider = llm.Value.Default,
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

// Sources are stored as MemoryStore resources with scopeType=Graph and memoryId="source::<id>"
app.MapPost("/api/sources/text", async (
    SourceTextInDto input,
    IMemoryStore store,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Text))
        return Results.BadRequest(new { error = "text is required" });

    var sourceId = Guid.NewGuid().ToString("N")[..12];
    var memoryId = $"source::{sourceId}";

    var entry = new MemoryEntry
    {
        EntryId = Guid.NewGuid().ToString("N"),
        MemoryId = memoryId,
        Scope = new MemoryScope { Type = MemoryScopeType.Graph, ScopeId = sourceId },
        Role = "source",
        Content = input.Text.Trim(),
        CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
    };

    if (!string.IsNullOrWhiteSpace(input.Title))
        entry.Tags["title"] = input.Title.Trim();

    await store.AppendAsync(entry, ct);

    return Results.Json(new { ok = true, sourceId, memoryId });
});

app.MapGet("/api/sources", async (IMemoryStore store, CancellationToken ct) =>
{
    var list = await store.ListResourcesAsync(scopeTypeFilter: MemoryScopeType.Graph, limit: 200, ct: ct);
    var sources = list
        .Where(r => (r.MemoryId ?? string.Empty).StartsWith("source::", StringComparison.Ordinal))
        .Select(r => new
        {
            sourceId = (r.MemoryId ?? string.Empty).Replace("source::", "", StringComparison.Ordinal),
            memoryId = r.MemoryId,
            entryCount = r.EntryCount,
            latestAt = r.LatestAt?.ToDateTime().ToString("O") ?? ""
        })
        .ToList();
    return Results.Json(new { count = sources.Count, sources });
});

app.MapGet("/api/sources/{sourceId}", async (
    string sourceId,
    IMemoryStore store,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(sourceId))
        return Results.BadRequest(new { error = "sourceId is required" });

    var memoryId = $"source::{sourceId.Trim()}";
    var entries = await store.ListEntriesAsync(memoryId, limit: 50, ct: ct);
    return Results.Json(new { sourceId = sourceId.Trim(), memoryId, count = entries.Count, entries });
});

app.MapPost("/api/chat", async (
    ChatInDto input,
    NotebookRuntime runtime,
    IMemoryStore store,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(input.Message))
        return Results.BadRequest(new { error = "message is required" });

    var (agent, agentId) = await runtime.GetAgentAsync(ct);

    // Build "all sources" context (bounded) and feed to LLM each time (MVP).
    var context = await BuildNotebookContextAsync(store, ct);

    var request = new ChatRequest
    {
        Message = input.Message.Trim(),
        RequestId = input.RequestId ?? Guid.NewGuid().ToString("N"),
        StageHint = input.StageHint ?? ""
    };
    request.Context["notebook_context"] = context;

    var resp = await agent.ChatAsync(request, ct);

    return Results.Json(new
    {
        agentId,
        requestId = request.RequestId,
        content = resp.Content,
        toolCalled = resp.ToolCalled
    });
});

app.MapPost("/api/report", async (
    ReportInDto input,
    NotebookRuntime runtime,
    IMemoryStore store,
    CancellationToken ct) =>
{
    var topic = string.IsNullOrWhiteSpace(input.Topic) ? "Untitled Report" : input.Topic.Trim();
    var (agent, agentId) = await runtime.GetAgentAsync(ct);

    var context = await BuildNotebookContextAsync(store, ct);

    var request = new ChatRequest
    {
        Message =
            $"""
            Generate a structured report.

            Topic: {topic}

            Requirements:
            - Use the Notebook context as the only source of truth.
            - Include a short executive summary.
            - Include 3-8 bullet key points.
            - Include citations by referring to sourceIds when possible.
            """.Trim(),
        RequestId = Guid.NewGuid().ToString("N"),
        StageHint = "report"
    };
    request.Context["notebook_context"] = context;

    var resp = await agent.ChatAsync(request, ct);

    return Results.Json(new { agentId, topic, content = resp.Content });
});

app.MapPost("/api/reset", async (NotebookRuntime runtime, CancellationToken ct) =>
{
    var status = await runtime.ResetAsync(ct);
    return Results.Json(status);
});

app.Run();

// ============================================================
//  DTOs + helpers
// ============================================================

static async Task<string> BuildNotebookContextAsync(IMemoryStore store, CancellationToken ct)
{
    // ------------------------------------------------------------
    //  MVP strategy:
    //  - Load last entry per source (bounded).
    //  - Keep total context bounded to avoid prompt explosion.
    // ------------------------------------------------------------
    var resources = await store.ListResourcesAsync(scopeTypeFilter: MemoryScopeType.Graph, limit: 50, ct: ct);
    var sources = resources
        .Where(r => (r.MemoryId ?? string.Empty).StartsWith("source::", StringComparison.Ordinal))
        .OrderByDescending(r => r.LatestAt?.ToDateTime() ?? DateTime.MinValue)
        .ToList();

    const int maxTotalChars = 18_000;
    const int maxPerSourceChars = 4_000;

    var sb = new StringBuilder(capacity: 4096);
    foreach (var s in sources)
    {
        ct.ThrowIfCancellationRequested();
        if (sb.Length >= maxTotalChars)
            break;

        var memoryId = s.MemoryId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(memoryId))
            continue;

        var sourceId = memoryId.Replace("source::", "", StringComparison.Ordinal);
        var entries = await store.ListEntriesAsync(memoryId, limit: 1, ct: ct);
        var text = entries.FirstOrDefault()?.Content ?? string.Empty;
        text = text.Replace("\r", "").Trim();
        if (text.Length > maxPerSourceChars)
            text = text[..maxPerSourceChars];

        if (text.Length == 0)
            continue;

        sb.AppendLine($"[source:{sourceId}]");
        sb.AppendLine(text);
        sb.AppendLine();
    }

    var result = sb.ToString().Trim();
    if (result.Length > maxTotalChars)
        result = result[..maxTotalChars];

    return result;
}

public sealed record ChatInDto(string Message)
{
    public string? RequestId { get; init; }
    public string? StageHint { get; init; }
}

public sealed record SourceTextInDto(string Text)
{
    public string? Title { get; init; }
}

public sealed record ReportInDto
{
    public string? Topic { get; init; }
}

