using System.Text;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Core.Memory;
using Aevatar.Agents.Core.Tracing;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryDemo;

// ============================================================
//  Knowledge Base (Book) API
//
//  Design:
//  - Ingest a "book" as MemoryEntry chunks under a dedicated memoryId (tenant::<bookId>).
//  - Optionally write embeddings into IMemoryVectorIndex for semantic retrieval.
//  - Select an active book for the agent via State.Context so chat can recall it via search_memory.
// ============================================================
internal static class MemoryDemoKnowledgeBaseApi
{
    private const int DefaultChunkChars = 1000;
    private const int DefaultOverlapChars = 150;
    private const int DefaultMaxChunks = 400;

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/kb/ingest", IngestAsync);
        app.MapGet("/api/kb/resources", ListResourcesAsync);
        app.MapGet("/api/kb/status", GetStatusAsync);
        app.MapPost("/api/kb/select", SelectAsync);
    }

    private static async Task<IResult> IngestAsync(
        KbIngestInDto input,
        MemoryDemoRuntime runtime,
        IMemoryStore store,
        IMemoryVectorIndex vectorIndex,
        ILogger<MemoryDemoRuntime> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
            return Results.BadRequest(new { error = "text is required" });

        // Keep demo bounded (avoid accidental multi-MB pastes locking the server).
        const int hardMaxChars = 2_000_000;
        if (input.Text.Length > hardMaxChars)
        {
            return Results.BadRequest(new
            {
                error = "text is too large for demo ingestion",
                maxChars = hardMaxChars
            });
        }

        var bookId = NormalizeIdOrCreate(input.BookId, prefix: "book");
        var title = (input.Title ?? string.Empty).Trim();

        // Use TENANT scope to represent a shared knowledge base resource.
        var memoryId = $"tenant::{bookId}";
        var scope = new MemoryScope { Type = MemoryScopeType.Tenant, ScopeId = bookId };

        var chunkChars = Math.Clamp(input.ChunkChars ?? DefaultChunkChars, 200, 2000);
        var overlapChars = Math.Clamp(input.OverlapChars ?? DefaultOverlapChars, 0, 400);
        var maxChunks = Math.Clamp(input.MaxChunks ?? DefaultMaxChunks, 1, 2000);

        var chunks = ChunkText(input.Text, chunkChars, overlapChars, maxChunks);
        if (chunks.Count == 0)
            return Results.BadRequest(new { error = "no content after chunking" });

        var generateEmbeddings = input.GenerateEmbeddings ?? true;
        var selectAfterIngest = input.SelectAfterIngest ?? true;

        var (agent, agentId) = await runtime.GetAgentAsync(ct);

        logger.LogInformation("[MemoryDemo][KB] Ingesting book: {BookId}, chunks={Count}, embeddings={Embeddings}",
            bookId, chunks.Count, generateEmbeddings);

        var savedEntries = 0;
        var savedVectors = 0;
        var startedAt = DateTime.UtcNow;

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var content = chunks[i];
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var entryId = $"{bookId}:{i:D6}";

            var entry = new MemoryEntry
            {
                EntryId = entryId,
                MemoryId = memoryId,
                Scope = scope,
                RunId = string.Empty,
                AgentId = agentId,
                Role = "book",
                Content = content,
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            entry.Tags["source"] = "memorydemo.kb";
            entry.Tags["book_id"] = bookId;
            entry.Tags["chunk_index"] = i.ToString();
            entry.Tags["chunk_chars"] = content.Length.ToString();
            if (!string.IsNullOrWhiteSpace(title))
                entry.Tags["book_title"] = title;

            await store.AppendAsync(entry, ct);
            savedEntries++;

            if (!generateEmbeddings)
                continue;

            // Best-effort embeddings for semantic retrieval.
            // Keep input bounded, matching AIGAgentBase's default embedding cap style.
            const int maxEmbedChars = 2000;
            var embedText = content.Length <= maxEmbedChars ? content : content[..maxEmbedChars];

            try
            {
                var vec = await agent.TryGenerateEmbeddingVectorAsync(embedText, ct);
                if (vec == null || vec.Count == 0)
                    continue;

                var record = new MemoryVectorRecord
                {
                    EntryId = entryId,
                    MemoryId = memoryId,
                    Scope = scope,
                    RunId = string.Empty,
                    AgentId = agentId,
                    Role = "book",
                    CreatedAt = entry.CreatedAt,
                    Content = embedText
                };

                foreach (var v in vec)
                    record.Embedding.Add(v);

                foreach (var kv in entry.Tags)
                    record.Tags[kv.Key] = kv.Value;

                await vectorIndex.UpsertAsync(record, ct);
                savedVectors++;
            }
            catch (Exception ex)
            {
                // best-effort: do not fail ingestion because a single vector write failed.
                logger.LogDebug(ex, "[MemoryDemo][KB] Vector upsert failed (best-effort) for entry {EntryId}", entryId);
            }
        }

        if (selectAfterIngest)
        {
            agent.SetKnowledgeBase(memoryId, title);
        }

        var elapsedMs = (int)Math.Max(0, (DateTime.UtcNow - startedAt).TotalMilliseconds);

        return Results.Json(new
        {
            ok = true,
            agentId,
            bookId,
            title,
            memoryId,
            chunks = new { count = chunks.Count, chunkChars, overlapChars, maxChunks },
            stored = new { entries = savedEntries, vectors = savedVectors },
            selectedForChat = selectAfterIngest,
            elapsedMs
        });
    }

    private static async Task<IResult> ListResourcesAsync(
        IMemoryStore store,
        CancellationToken ct)
    {
        var list = await store.ListResourcesAsync(scopeTypeFilter: MemoryScopeType.Tenant, limit: 200, ct: ct);
        return Results.Json(new { count = list.Count, resources = list });
    }

    private static async Task<IResult> GetStatusAsync(
        MemoryDemoRuntime runtime,
        CancellationToken ct)
    {
        var (agent, agentId) = await runtime.GetAgentAsync(ct);
        var (memoryId, title) = agent.GetKnowledgeBase();
        return Results.Json(new { agentId, memoryId, title });
    }

    private static async Task<IResult> SelectAsync(
        KbSelectInDto input,
        MemoryDemoRuntime runtime,
        CancellationToken ct)
    {
        var (agent, agentId) = await runtime.GetAgentAsync(ct);
        agent.SetKnowledgeBase(input.MemoryId, input.Title);

        var (memoryId, title) = agent.GetKnowledgeBase();
        return Results.Json(new { ok = true, agentId, memoryId, title });
    }

    private static string NormalizeIdOrCreate(string? input, string prefix)
    {
        var s = (input ?? string.Empty).Trim();
        if (s.Length == 0)
            return $"{prefix}-{Guid.NewGuid():N}";

        // Keep it filename-safe-ish (FileMemoryStore will also sanitize).
        var buf = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                buf.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                buf.Append('-');
            }
        }

        var normalized = buf.ToString().Trim('-');
        return normalized.Length == 0 ? $"{prefix}-{Guid.NewGuid():N}" : normalized;
    }

    private static IReadOnlyList<string> ChunkText(string text, int chunkChars, int overlapChars, int maxChunks)
    {
        // Normalize newlines for deterministic chunking.
        var t = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (t.Length == 0)
            return Array.Empty<string>();

        // First split into paragraphs.
        var paras = t.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chunks = new List<string>(capacity: Math.Min(maxChunks, Math.Max(8, paras.Length / 2)));

        var sb = new StringBuilder(capacity: Math.Min(chunkChars, 2048));

        void Flush()
        {
            if (sb.Length == 0) return;
            var chunk = sb.ToString().Trim();
            sb.Clear();
            if (chunk.Length == 0) return;
            chunks.Add(chunk);
        }

        foreach (var para in paras)
        {
            if (chunks.Count >= maxChunks) break;
            var p = para.Trim();
            if (p.Length == 0) continue;

            // If a single paragraph is huge, slice it.
            if (p.Length > chunkChars)
            {
                Flush();

                var step = Math.Max(1, chunkChars - overlapChars);
                for (var start = 0; start < p.Length && chunks.Count < maxChunks; start += step)
                {
                    var take = Math.Min(chunkChars, p.Length - start);
                    var part = p.Substring(start, take).Trim();
                    if (part.Length > 0)
                        chunks.Add(part);
                }

                continue;
            }

            // Append paragraph into current chunk.
            if (sb.Length == 0)
            {
                sb.Append(p);
                continue;
            }

            if (sb.Length + 2 + p.Length > chunkChars)
            {
                Flush();
                sb.Append(p);
                continue;
            }

            sb.Append("\n\n").Append(p);
        }

        Flush();
        return chunks;
    }
}

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
    public bool? AllowInternalTools { get; init; }
    public bool? AllowDangerousTools { get; init; }
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

public sealed record KbIngestInDto
{
    public string? BookId { get; init; }
    public string? Title { get; init; }
    public required string Text { get; init; }

    public int? ChunkChars { get; init; }
    public int? OverlapChars { get; init; }
    public int? MaxChunks { get; init; }

    public bool? GenerateEmbeddings { get; init; }
    public bool? SelectAfterIngest { get; init; }
}

public sealed record KbSelectInDto
{
    public string? MemoryId { get; init; }
    public string? Title { get; init; }
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
    public bool AllowInternalTools { get; init; }
    public bool AllowDangerousTools { get; init; }
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
            EnableMemoryVectorIndexAppend = _agent?.EnableMemoryVectorIndexAppend ?? false,
            AllowInternalTools = _agent?.AllowInternalTools ?? false,
            AllowDangerousTools = _agent?.AllowDangerousTools ?? false
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


