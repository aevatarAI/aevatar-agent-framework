using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Platform;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Aevatar.Platform.Core.Sessions;

// ============================================================
//  FileEventStore (Platform sessions)
//
//  说明：
//  - 基于 JSONL 的 append-only 事件存储（每行一个 AgentStateEvent JSON）
//  - 采用 optimistic concurrency（expectedVersion）
//  - 提供简单的 bounded guard（最大事件数 / 最大文件字节数）
// ============================================================
public sealed class FileEventStore : IEventStore
{
    public const string DefaultAgentTypeName = "platform_session";
    private const string MetaFileName = "meta.json";

    private static readonly TypeRegistry EventTypeRegistry = TypeRegistry.FromFiles(
        AbstrationsMessagesReflection.Descriptor,
        PlatformMessagesReflection.Descriptor,
        AiAbstractionsMessagesReflection.Descriptor,
        ToolMessagesReflection.Descriptor);

    private static readonly JsonFormatter EventJsonFormatter = new(
        JsonFormatter.Settings.Default
            .WithFormatDefaultValues(true)
            .WithPreserveProtoFieldNames(true)
            .WithTypeRegistry(EventTypeRegistry));

    private static readonly JsonParser EventJsonParser = new(
        JsonParser.Settings.Default
            .WithIgnoreUnknownFields(true)
            .WithTypeRegistry(EventTypeRegistry));

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly int _maxEventsPerStream;
    private readonly long _maxBytesPerStream;

    public FileEventStore(
        string rootDirectory,
        int maxEventsPerStream = 20000,
        long maxBytesPerStream = 64 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);

        _maxEventsPerStream = Math.Max(1, maxEventsPerStream);
        _maxBytesPerStream = Math.Max(1, maxBytesPerStream);
    }

    public string RootDirectory { get; }

    public async Task<long> AppendEventsAsync(
        string agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(events);

        var list = events.ToList();
        if (list.Count == 0)
            return await GetLatestVersionAsync(agentId, agentTypeName, ct);

        var streamKey = BuildStreamKey(agentId, agentTypeName);
        var gate = _locks.GetOrAdd(streamKey, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        try
        {
            var eventsPath = GetEventsPath(agentId, agentTypeName);
            Directory.CreateDirectory(Path.GetDirectoryName(eventsPath)!);

            var stats = ReadStreamStats(eventsPath);
            if (stats.LatestVersion != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"Concurrency conflict: expected version {expectedVersion}, got {stats.LatestVersion}");
            }

            EnsureBounds(stats, list, eventsPath);

            var newVersion = stats.LatestVersion;
            using var stream = new FileStream(eventsPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));

            foreach (var evt in list)
            {
                ct.ThrowIfCancellationRequested();
                newVersion = NormalizeEvent(agentId, evt, newVersion);

                var json = EventJsonFormatter.Format(evt);
                writer.WriteLine(json);
            }

            await writer.FlushAsync();

            var newCount = stats.EventCount + list.Count;
            var newBytes = new FileInfo(eventsPath).Length;
            WriteMeta(GetMetaPath(eventsPath), new StreamStats(newVersion, newCount, newBytes));

            return newVersion;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        string agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var eventsPath = GetEventsPath(agentId, agentTypeName);
        if (!File.Exists(eventsPath))
        {
            return Task.FromResult<IReadOnlyList<AgentStateEvent>>(Array.Empty<AgentStateEvent>());
        }

        var list = new List<AgentStateEvent>();
        foreach (var line in File.ReadLines(eventsPath))
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = EventJsonParser.Parse<AgentStateEvent>(line);
            if (fromVersion.HasValue && evt.Version < fromVersion.Value)
                continue;

            if (toVersion.HasValue && evt.Version > toVersion.Value)
                continue;

            list.Add(evt);
        }

        var ordered = list
            .OrderBy(e => e.Version)
            .ToList();

        if (maxCount.HasValue)
            ordered = ordered.Take(maxCount.Value).ToList();

        return Task.FromResult<IReadOnlyList<AgentStateEvent>>(ordered);
    }

    public Task<long> GetLatestVersionAsync(
        string agentId,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var eventsPath = GetEventsPath(agentId, agentTypeName);
        var stats = ReadStreamStats(eventsPath);
        return Task.FromResult(stats.LatestVersion);
    }

    public bool SessionExists(string agentId, string? agentTypeName = null)
    {
        if (string.IsNullOrWhiteSpace(agentId))
            return false;

        return File.Exists(GetEventsPath(agentId, agentTypeName));
    }

    private void EnsureBounds(StreamStats stats, IReadOnlyList<AgentStateEvent> events, string eventsPath)
    {
        if (stats.EventCount + events.Count > _maxEventsPerStream)
        {
            throw new InvalidOperationException(
                $"Event store limit exceeded: {stats.EventCount + events.Count}/{_maxEventsPerStream}");
        }

        var appendBytes = EstimateAppendBytes(events);
        if (stats.FileBytes + appendBytes > _maxBytesPerStream)
        {
            throw new InvalidOperationException(
                $"Event store size limit exceeded: {stats.FileBytes + appendBytes}/{_maxBytesPerStream} bytes");
        }
    }

    private static long EstimateAppendBytes(IReadOnlyList<AgentStateEvent> events)
    {
        var bytes = 0L;
        foreach (var evt in events)
        {
            var json = EventJsonFormatter.Format(evt);
            bytes += Encoding.UTF8.GetByteCount(json) + 1;
        }

        return bytes;
    }

    private static long NormalizeEvent(string agentId, AgentStateEvent evt, long currentVersion)
    {
        var nextVersion = currentVersion + 1;
        evt.Version = nextVersion;

        if (string.IsNullOrWhiteSpace(evt.EventId))
            evt.EventId = Guid.NewGuid().ToString("N");

        if (evt.Timestamp == null || evt.Timestamp == default)
            evt.Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);

        if (string.IsNullOrWhiteSpace(evt.AgentId))
            evt.AgentId = agentId;

        if (string.IsNullOrWhiteSpace(evt.EventType) && evt.EventData != null)
            evt.EventType = evt.EventData.TypeUrl ?? string.Empty;

        return nextVersion;
    }

    private StreamStats ReadStreamStats(string eventsPath)
    {
        if (!File.Exists(eventsPath))
            return new StreamStats(0, 0, 0);

        var bytes = new FileInfo(eventsPath).Length;
        var metaPath = GetMetaPath(eventsPath);

        if (TryReadMeta(metaPath, out var meta) && meta.FileBytes == bytes)
            return new StreamStats(meta.LatestVersion, meta.EventCount, bytes);

        var scanned = ScanEvents(eventsPath, bytes);
        WriteMeta(metaPath, scanned);
        return scanned;
    }

    private static StreamStats ScanEvents(string eventsPath, long fileBytes)
    {
        long latest = 0;
        var count = 0;
        foreach (var line in File.ReadLines(eventsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (TryReadVersion(line, out var version) && version > latest)
                latest = version;

            count++;
        }

        return new StreamStats(latest, count, fileBytes);
    }

    private static string GetMetaPath(string eventsPath)
    {
        var dir = Path.GetDirectoryName(eventsPath) ?? string.Empty;
        return Path.Combine(dir, MetaFileName);
    }

    private static bool TryReadMeta(string metaPath, out StreamMeta meta)
    {
        meta = new StreamMeta(0, 0, 0);
        try
        {
            if (!File.Exists(metaPath))
                return false;

            var json = File.ReadAllText(metaPath);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            var parsed = JsonSerializer.Deserialize<StreamMeta>(json);
            if (parsed == null)
                return false;

            meta = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteMeta(string metaPath, StreamStats stats)
    {
        try
        {
            var meta = new StreamMeta(stats.LatestVersion, stats.EventCount, stats.FileBytes);
            var json = JsonSerializer.Serialize(meta);
            File.WriteAllText(metaPath, json, new UTF8Encoding(false));
        }
        catch
        {
            // best-effort only
        }
    }

    private static bool TryReadVersion(string jsonLine, out long version)
    {
        version = 0;
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            if (!doc.RootElement.TryGetProperty("version", out var value))
                return false;

            return value.TryGetInt64(out version);
        }
        catch
        {
            return false;
        }
    }

    private string GetEventsPath(string agentId, string? agentTypeName)
    {
        var dir = GetStreamDirectory(agentId, agentTypeName);
        return Path.Combine(dir, "events.jsonl");
    }

    private string GetStreamDirectory(string agentId, string? agentTypeName)
    {
        var safeId = SanitizeSegment(agentId);
        if (string.IsNullOrWhiteSpace(agentTypeName))
            return Path.Combine(RootDirectory, safeId);

        var safeType = SanitizeSegment(agentTypeName.Trim());
        return Path.Combine(RootDirectory, safeType, safeId);
    }

    private static string BuildStreamKey(string agentId, string? agentTypeName)
        => string.IsNullOrWhiteSpace(agentTypeName)
            ? agentId
            : $"{agentTypeName}::{agentId}";

    private static string SanitizeSegment(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private sealed record StreamStats(long LatestVersion, int EventCount, long FileBytes);

    private sealed record StreamMeta(long LatestVersion, int EventCount, long FileBytes);
}


