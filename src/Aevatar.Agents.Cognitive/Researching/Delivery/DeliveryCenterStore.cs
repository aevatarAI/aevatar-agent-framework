using System.Text;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Cognitive.Researching.Workspace;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Delivery;

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
    //  UI snapshot (JSON-friendly, bounded)
    // ------------------------------------------------------------
    public async Task<object> GetSnapshotForUiAsync(string sessionId, CancellationToken ct)
    {
        var (delivery, conclusions, evidence, tasks) = await LoadAllAsync(sessionId, ct);

        static string Iso(Timestamp? ts)
        {
            try
            {
                return ts == null ? "" : ts.ToDateTime().ToUniversalTime().ToString("O");
            }
            catch
            {
                return "";
            }
        }

        return new
        {
            version = delivery.Version,
            updatedAt = Iso(delivery.UpdatedAt),
            changedSummary = delivery.ChangedSummary ?? "",
            paths = new
            {
                paperOutline = delivery.PaperOutlinePath ?? "",
                paperDraft = delivery.PaperDraftPath ?? "",
                conclusions = delivery.ConclusionsPath ?? "",
                evidence = delivery.EvidencePath ?? "",
                tasks = delivery.TasksPath ?? ""
            },
            conclusions = new
            {
                version = conclusions.Version,
                updatedAt = Iso(conclusions.UpdatedAt),
                items = conclusions.Items.Select(c => new
                {
                    cardId = c.CardId ?? "",
                    claim = c.Claim ?? "",
                    confidence = c.Confidence.ToString(),
                    evidencePaths = c.EvidencePaths?.ToList() ?? new List<string>(),
                    counterEvidencePaths = c.CounterEvidencePaths?.ToList() ?? new List<string>(),
                    relatedDagNodeIds = c.RelatedDagNodeIds?.ToList() ?? new List<string>(),
                    notes = c.Notes ?? "",
                    updatedAt = Iso(c.UpdatedAt)
                }).ToList()
            },
            evidence = new
            {
                version = evidence.Version,
                updatedAt = Iso(evidence.UpdatedAt),
                items = evidence.Items.Select(e => new
                {
                    evidenceId = e.EvidenceId ?? "",
                    title = e.Title ?? "",
                    path = e.Path ?? "",
                    excerpt = e.Excerpt ?? "",
                    relevance = e.Relevance ?? "",
                    updatedAt = Iso(e.UpdatedAt)
                }).ToList()
            },
            tasks = new
            {
                version = tasks.Version,
                updatedAt = Iso(tasks.UpdatedAt),
                items = tasks.Items.Select(t => new
                {
                    taskId = t.TaskId ?? "",
                    title = t.Title ?? "",
                    detail = t.Detail ?? "",
                    priority = t.Priority,
                    blockedBy = t.BlockedBy?.ToList() ?? new List<string>(),
                    updatedAt = Iso(t.UpdatedAt)
                }).ToList()
            }
        };
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
            t.Title = Bound(t.Title, 200);
            t.Detail = Bound(t.Detail, 600);
            t.UpdatedAt ??= snapshot.UpdatedAt;
        }

        var path = Path.Combine(ws.DeliverablesDir, TasksFile);
        await WriteAtomicAsync(ws, path, snapshot, ct);
        return snapshot;
    }

    // ------------------------------------------------------------
    //  Internals
    // ------------------------------------------------------------

    private async Task<T> LoadOrEmptyAsync<T>(string path, string sessionId, Func<T> makeEmpty, CancellationToken ct)
        where T : class, IMessage<T>, new()
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(path))
                return makeEmpty();

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
                return makeEmpty();

            var msg = Parser.Parse<T>(json);
            if (msg is SraDeliveryCenterSnapshot s1 && string.IsNullOrWhiteSpace(s1.SessionId))
                s1.SessionId = sessionId;
            if (msg is SraConclusionCardsSnapshot s2 && string.IsNullOrWhiteSpace(s2.SessionId))
                s2.SessionId = sessionId;
            if (msg is SraEvidenceTableSnapshot s3 && string.IsNullOrWhiteSpace(s3.SessionId))
                s3.SessionId = sessionId;
            if (msg is SraNextTasksSnapshot s4 && string.IsNullOrWhiteSpace(s4.SessionId))
                s4.SessionId = sessionId;
            return msg;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load deliverables (best-effort). Returning empty.");
            return makeEmpty();
        }
    }

    private static async Task WriteAtomicAsync(WorkspacePaths ws, string path, IMessage msg, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(ws.TmpDir);
        Directory.CreateDirectory(ws.DeliverablesDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");

        var json = Formatter.Format(msg);
        await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct);

        File.Move(tmp, path, overwrite: true);
    }

    private static void BoundRepeated<T>(RepeatedField<T> list, int maxItems)
    {
        if (list == null) return;
        maxItems = Math.Clamp(maxItems, 0, 500);

        if (list.Count > maxItems)
        {
            while (list.Count > maxItems)
                list.RemoveAt(list.Count - 1);
        }
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
        {
            var s = list[i] ?? string.Empty;
            s = s.Replace("\r", "").Trim();
            if (s.Length > maxChars) s = s[..maxChars];
            list[i] = s;
        }
    }

    private static string Bound(string? value, int maxChars)
    {
        maxChars = Math.Clamp(maxChars, 0, 500_000);
        var s = (value ?? string.Empty).Replace("\r", "").Trim();
        if (maxChars == 0) return string.Empty;
        return s.Length <= maxChars ? s : s[..maxChars];
    }

    private static string NormalizeRel(string? path)
        => (path ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/').TrimEnd('/');
}
