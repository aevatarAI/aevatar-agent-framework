using System.Text;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.Cognitive.Researching.Workspace;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Brief;

// ============================================================
//  BriefStore (File-SSoT)
//
//  - Canonical brief lives under:
//      workspace/sessions/{sessionId}/deliverables/brief.json
//  - Stored as Protobuf-JSON (review friendly).
//  - Bounded + deterministic: store enforces basic size bounds;
//    caller manages version policy.
// ============================================================
public sealed class BriefStore
{
    private const string FileName = "brief.json";

    // Keep brief "one-page-ish" (best-effort).
    private const int MaxRewrittenQuestionChars = 1200;
    private const int MaxScopeChars = 2000;
    private const int MaxSuccessCriteriaChars = 1200;
    private const int MaxBulletChars = 800;
    private const int MaxBullets = 80;
    private const int MaxTerms = 40;
    private const int MaxMilestones = 20;

    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter Formatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: Registry));
    private static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true).WithTypeRegistry(Registry));

    private readonly WorkspaceService _workspace;
    private readonly ILogger<BriefStore> _logger;

    public BriefStore(WorkspaceService workspace, ILogger<BriefStore> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetBriefPath(string sessionId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return Path.Combine(ws.DeliverablesDir, FileName);
    }

    public async Task<SraResearchBriefSnapshot> LoadAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = Path.Combine(ws.DeliverablesDir, FileName);

        try
        {
            if (!File.Exists(path))
            {
                return new SraResearchBriefSnapshot
                {
                    SessionId = ws.SessionId,
                    Version = 0,
                    UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
            }

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SraResearchBriefSnapshot
                {
                    SessionId = ws.SessionId,
                    Version = 0,
                    UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
            }

            var snap = Parser.Parse<SraResearchBriefSnapshot>(json);
            if (string.IsNullOrWhiteSpace(snap.SessionId))
                snap.SessionId = ws.SessionId;
            return snap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load brief.json (best-effort). Returning empty brief.");
            return new SraResearchBriefSnapshot
            {
                SessionId = ws.SessionId,
                Version = 0,
                UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    public async Task<SraResearchBriefSnapshot> SaveAsync(string sessionId, SraResearchBriefSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        // Normalize + bound
        snapshot.SessionId = ws.SessionId;
        snapshot.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        snapshot.RewrittenQuestion = Bound(snapshot.RewrittenQuestion, MaxRewrittenQuestionChars);
        snapshot.Scope = Bound(snapshot.Scope, MaxScopeChars);
        snapshot.SuccessCriteria = Bound(snapshot.SuccessCriteria, MaxSuccessCriteriaChars);

        if (snapshot.Terms.Count > MaxTerms)
        {
            while (snapshot.Terms.Count > MaxTerms)
                snapshot.Terms.RemoveAt(snapshot.Terms.Count - 1);
        }

        foreach (var t in snapshot.Terms)
        {
            if (t == null) continue;
            t.Term = Bound(t.Term, 80);
            t.Meaning = Bound(t.Meaning, 240);
        }

        BoundList(snapshot.Assumptions, MaxBullets, MaxBulletChars);
        BoundList(snapshot.Risks, MaxBullets, MaxBulletChars);
        BoundList(snapshot.Uncertainties, MaxBullets, MaxBulletChars);

        if (snapshot.Milestones.Count > MaxMilestones)
        {
            while (snapshot.Milestones.Count > MaxMilestones)
                snapshot.Milestones.RemoveAt(snapshot.Milestones.Count - 1);
        }

        foreach (var m in snapshot.Milestones)
        {
            if (m == null) continue;
            m.ExpectedOutput = Bound(m.ExpectedOutput, 280);
            if (m.RoundIndex < 0) m.RoundIndex = 0;
        }

        // Atomic write: session tmp -> overwrite target
        Directory.CreateDirectory(ws.TmpDir);
        Directory.CreateDirectory(ws.DeliverablesDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");

        var json = Formatter.Format(snapshot);
        await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct);

        var target = Path.Combine(ws.DeliverablesDir, FileName);
        File.Move(tmp, target, overwrite: true);

        return snapshot;
    }

    private static void BoundList(RepeatedField<string> list, int maxItems, int maxChars)
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

    /// <summary>
    /// Updates only the milestones in the brief, preserving all other fields.
    /// Used for dynamic milestone modification (direction change).
    /// </summary>
    public async Task<SraResearchBriefSnapshot> UpdateMilestonesAsync(
        string sessionId,
        List<SraResearchMilestone> newMilestones,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Load existing brief
        var brief = await LoadAsync(sessionId, ct);

        // Update milestones (bounded)
        brief.Milestones.Clear();
        if (newMilestones?.Count > 0)
        {
            var bounded = newMilestones.Take(MaxMilestones).ToList();
            brief.Milestones.AddRange(bounded);
        }

        return await SaveAsync(sessionId, brief, ct);
    }
}
