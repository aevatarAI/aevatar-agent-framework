using Aevatar.Agents.Abstractions.Memory;

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

    private MemoryStoreRuntime? _memoryStoreRuntime;
    private MemoryStoreRuntime MemoryRuntime => _memoryStoreRuntime ??= new MemoryStoreRuntime(this);

    protected virtual async Task AppendChatMemoryAsync(
        AevatarChatRole role,
        string content,
        ChatRequest request,
        CancellationToken ct)
    {
        await MemoryRuntime.AppendChatMemoryAsync(role, content, request, ct);
    }

    protected virtual async Task AppendMemoryVectorAsync(MemoryEntry entry, CancellationToken ct)
    {
        await MemoryRuntime.AppendMemoryVectorAsync(entry, ct);
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

    // NOTE: helper kept in base because BuildMemoryScope is virtual (override point).
    // Runtime logic can have its own parsing, but base must keep a stable helper for derived overrides.
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