using System.Linq;
using System.Text;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Platform;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Platform.Core.Sessions;

// ============================================================
//  SessionService
//
//  说明：
//  - 基于 IEventStore 的会话管理（list/show/resume/export/import）
//  - 会话状态写入 state.json（本地缓存，便于列表与快速恢复）
//  - 事件流仍然是 append-only（AgentStateEvent + Any）
// ============================================================
public sealed class SessionService
{

    private static readonly TypeRegistry SessionTypeRegistry = TypeRegistry.FromFiles(
        AbstrationsMessagesReflection.Descriptor,
        PlatformMessagesReflection.Descriptor);

    private static readonly JsonFormatter SessionJsonFormatter = new(
        JsonFormatter.Settings.Default
            .WithFormatDefaultValues(true)
            .WithPreserveProtoFieldNames(true)
            .WithTypeRegistry(SessionTypeRegistry));

    private static readonly JsonParser SessionJsonParser = new(
        JsonParser.Settings.Default
            .WithIgnoreUnknownFields(true)
            .WithTypeRegistry(SessionTypeRegistry));

    private readonly IEventStore _eventStore;
    private readonly string _sessionsRoot;

    public SessionService(IEventStore eventStore, string configDirectory)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _sessionsRoot = Path.Combine(configDirectory, "sessions");
        Directory.CreateDirectory(_sessionsRoot);
    }

    public string SessionsRoot => _sessionsRoot;

    public async Task<string> CreateSessionAsync(PlatformSessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sessionId = string.IsNullOrWhiteSpace(state.SessionId)
            ? GenerateSessionId()
            : state.SessionId.Trim();

        if (SessionExists(sessionId))
            throw new InvalidOperationException($"Session '{sessionId}' already exists.");

        state.SessionId = sessionId;
        await SaveSessionStateAsync(sessionId, state, ct);

        var initEvent = BuildAgentStateEvent(sessionId, state, PlatformSessionState.Descriptor.FullName);
        await _eventStore.AppendEventsAsync(sessionId, [initEvent], expectedVersion: 0, agentTypeName: null, ct);

        return sessionId;
    }

    public async Task UpdateSessionStateAsync(PlatformSessionState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sessionId = (state.SessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            throw new ArgumentException("Session id is required.", nameof(state));

        if (!SessionExists(sessionId))
            throw new InvalidOperationException($"Session '{sessionId}' not found.");

        state.SessionId = sessionId;
        await SaveSessionStateAsync(sessionId, state, ct);

        var expected = await _eventStore.GetLatestVersionAsync(sessionId, agentTypeName: null, ct);
        var stateEvent = BuildAgentStateEvent(sessionId, state, PlatformSessionState.Descriptor.FullName);
        await _eventStore.AppendEventsAsync(sessionId, [stateEvent], expected, agentTypeName: null, ct);
    }

    public async Task AppendEventAsync(string sessionId, PlatformSessionEvent evt, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(evt);

        var expected = await _eventStore.GetLatestVersionAsync(sessionId, agentTypeName: null, ct);
        var stateEvent = BuildAgentStateEvent(sessionId, evt, PlatformSessionEvent.Descriptor.FullName);

        await _eventStore.AppendEventsAsync(sessionId, [stateEvent], expected, agentTypeName: null, ct);
    }

    public async Task<PlatformSessionState?> GetSessionStateAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var statePath = GetStatePath(sessionId);
        if (File.Exists(statePath))
        {
            var json = await File.ReadAllTextAsync(statePath, ct);
            if (!string.IsNullOrWhiteSpace(json))
                return SessionJsonParser.Parse<PlatformSessionState>(json);
        }

        var events = await _eventStore.GetEventsAsync(sessionId, agentTypeName: null, ct: ct);
        return TryExtractLatestState(events);
    }

    public async Task<IReadOnlyList<PlatformSessionEvent>> GetSessionEventsAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var events = await _eventStore.GetEventsAsync(sessionId, agentTypeName: null, ct: ct);
        var list = new List<PlatformSessionEvent>();

        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            if (evt.EventData == null || !evt.EventData.Is(PlatformSessionEvent.Descriptor))
                continue;

            list.Add(evt.EventData.Unpack<PlatformSessionEvent>());
        }

        return list;
    }

    public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_sessionsRoot))
        {
            return Task.FromResult<IReadOnlyList<SessionSummary>>(Array.Empty<SessionSummary>());
        }

        var list = new List<SessionSummary>();
        foreach (var dir in Directory.EnumerateDirectories(_sessionsRoot))
        {
            ct.ThrowIfCancellationRequested();

            var sessionId = Path.GetFileName(dir);
            var state = TryReadStateFromDisk(dir);
            var lastActivity = TryGetLastWriteTimeUtc(dir);

            list.Add(new SessionSummary(
                SessionId: sessionId,
                Profile: state?.Profile ?? string.Empty,
                ActiveWorkflow: state?.ActiveWorkflow ?? string.Empty,
                LastActivityUtc: lastActivity));
        }

        var ordered = list
            .OrderByDescending(x => x.LastActivityUtc ?? DateTime.MinValue)
            .ToList();

        return Task.FromResult<IReadOnlyList<SessionSummary>>(ordered);
    }

    public async Task ExportSessionAsync(string sessionId, string outputPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var events = await _eventStore.GetEventsAsync(sessionId, agentTypeName: null, ct: ct);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("session_id", sessionId);
        writer.WriteString("exported_at", DateTime.UtcNow.ToString("O"));

        writer.WriteStartArray("events");
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();

            var json = SessionJsonFormatter.Format(evt);
            using var doc = JsonDocument.Parse(json);
            doc.RootElement.WriteTo(writer);
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        await writer.FlushAsync(ct);
    }

    public async Task<string> ImportSessionAsync(
        string inputPath,
        bool overwriteExisting = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        var export = await ReadExportAsync(inputPath, ct);
        var targetId = string.IsNullOrWhiteSpace(export.SessionId)
            ? GenerateSessionId()
            : export.SessionId.Trim();

        if (SessionExists(targetId))
        {
            if (!overwriteExisting)
                targetId = GenerateSessionId();
            else
                DeleteSessionFiles(targetId);
        }

        var ordered = export.Events
            .OrderBy(e => e.Version)
            .ToList();

        foreach (var evt in ordered)
        {
            evt.AgentId = targetId;
            NormalizeImportedEventId(evt);
            NormalizeImportedStateSessionId(evt, targetId);
        }

        await _eventStore.AppendEventsAsync(targetId, ordered, expectedVersion: 0, agentTypeName: null, ct);

        var state = TryExtractLatestState(ordered);
        if (state != null)
            await SaveSessionStateAsync(targetId, state, ct);

        return targetId;
    }

    private async Task SaveSessionStateAsync(string sessionId, PlatformSessionState state, CancellationToken ct)
    {
        var sessionDir = GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDir);

        var json = SessionJsonFormatter.Format(state);
        await File.WriteAllTextAsync(GetStatePath(sessionId), json, new UTF8Encoding(false), ct);
    }

    private PlatformSessionState? TryExtractLatestState(IEnumerable<AgentStateEvent> events)
    {
        PlatformSessionState? latest = null;
        foreach (var evt in events)
        {
            if (evt.EventData == null || !evt.EventData.Is(PlatformSessionState.Descriptor))
                continue;

            latest = evt.EventData.Unpack<PlatformSessionState>();
        }

        return latest;
    }

    private static void NormalizeImportedEventId(AgentStateEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.EventId))
            evt.EventId = Guid.NewGuid().ToString("N");
    }

    private static void NormalizeImportedStateSessionId(AgentStateEvent evt, string sessionId)
    {
        if (evt.EventData == null || !evt.EventData.Is(PlatformSessionState.Descriptor))
            return;

        var state = evt.EventData.Unpack<PlatformSessionState>();
        if (string.Equals(state.SessionId, sessionId, StringComparison.Ordinal))
            return;

        state.SessionId = sessionId;
        evt.EventData = Any.Pack(state);
    }

    private static AgentStateEvent BuildAgentStateEvent(
        string sessionId,
        IMessage payload,
        string eventType)
    {
        return new AgentStateEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            EventType = eventType ?? string.Empty,
            EventData = Any.Pack(payload),
            AgentId = sessionId
        };
    }

    private bool SessionExists(string sessionId)
    {
        var dir = GetSessionDirectory(sessionId);
        if (Directory.Exists(dir))
            return true;

        if (_eventStore is FileEventStore fileStore)
            return fileStore.SessionExists(sessionId, agentTypeName: null);

        return false;
    }

    private void DeleteSessionFiles(string sessionId)
    {
        var dir = GetSessionDirectory(sessionId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private string GetSessionDirectory(string sessionId)
        => Path.Combine(_sessionsRoot, SanitizeSegment(sessionId));

    private string GetStatePath(string sessionId)
        => Path.Combine(GetSessionDirectory(sessionId), "state.json");

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

    private static PlatformSessionState? TryReadStateFromDisk(string sessionDir)
    {
        try
        {
            var path = Path.Combine(sessionDir, "state.json");
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return SessionJsonParser.Parse<PlatformSessionState>(json);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? TryGetLastWriteTimeUtc(string sessionDir)
    {
        try
        {
            var eventsPath = Path.Combine(sessionDir, "events.jsonl");
            if (File.Exists(eventsPath))
                return File.GetLastWriteTimeUtc(eventsPath);

            var statePath = Path.Combine(sessionDir, "state.json");
            if (File.Exists(statePath))
                return File.GetLastWriteTimeUtc(statePath);
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string GenerateSessionId()
        => Guid.NewGuid().ToString("N");

    private static async Task<SessionExport> ReadExportAsync(string inputPath, CancellationToken ct)
    {
        await using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        var sessionId = root.TryGetProperty("session_id", out var idEl)
            ? idEl.GetString() ?? string.Empty
            : string.Empty;

        var events = new List<AgentStateEvent>();
        if (root.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in eventsEl.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var json = item.GetRawText();
                events.Add(SessionJsonParser.Parse<AgentStateEvent>(json));
            }
        }

        return new SessionExport(sessionId, events);
    }

    public sealed record SessionSummary(
        string SessionId,
        string Profile,
        string ActiveWorkflow,
        DateTime? LastActivityUtc);

    private sealed record SessionExport(string SessionId, List<AgentStateEvent> Events);
}


