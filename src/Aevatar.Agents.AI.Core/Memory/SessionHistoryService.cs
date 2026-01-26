using Aevatar.Agents.Abstractions.Memory;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core;

// ============================================================
//  SessionHistoryService
//
//  中文 + ASCII:
//  - 基于 IMemoryStore 的 session 历史读取（跨 Agent 聚合）。
//  - 统一输出为时间正序（不同 Store 默认排序不一致）。
// ============================================================
public sealed class SessionHistoryService
{
    private readonly IMemoryStore _memoryStore;

    public SessionHistoryService(IMemoryStore memoryStore)
    {
        _memoryStore = memoryStore ?? throw new ArgumentNullException(nameof(memoryStore));
    }

    public async Task<IReadOnlyList<MemoryEntry>> GetSessionEntriesAsync(
        string sessionId,
        int limit = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Array.Empty<MemoryEntry>();

        var memoryId = BuildSessionMemoryId(sessionId);
        var entries = await _memoryStore.ListEntriesAsync(memoryId, limit, ct);

        return entries
            .OrderBy(e => ToDateTimeUtc(e.CreatedAt))
            .ToList();
    }

    public static string BuildSessionMemoryId(string sessionId)
    {
        var id = (sessionId ?? string.Empty).Trim();
        return $"{MemoryScopeType.Session.ToString().ToLowerInvariant()}::{id}";
    }

    private static DateTime ToDateTimeUtc(Timestamp? timestamp)
    {
        if (timestamp is null || (timestamp.Seconds == 0 && timestamp.Nanos == 0))
            return DateTime.MinValue;

        var dt = timestamp.ToDateTime();
        return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
    }
}
