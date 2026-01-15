using System.Text;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe.Delivery;

// ============================================================
//  DeliveryCenterStore (File-SSoT)
//
//  Canonical files under workspace/sessions/{sessionId}/deliverables/:
//  - conclusions.json        : SraConclusionCardsSnapshot
//  - evidence.json           : SraEvidenceTableSnapshot
//  - tasks.json              : SraNextTasksSnapshot
//  - delivery_snapshot.json  : SraDeliveryCenterSnapshot (paths + changedSummary)
//
//  Stored as Protobuf-JSON (review friendly).
//  Keep lists bounded to prevent "paper becomes a log" failure mode (issue #67).
// ============================================================

public sealed class DeliveryCenterStore
{
    private const string ConclusionsFile = "conclusions.json";
    private const string EvidenceFile = "evidence.json";
    private const string TasksFile = "tasks.json";
    private const string DeliverySnapshotFile = "delivery_snapshot.json";

    private const int MaxConclusions = 32;
    private const int MaxEvidence = 80;
    private const int MaxTasks = 64;

    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter Formatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: Registry));
    private static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true).WithTypeRegistry(Registry));

    private readonly WorkspaceService _workspace;
    private readonly ILogger<DeliveryCenterStore> _logger;

    public DeliveryCenterStore(WorkspaceService workspace, ILogger<DeliveryCenterStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public (string Conclusions, string Evidence, string Tasks, string DeliverySnapshot) GetPaths(string sessionId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return (
            Path.Combine(ws.DeliverablesDir, ConclusionsFile),
            Path.Combine(ws.DeliverablesDir, EvidenceFile),
            Path.Combine(ws.DeliverablesDir, TasksFile),
            Path.Combine(ws.DeliverablesDir, DeliverySnapshotFile)
        );
    }

    // ------------------------------------------------------------
    //  Loaders (best-effort)
    // ------------------------------------------------------------

    public async Task<SraDeliveryCenterSnapshot> LoadDeliverySnapshotAsync(string sessionId, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = Path.Combine(ws.DeliverablesDir, DeliverySnapshotFile);
        return await LoadOrEmptyAsync(path, ws.SessionId, () => new SraDeliveryCenterSnapshot
        {
            SessionId = ws.SessionId,
            Version = 0,
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        }, ct);
    }

    public async Task<SraConclusionCardsSnapshot> LoadConclusionsAsync(string sessionId, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = Path.Combine(ws.DeliverablesDir, ConclusionsFile);
        return await LoadOrEmptyAsync(path, ws.SessionId, () => new SraConclusionCardsSnapshot
        {
            SessionId = ws.SessionId,
            Version = 0,
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        }, ct);
    }

    public async Task<SraEvidenceTableSnapshot> LoadEvidenceAsync(string sessionId, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = Path.Combine(ws.DeliverablesDir, EvidenceFile);
        return await LoadOrEmptyAsync(path, ws.SessionId, () => new SraEvidenceTableSnapshot
        {
            SessionId = ws.SessionId,
            Version = 0,
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        }, ct);
    }

    public async Task<SraNextTasksSnapshot> LoadTasksAsync(string sessionId, CancellationToken ct)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = Path.Combine(ws.DeliverablesDir, TasksFile);
        return await LoadOrEmptyAsync(path, ws.SessionId, () => new SraNextTasksSnapshot
        {
            SessionId = ws.SessionId,
            Version = 0,
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        }, ct);
    }

    public async Task<(SraDeliveryCenterSnapshot Delivery, SraConclusionCardsSnapshot Conclusions, SraEvidenceTableSnapshot Evidence, SraNextTasksSnapshot Tasks)>
        LoadAllAsync(string sessionId, CancellationToken ct)
    {
        var delivery = await LoadDeliverySnapshotAsync(sessionId, ct);
        var conclusions = await LoadConclusionsAsync(sessionId, ct);
        var evidence = await LoadEvidenceAsync(sessionId, ct);
        var tasks = await LoadTasksAsync(sessionId, ct);
        return (delivery, conclusions, evidence, tasks);
    }

    // ------------------------------------------------------------
    //  Savers (atomic)
    // ------------------------------------------------------------

    public async Task<SraDeliveryCenterSnapshot> SaveDeliverySnapshotAsync(string sessionId, SraDeliveryCenterSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        snapshot.SessionId = ws.SessionId;
        snapshot.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        snapshot.ChangedSummary = Bound(snapshot.ChangedSummary, 4000);

        // Normalize paths to be relative-ish and stable.
        snapshot.PaperOutlinePath = NormalizeRel(snapshot.PaperOutlinePath);
        snapshot.PaperDraftPath = NormalizeRel(snapshot.PaperDraftPath);
        snapshot.ConclusionsPath = NormalizeRel(snapshot.ConclusionsPath);
        snapshot.EvidencePath = NormalizeRel(snapshot.EvidencePath);
        snapshot.TasksPath = NormalizeRel(snapshot.TasksPath);

        var path = Path.Combine(ws.DeliverablesDir, DeliverySnapshotFile);
        await WriteAtomicAsync(ws, path, snapshot, ct);
        return snapshot;
    }

    public async Task<SraConclusionCardsSnapshot> SaveConclusionsAsync(string sessionId, SraConclusionCardsSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        snapshot.SessionId = ws.SessionId;
        snapshot.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        BoundRepeated(snapshot.Items, MaxConclusions);
        foreach (var c in snapshot.Items)
        {
            if (c == null) continue;
            c.CardId = Bound(c.CardId, 80);
            c.Claim = Bound(c.Claim, 220);
            c.Notes = Bound(c.Notes, 600);
            BoundStringList(c.EvidencePaths, 30, 240);
            BoundStringList(c.CounterEvidencePaths, 30, 240);
            BoundStringList(c.RelatedDagNodeIds, 30, 120);
            c.UpdatedAt ??= snapshot.UpdatedAt;
        }

        var path = Path.Combine(ws.DeliverablesDir, ConclusionsFile);
        await WriteAtomicAsync(ws, path, snapshot, ct);
        return snapshot;
    }

    public async Task<SraEvidenceTableSnapshot> SaveEvidenceAsync(string sessionId, SraEvidenceTableSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        snapshot.SessionId = ws.SessionId;
        snapshot.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        BoundRepeated(snapshot.Items, MaxEvidence);
        foreach (var e in snapshot.Items)
        {
            if (e == null) continue;
            e.EvidenceId = Bound(e.EvidenceId, 80);
            e.Title = Bound(e.Title, 160);
            e.Path = NormalizeRel(e.Path);
            e.Excerpt = Bound(e.Excerpt, 280);
            e.Relevance = Bound(e.Relevance, 240);
            e.UpdatedAt ??= snapshot.UpdatedAt;
        }

        var path = Path.Combine(ws.DeliverablesDir, EvidenceFile);
        await WriteAtomicAsync(ws, path, snapshot, ct);
        return snapshot;
    }

    public async Task<SraNextTasksSnapshot> SaveTasksAsync(string sessionId, SraNextTasksSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        snapshot.SessionId = ws.SessionId;
        snapshot.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        BoundRepeated(snapshot.Items, MaxTasks);
        foreach (var t in snapshot.Items)
        {
            if (t == null) continue;
            t.TaskId = Bound(t.TaskId, 80);
            t.Title = Bound(t.Title, 160);
            t.Detail = Bound(t.Detail, 280);
            t.Priority = Math.Clamp(t.Priority, 0, 100);
            BoundStringList(t.BlockedBy, 20, 240);
            t.UpdatedAt ??= snapshot.UpdatedAt;
        }

        var path = Path.Combine(ws.DeliverablesDir, TasksFile);
        await WriteAtomicAsync(ws, path, snapshot, ct);
        return snapshot;
    }

    // ------------------------------------------------------------
    //  UI helper: bounded "latest view"
    // ------------------------------------------------------------

    public async Task<object> GetSnapshotForUiAsync(string sessionId, CancellationToken ct)
    {
        var (delivery, conclusions, evidence, tasks) = await LoadAllAsync(sessionId, ct);

        return new
        {
            delivery = new
            {
                version = delivery.Version,
                updatedAt = delivery.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
                changedSummary = delivery.ChangedSummary ?? "",
                paperOutlinePath = delivery.PaperOutlinePath ?? "",
                paperDraftPath = delivery.PaperDraftPath ?? "",
                conclusionsPath = delivery.ConclusionsPath ?? "",
                evidencePath = delivery.EvidencePath ?? "",
                tasksPath = delivery.TasksPath ?? ""
            },
            conclusions = conclusions.Items.Take(12).Select(c => new
            {
                cardId = c.CardId,
                claim = c.Claim,
                confidence = c.Confidence.ToString(),
                evidencePaths = c.EvidencePaths.Take(8).ToList(),
                counterEvidencePaths = c.CounterEvidencePaths.Take(8).ToList(),
                relatedDagNodeIds = c.RelatedDagNodeIds.Take(8).ToList(),
                notes = c.Notes ?? ""
            }).ToList(),
            evidence = evidence.Items.Take(20).Select(e => new
            {
                evidenceId = e.EvidenceId,
                title = e.Title,
                path = e.Path,
                excerpt = e.Excerpt ?? "",
                relevance = e.Relevance ?? ""
            }).ToList(),
            tasks = tasks.Items.Take(20).Select(t => new
            {
                taskId = t.TaskId,
                title = t.Title,
                detail = t.Detail,
                priority = t.Priority,
                blockedBy = t.BlockedBy.Take(8).ToList()
            }).ToList()
        };
    }

    // ============================================================
    //  Internal helpers
    // ============================================================

    private async Task<T> LoadOrEmptyAsync<T>(string path, string sessionId, Func<T> createEmpty, CancellationToken ct)
        where T : class, IMessage<T>, new()
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(path))
                return createEmpty();

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return createEmpty();

            var msg = Parser.Parse<T>(json);

            // Best-effort: fill session_id if missing.
            var prop = msg.Descriptor.FindFieldByName("session_id");
            if (prop != null)
            {
                var existing = prop.Accessor.GetValue(msg)?.ToString();
                if (string.IsNullOrWhiteSpace(existing))
                    prop.Accessor.SetValue(msg, sessionId);
            }

            return msg;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load deliverables file (best-effort): {Path}", path);
            return createEmpty();
        }
    }

    private async Task WriteAtomicAsync<T>(WorkspacePaths ws, string targetPath, T message, CancellationToken ct)
        where T : class, IMessage<T>
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(ws.TmpDir);
        Directory.CreateDirectory(ws.DeliverablesDir);

        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");
        var json = Formatter.Format(message);
        await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct);
        File.Move(tmp, targetPath, overwrite: true);
    }

    private static void BoundRepeated<T>(RepeatedField<T> list, int max)
    {
        if (list == null) return;
        max = Math.Clamp(max, 0, 5000);
        if (list.Count <= max) return;
        while (list.Count > max)
            list.RemoveAt(list.Count - 1);
    }

    private static void BoundStringList(RepeatedField<string> list, int maxItems, int maxChars)
    {
        if (list == null) return;
        maxItems = Math.Clamp(maxItems, 0, 500);
        maxChars = Math.Clamp(maxChars, 20, 5000);

        if (list.Count > maxItems)
        {
            while (list.Count > maxItems)
                list.RemoveAt(list.Count - 1);
        }

        for (var i = 0; i < list.Count; i++)
            list[i] = Bound(list[i], maxChars);
    }

    private static string NormalizeRel(string? value)
    {
        var s = (value ?? string.Empty).Replace('\\', '/').Trim();
        if (s.Length == 0) return string.Empty;
        if (s.StartsWith("/", StringComparison.Ordinal)) s = s.TrimStart('/');

        // Remove traversal segments.
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var safe = parts.Where(p => p != "." && p != "..").ToList();
        return string.Join('/', safe);
    }

    private static string Bound(string? value, int maxChars)
    {
        maxChars = Math.Clamp(maxChars, 0, 500_000);
        var s = (value ?? string.Empty).Replace("\r", "").Trim();
        if (maxChars == 0) return string.Empty;
        return s.Length <= maxChars ? s : s[..maxChars];
    }
}


