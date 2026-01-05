using Aevatar.Agents.Abstractions.Memory;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Memory Store (Resource-based, append-only)
    //
    //  Design:
    //  - Default OFF (avoid hidden IO / surprises).
    //  - When enabled, append user/assistant messages as MemoryEntry (Protobuf).
    //  - Best-effort: never fail the chat because memory store failed.
    // ============================================================

    /// <summary>
    /// Memory store (injected by runtime via MemoryStoreInjector).
    /// </summary>
    protected IMemoryStore? MemoryStore { get; set; }

    /// <summary>
    /// Persistent vector index (injected by runtime via MemoryVectorIndexInjector).
    /// </summary>
    protected IMemoryVectorIndex? MemoryVectorIndex { get; set; }

    /// <summary>
    /// Switch (default: false):
    /// - When enabled, ChatAsync / ChatStreamAsync append MemoryEntry into IMemoryStore (append-only).
    /// </summary>
    public bool EnableMemoryStoreAppend { get; set; }

    /// <summary>
    /// Switch (default: false):
    /// - When enabled, and embedding generator is configured, we persist embeddings into IMemoryVectorIndex.
    /// </summary>
    public bool EnableMemoryVectorIndexAppend { get; set; }

    /// <summary>
    /// Default scope type for memory writes.
    /// </summary>
    public MemoryScopeType MemoryStoreScopeType { get; set; } = MemoryScopeType.PrivateAgent;

    /// <summary>
    /// Optional override for scope_id. When not set, we best-effort derive it from ChatRequest.Context.
    /// </summary>
    public string? MemoryStoreScopeIdOverride { get; set; }

    /// <summary>
    /// Optional override for memory_id. When not set, memory_id is derived from scope.
    /// </summary>
    public string? MemoryIdOverride { get; set; }

    protected virtual async Task AppendChatMemoryAsync(
        AevatarChatRole role,
        string content,
        ChatRequest request,
        CancellationToken ct)
    {
        if (!EnableMemoryStoreAppend)
            return;

        if (MemoryStore == null)
            return;

        if (string.IsNullOrWhiteSpace(content))
            return;

        try
        {
            var scope = BuildMemoryScope(request);
            var memoryId = BuildMemoryId(scope);
            var runId = TryGetContextValue(request, "run_id", "runId") ?? string.Empty;

            var entry = new MemoryEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                MemoryId = memoryId,
                Scope = scope,
                RunId = runId,
                AgentId = Id.ToString(),
                Role = role.ToString().ToLowerInvariant(),
                Content = content.Trim(),
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            // Lightweight tags for governance / debug
            entry.Tags["agent_type"] = GetType().FullName ?? GetType().Name;
            entry.Tags["request_id"] = request.RequestId ?? string.Empty;
            entry.Tags["scope_type"] = scope.Type.ToString();
            entry.Tags["scope_id"] = scope.ScopeId ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(request.StageHint))
                entry.Tags["stage_hint"] = request.StageHint!;

            await MemoryStore.AppendAsync(entry, ct);

            // Optional: persist vector record (best-effort, does NOT affect chat).
            await AppendMemoryVectorAsync(entry, ct);
        }
        catch (Exception ex)
        {
            // Best-effort: never fail chat because memory append failed.
            Logger.LogDebug(ex, "Failed to append chat memory (best-effort)");
        }
    }

    protected virtual async Task AppendMemoryVectorAsync(MemoryEntry entry, CancellationToken ct)
    {
        if (!EnableMemoryVectorIndexAppend)
            return;

        if (MemoryVectorIndex == null)
            return;

        if (!TryGetEmbeddingGenerator(out _))
            return;

        // Keep inputs bounded (avoid huge embedding calls).
        const int maxChars = 2000;
        var text = (entry.Content ?? string.Empty).Replace("\r", "").Trim();
        if (text.Length == 0)
            return;

        if (text.Length > maxChars)
            text = text[..maxChars];

        try
        {
            var embedding = await GenerateEmbeddingAsync(text, cancellationToken: ct);
            if (embedding == null)
                return;

            var record = new MemoryVectorRecord
            {
                EntryId = entry.EntryId ?? string.Empty,
                MemoryId = entry.MemoryId ?? string.Empty,
                Scope = entry.Scope,
                RunId = entry.RunId ?? string.Empty,
                AgentId = entry.AgentId ?? string.Empty,
                Role = entry.Role ?? string.Empty,
                CreatedAt = entry.CreatedAt,
                Content = text
            };

            foreach (var v in embedding.Vector.Span)
                record.Embedding.Add(v);

            foreach (var kv in entry.Tags)
                record.Tags[kv.Key] = kv.Value;

            await MemoryVectorIndex.UpsertAsync(record, ct);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to append memory vector record (best-effort)");
        }
    }

    protected virtual MemoryScope BuildMemoryScope(ChatRequest request)
    {
        var type = MemoryStoreScopeType == MemoryScopeType.Unspecified
            ? MemoryScopeType.PrivateAgent
            : MemoryStoreScopeType;

        var scopeId = MemoryStoreScopeIdOverride;
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            scopeId = type switch
            {
                MemoryScopeType.Session => TryGetContextValue(request, "session_id", "sessionId"),
                MemoryScopeType.Run => TryGetContextValue(request, "run_id", "runId"),
                MemoryScopeType.Execution => TryGetContextValue(request, "execution_id", "executionId"),
                MemoryScopeType.Graph => TryGetContextValue(request, "graph_id", "graphId"),
                MemoryScopeType.Tenant => TryGetContextValue(request, "tenant_id", "tenantId"),
                _ => Id.ToString()
            };
        }

        scopeId = string.IsNullOrWhiteSpace(scopeId) ? Id.ToString() : scopeId.Trim();

        return new MemoryScope
        {
            Type = type,
            ScopeId = scopeId
        };
    }

    protected virtual string BuildMemoryId(MemoryScope scope)
    {
        if (!string.IsNullOrWhiteSpace(MemoryIdOverride))
            return MemoryIdOverride.Trim();

        var type = scope.Type.ToString().ToLowerInvariant();
        var id = (scope.ScopeId ?? string.Empty).Trim();
        return $"{type}::{id}";
    }

    private static string? TryGetContextValue(ChatRequest request, params string[] keys)
    {
        if (request?.Context == null || request.Context.Count == 0)
            return null;

        foreach (var k in keys)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (!request.Context.TryGetValue(k, out var v)) continue;
            if (string.IsNullOrWhiteSpace(v)) continue;
            return v.Trim();
        }

        return null;
    }
}