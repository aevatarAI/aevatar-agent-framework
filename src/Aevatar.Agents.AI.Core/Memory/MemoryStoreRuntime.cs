using Aevatar.Agents.Abstractions.Memory;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    /// <summary>
    /// MemoryStore/VectorIndex runtime extracted from <see cref="AIGAgentBase.MemoryStore.cs"/>.
    ///
    /// 中文 + ASCII:
    /// - 目标：把“平台能力”的内部状态/细节逻辑从 base 收敛出去，base 只保留开关/注入点/薄 façade
    /// - 失败语义：best-effort（任何异常都吞掉并记录 debug），绝不影响 Chat 主路径
    /// </summary>
    private sealed class MemoryStoreRuntime(AIGAgentBase owner)
    {
        private readonly AIGAgentBase _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        internal async Task AppendChatMemoryAsync(
            AevatarChatRole role,
            string content,
            ChatRequest request,
            CancellationToken ct)
        {
            await AppendChatMemoryCoreAsync(
                role,
                content,
                request,
                ct,
                enabled: _owner.EnableMemoryStoreAppend,
                scopeOverride: null,
                memoryIdOverride: null);
        }

        internal async Task AppendChatMemoryOverrideAsync(
            AevatarChatRole role,
            string content,
            ChatRequest request,
            MemoryScope scope,
            string memoryId,
            CancellationToken ct)
        {
            await AppendChatMemoryCoreAsync(
                role,
                content,
                request,
                ct,
                enabled: true,
                scopeOverride: scope,
                memoryIdOverride: memoryId);
        }

        internal async Task AppendMemoryVectorAsync(MemoryEntry entry, CancellationToken ct)
        {
            if (!_owner.EnableMemoryVectorIndexAppend)
                return;

            if (_owner.MemoryVectorIndex == null)
                return;

            if (!_owner.TryGetEmbeddingGenerator(out _))
                return;

            // Keep inputs bounded (avoid huge embedding calls).
            const int maxChars = 2000;
            var text = (entry.Content ?? string.Empty).Replace("\r", "").Trim();
            if (text.Length == 0)
                return;

            if (text.Length > maxChars)
                text = text[..maxChars];

            await BestEffort.TryAsync(
                async () =>
                {
                    var embedding = await _owner.GenerateEmbeddingAsync(text, cancellationToken: ct);
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

                    await _owner.MemoryVectorIndex.UpsertAsync(record, ct);
                },
                _owner.Logger,
                LogLevel.Debug,
                "Failed to append memory vector record (best-effort)");
        }

        private async Task AppendChatMemoryCoreAsync(
            AevatarChatRole role,
            string content,
            ChatRequest request,
            CancellationToken ct,
            bool enabled,
            MemoryScope? scopeOverride,
            string? memoryIdOverride)
        {
            if (!enabled)
                return;

            if (_owner.MemoryStore == null)
                return;

            if (string.IsNullOrWhiteSpace(content))
                return;

            await BestEffort.TryAsync(
                async () =>
                {
                    var scope = scopeOverride ?? _owner.BuildMemoryScope(request);
                    if (scope == null)
                        return;

                    var memoryId = !string.IsNullOrWhiteSpace(memoryIdOverride)
                        ? memoryIdOverride.Trim()
                        : _owner.BuildMemoryId(scope);

                    if (string.IsNullOrWhiteSpace(memoryId))
                        return;

                    var runId = TryGetContextValue(request, "run_id", "runId") ?? string.Empty;

                    var entry = new MemoryEntry
                    {
                        EntryId = Guid.NewGuid().ToString("N"),
                        MemoryId = memoryId,
                        Scope = scope,
                        RunId = runId,
                        AgentId = _owner.Id.ToString(),
                        Role = role.ToString().ToLowerInvariant(),
                        Content = content.Trim(),
                        CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                    };

                    // Lightweight tags for governance / debug
                    entry.Tags["agent_type"] = _owner.GetType().FullName ?? _owner.GetType().Name;
                    entry.Tags["request_id"] = request.RequestId ?? string.Empty;
                    entry.Tags["scope_type"] = scope.Type.ToString();
                    entry.Tags["scope_id"] = scope.ScopeId ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(request.StageHint))
                        entry.Tags["stage_hint"] = request.StageHint!;

                    await _owner.MemoryStore.AppendAsync(entry, ct);

                    // Optional: persist vector record (best-effort, does NOT affect chat).
                    await AppendMemoryVectorAsync(entry, ct);
                },
                _owner.Logger,
                LogLevel.Debug,
                "Failed to append chat memory (best-effort)");
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
}


