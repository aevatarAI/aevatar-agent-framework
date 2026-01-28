using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AGUI; // TODO: Reference old namespace until Agents module migrated
using Aevatar.VibeResearching.Infrastructure.MongoDB.Workspace;

namespace Aevatar.VibeResearching.Sessions.MongoDB.Services;

/// <summary>
/// File-backed UI work trace store for session snapshots.
///
/// Purpose:
/// - Some UI projections are client-only (step/system messages, tool cards).
/// - On browser refresh, those projections disappear because SSE does not replay.
/// - We persist a small "UI snapshot" + per-run JSONL so refresh can rehydrate.
///
/// Scope (MVP):
/// - messages snapshot (bounded)
/// - message meta (agent/stepName, bounded)
/// - tool outputs (bounded)
/// - run steps (last run, bounded)
///
/// Note: This is best-effort telemetry; failures must not break runs.
/// Avoid writing on every streamed token; flush on message-end / step / tool events.
/// </summary>
public sealed class SessionUiSnapshotStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly WorkspaceService _workspace;
    private readonly ILogger<SessionUiSnapshotStore> _logger;

    public SessionUiSnapshotStore(WorkspaceService workspace, ILogger<SessionUiSnapshotStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public sealed record UiMessage(string Id, string Role, string Content);

    public sealed record UiMessageMeta(string MessageId, string Agent, string StepName, string ProviderName = "");

    public sealed record UiToolOutput(
        string MessageId,
        string ToolCallId,
        string ToolName,
        string Status, // "running" | "done"
        string? ResultPreview,
        string? Error,
        long? StartedAt = null,
        string? ProviderName = null,
        string? TargetAgent = null);

    public sealed record UiRunStep(
        string Status, // "running" | "done"
        long? StartedAt,
        long? FinishedAt);

    public sealed record UiRunStepsSnapshot(
        string RunId,
        List<string> Order,
        Dictionary<string, UiRunStep> Map);

    public sealed record UiSnapshot(
        int Version,
        string UpdatedAt,
        List<UiMessage> Messages,
        List<UiMessageMeta> MessageMeta,
        List<UiToolOutput> Tools,
        UiRunStepsSnapshot? RunSteps);

    public string GetSnapshotPath(string sessionId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var dir = Path.Combine(ws.ArtifactsDir, "ui");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ui_snapshot.json");
    }

    public async Task<UiSnapshot> LoadAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = GetSnapshotPath(ws.SessionId);

        try
        {
            if (!File.Exists(path))
                return new UiSnapshot(0, "", [], [], [], null);

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return new UiSnapshot(0, "", [], [], [], null);

            var snap = JsonSerializer.Deserialize<UiSnapshot>(json, Json);
            if (snap == null)
                return new UiSnapshot(0, "", [], [], [], null);

            return new UiSnapshot(
                Version: snap.Version,
                UpdatedAt: snap.UpdatedAt ?? "",
                Messages: snap.Messages ?? [],
                MessageMeta: snap.MessageMeta ?? [],
                Tools: snap.Tools ?? [],
                RunSteps: snap.RunSteps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ui_snapshot.json (best-effort).");
            return new UiSnapshot(0, "", [], [], [], null);
        }
    }

    public async Task SaveAsync(string sessionId, UiSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = GetSnapshotPath(ws.SessionId);

        // Atomic write via tmp
        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");

        var json = JsonSerializer.Serialize(snapshot, Json);
        await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

        File.Move(tmp, path, overwrite: true);
    }

    public string GetRunEventsPath(string sessionId, string runId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var rid = (runId ?? string.Empty).Trim();
        if (rid.Length == 0) rid = "run";

        var dir = Path.Combine(ws.RunsDir, rid);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ui_events.jsonl");
    }

    public async Task AppendRunEventAsync(string sessionId, string runId, object evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ct.ThrowIfCancellationRequested();

        try
        {
            var path = GetRunEventsPath(sessionId, runId);
            var line = JsonSerializer.Serialize(evt, Json);
            await File.AppendAllTextAsync(path, line + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        }
        catch
        {
            // best-effort only
        }
    }

    public static List<UiMessage> FromAgUiMessages(IEnumerable<AgUiMessage> messages, int maxMessages)
    {
        maxMessages = Math.Clamp(maxMessages, 0, 400);
        if (maxMessages == 0) return [];

        var list = new List<UiMessage>();
        foreach (var m in messages)
        {
            if (m == null) continue;
            var id = (m.Id ?? string.Empty).Trim();
            var role = (m.Role ?? string.Empty).Trim();
            if (id.Length == 0 || role.Length == 0) continue;

            var content = (m.Content ?? string.Empty).Replace("\r", "");
            if (content.Length > 120_000) content = content[..120_000];
            list.Add(new UiMessage(id, role, content));
        }

        if (list.Count > maxMessages)
            list = list.Skip(Math.Max(0, list.Count - maxMessages)).ToList();

        return list;
    }
}
