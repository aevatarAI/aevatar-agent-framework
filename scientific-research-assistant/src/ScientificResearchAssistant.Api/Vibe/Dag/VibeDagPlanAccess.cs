using System.Text.Json;
using Aevatar.Agents.AGUI;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Contracts.Collab;
using ScientificResearchAssistant.Vibe.Tools;

namespace ScientificResearchAssistant.Api.Vibe.Dag;

// ============================================================
//  VibeDagPlanAccess (API-side implementation)
//
//  Purpose:
//  - Provide PLAN write access to agents via tool-calling.
//  - Respect session DAG binding (session.DagId) automatically.
// ============================================================

internal sealed class VibeDagPlanAccess : IVibeDagPlanAccess
{
    private readonly ResearchSessionManager _sessions;
    private readonly DagStore _dag;

    public VibeDagPlanAccess(ResearchSessionManager sessions, DagStore dag)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
    }

    public async Task<Struct> SetMilestonesAsync(
        string sessionId,
        IReadOnlyList<DagPlanMilestone> milestones,
        string? note,
        bool deleteOthers,
        CancellationToken ct)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            return ToStruct(new { ok = false, error = "missing sessionId" });

        ResearchSession session;
        try
        {
            // Sessions are ephemeral (in-memory). If the session isn't currently registered
            // (e.g., after restart), we re-hydrate a minimal session shell so plan edits
            // can still apply to File-SSoT workspace + DAG.
            session = _sessions.GetOrCreate(sessionId);
        }
        catch (Exception ex)
        {
            return ToStruct(new { ok = false, error = "invalid sessionId", detail = ex.Message, sessionId });
        }

        var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();

        milestones ??= Array.Empty<DagPlanMilestone>();
        var items = milestones
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.ExpectedOutput))
            .Select(m => new DagPlanMilestone(Math.Clamp(m.RoundIndex, 0, 200), (m.ExpectedOutput ?? "").Replace("\r", "").Trim()))
            .Where(m => m.ExpectedOutput.Length > 0)
            .Take(12)
            .ToList();

        if (items.Count == 0)
            return ToStruct(new { ok = false, error = "milestones must be non-empty", sessionId, dagId });

        var snap = await _dag.LoadSnapshotAsync(dagId, ct);

        static bool IsDeleted(SraDagNode n)
        {
            if (n.Tags == null) return false;
            if (!n.Tags.TryGetValue("deleted", out var v)) return false;
            return string.Equals((v ?? string.Empty).Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        bool IsSessionMilestone(SraDagNode n)
        {
            if (n == null) return false;
            if (n.Kind != SraDagNodeKind.Plan) return false;
            if (IsDeleted(n)) return false;
            if (n.Tags == null) return false;
            if (!n.Tags.TryGetValue("planKind", out var pk)) return false;
            if (!string.Equals((pk ?? string.Empty).Trim(), "milestone", StringComparison.OrdinalIgnoreCase)) return false;
            if (!n.Tags.TryGetValue("originSessionId", out var os)) return false;
            return string.Equals((os ?? string.Empty).Trim(), session.Id, StringComparison.Ordinal);
        }

        var existing = snap.Nodes
            .Where(n => n != null && IsSessionMilestone(n))
            .ToList();

        var now = Timestamp.FromDateTime(DateTime.UtcNow);
        var mutationId = $"plan_set_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        var m = new SraDagMutation
        {
            SessionId = session.Id,
            MutationId = mutationId,
            AuthorAgent = "research_assistant",
            CreatedAt = now
        };
        m.Labels["kind"] = "plan";
        m.Labels["planKind"] = "milestone";
        if (!string.IsNullOrWhiteSpace(note))
            m.Labels["note"] = note!.Replace("\r", "").Trim();

        var desiredIds = new HashSet<string>(StringComparer.Ordinal);
        var upserted = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var suffix = it.RoundIndex > 0 ? $"r{it.RoundIndex}" : $"i{i + 1}";
            var nodeId = SanitizeId($"plan_{session.Id}_ms_{suffix}");
            if (nodeId.Length == 0) nodeId = $"plan_{Guid.NewGuid():N}";
            desiredIds.Add(nodeId);

            var label = it.RoundIndex > 0
                ? $"Milestone (Round {it.RoundIndex}): {Bound(it.ExpectedOutput, 160)}"
                : $"Milestone: {Bound(it.ExpectedOutput, 180)}";

            var proof = string.IsNullOrWhiteSpace(note)
                ? $"ExpectedOutput:\n{Bound(it.ExpectedOutput, 900)}"
                : $"Note:\n{Bound(note!, 900)}\n\nExpectedOutput:\n{Bound(it.ExpectedOutput, 900)}";

            var node = new SraDagNode
            {
                Id = nodeId,
                Type = SraDagNodeType.Assumption,
                Label = Bound(label, 200),
                Proof = Bound(proof, 1200),
                UpdatedAt = now,
                Kind = SraDagNodeKind.Plan
            };
            node.Tags["originSessionId"] = session.Id;
            node.Tags["author"] = "user_chat";
            node.Tags["planKind"] = "milestone";
            node.Tags["milestoneRoundIndex"] = it.RoundIndex.ToString();
            node.Tags["deleted"] = "false";
            m.UpsertNodes.Add(node);
            upserted++;
        }

        var deleted = 0;
        if (deleteOthers)
        {
            foreach (var old in existing)
            {
                var id = (old?.Id ?? string.Empty).Trim();
                if (id.Length == 0) continue;
                if (desiredIds.Contains(id)) continue;
                old!.Tags["deleted"] = "true";
                old.UpdatedAt = now;
                m.UpsertNodes.Add(old);
                deleted++;
            }
        }

        _ = await _dag.ApplyMutationAsync(dagId, m, ct);

        // Notify UI to refresh dag snapshot (same pattern as other dag writes).
        session.Events.Publish(new CustomEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = "aevatar.vibe.dag_updated",
            Value = new { sessionId = session.Id, dagId, mutationId }
        });

        return ToStruct(new
        {
            ok = true,
            sessionId = session.Id,
            dagId,
            mutationId,
            upserted,
            deleted,
            note = note ?? ""
        });
    }

    private static string Bound(string s, int max)
    {
        s = (s ?? string.Empty).Replace("\r", "").Trim();
        if (s.Length <= max) return s;
        return s[..max];
    }

    private static string SanitizeId(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        var sb = new System.Text.StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        var outId = sb.ToString().Trim('_');
        return outId.Length <= 64 ? outId : outId[..64];
    }

    private static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonParser.Default.Parse<Struct>(json);
    }
}