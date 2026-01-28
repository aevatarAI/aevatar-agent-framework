using System.Text;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Workspace;
using Aevatar.VibeResearching.Infrastructure;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.Delivery;

// ============================================================
//  GoalsRepository (File-SSoT)
//
//  - Canonical goals live under:
//      workspace/sessions/{sessionId}/decisions/goals.json
//  - Stored as Protobuf-JSON (review friendly).
//  - Bounded + deterministic: caller manages ordering/priority;
//    store guarantees atomicity + within-root safety via WorkspaceService.
// ============================================================

public sealed class GoalsRepository : IGoalsRepository
{
    private const string FileName = "goals.json";
    private const int MaxGoals = 200;
    private const int MaxGoalTextChars = 2000;

    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(SraCollabReflection.Descriptor);
    private static readonly JsonFormatter Formatter =
        new(new JsonFormatter.Settings(formatDefaultValues: true, typeRegistry: Registry));
    private static readonly JsonParser Parser =
        new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true).WithTypeRegistry(Registry));

    private readonly WorkspaceService _workspace;
    private readonly ILogger<GoalsRepository> _logger;

    public GoalsRepository(WorkspaceService workspace, ILogger<GoalsRepository> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string GetGoalsPath(string sessionId)
    {
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        return Path.Combine(ws.DecisionsDir, FileName);
    }

    public async Task<SraGoalsSnapshot> LoadAsync(string sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ws = _workspace.EnsureSessionWorkspace(sessionId);
        var path = Path.Combine(ws.DecisionsDir, FileName);

        try
        {
            if (!File.Exists(path))
            {
                return new SraGoalsSnapshot
                {
                    SessionId = ws.SessionId,
                    Version = 0,
                    UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
            }

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SraGoalsSnapshot
                {
                    SessionId = ws.SessionId,
                    Version = 0,
                    UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };
            }

            var snap = Parser.Parse<SraGoalsSnapshot>(json);
            if (string.IsNullOrWhiteSpace(snap.SessionId))
                snap.SessionId = ws.SessionId;
            return snap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load goals.json (best-effort). Returning empty snapshot.");
            return new SraGoalsSnapshot
            {
                SessionId = ws.SessionId,
                Version = 0,
                UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }
    }

    public async Task<SraGoalsSnapshot> SaveAsync(
        string sessionId,
        SraGoalsSnapshot snapshot,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ct.ThrowIfCancellationRequested();

        var ws = _workspace.EnsureSessionWorkspace(sessionId);

        // Normalize + bound (safety against huge payloads)
        snapshot.SessionId = ws.SessionId;
        snapshot.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        if (snapshot.Goals.Count > MaxGoals)
        {
            // Keep first N deterministically (caller should already order by priority).
            while (snapshot.Goals.Count > MaxGoals)
            {
                snapshot.Goals.RemoveAt(snapshot.Goals.Count - 1);
            }
        }

        foreach (var g in snapshot.Goals)
        {
            if (g is null) continue;

            g.GoalId = (g.GoalId ?? string.Empty).Trim();
            g.Text = (g.Text ?? string.Empty).Replace("\r", "").Trim();
            if (g.Text.Length > MaxGoalTextChars)
                g.Text = g.Text[..MaxGoalTextChars];
            if (g.UpdatedAt == null)
                g.UpdatedAt = snapshot.UpdatedAt;
        }

        // Atomic write: session tmp -> overwrite target
        Directory.CreateDirectory(ws.TmpDir);
        var tmp = Path.Combine(ws.TmpDir, $"{Guid.NewGuid():N}.tmp");

        var json = Formatter.Format(snapshot);
        await File.WriteAllTextAsync(tmp, json, Encoding.UTF8, ct);

        Directory.CreateDirectory(ws.DecisionsDir);
        var target = Path.Combine(ws.DecisionsDir, FileName);
        File.Move(tmp, target, overwrite: true);

        return snapshot;
    }
}
