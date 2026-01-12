using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using ScientificResearchAssistant.Api.Sessions;
using ScientificResearchAssistant.Contracts.Collab;
using ScientificResearchAssistant.Vibe.Tools;

namespace ScientificResearchAssistant.Api.Vibe.Dag;

// ============================================================
//  VibeDagAccess (API-side implementation)
//
//  Purpose:
//  - Provide read-only DAG access to agents via tool-calling.
//  - Respect session DAG binding (session.DagId) automatically.
//
//  Safety:
//  - Read-only: only loads snapshot and computes explain.
// ============================================================

internal sealed class VibeDagAccess : IVibeDagAccess
{
    private const int MaxTagsPerNode = 12;
    private const int MaxTagValueChars = 200;

    private readonly ResearchSessionManager _sessions;
    private readonly DagStore _dag;

    public VibeDagAccess(ResearchSessionManager sessions, DagStore dag)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
    }

    public async Task<Struct> GetSnapshotAsync(string sessionId, int maxNodes, int maxEdges, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (dagId, ok) = ResolveDagId(sessionId);

        var snap = await _dag.LoadSnapshotAsync(dagId, ct);

        var nodes = snap.Nodes
            .Where(n => n != null)
            .Take(Math.Clamp(maxNodes, 1, 2000))
            .Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString(),
                label = n.Label,
                proof = n.Proof,
                tags = TakeSmallTags(n.Tags),
                updatedAt = n.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
            })
            .ToList();

        var edges = snap.Edges
            .Where(e => e != null)
            .Take(Math.Clamp(maxEdges, 1, 8000))
            .Select(e => new
            {
                fromId = e.FromId,
                toId = e.ToId,
                type = e.Type,
                updatedAt = e.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
            })
            .ToList();

        var result = new
        {
            ok,
            sessionId,
            dagId,
            updatedAt = snap.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? "",
            nodes,
            edges,
            truncated = new
            {
                nodes = snap.Nodes.Count > nodes.Count,
                edges = snap.Edges.Count > edges.Count
            }
        };

        return ToStruct(result);
    }

    public async Task<Struct> ExplainAsync(string sessionId, string nodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (dagId, ok) = ResolveDagId(sessionId);
        var snap = await _dag.LoadSnapshotAsync(dagId, ct);

        var explain = DagExplain.Explain(snap, nodeId);
        // Include binding info for better debugging (as metadata on top of explain).
        var result = new
        {
            ok,
            sessionId,
            dagId,
            explain = new
            {
                sessionId = explain.SessionId,
                nodeId = explain.NodeId,
                hasCycle = explain.HasCycle,
                provable = explain.Provable,
                directDeps = explain.DirectDeps,
                topo = explain.Topo,
                node = explain.Node == null ? null : new
                {
                    id = explain.Node.Id,
                    type = explain.Node.Type.ToString(),
                    label = explain.Node.Label,
                    proof = explain.Node.Proof,
                    tags = TakeSmallTags(explain.Node.Tags),
                    updatedAt = explain.Node.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                },
                missing = explain.Missing
                    .Select(m => new
                    {
                        id = m.Id,
                        type = m.Type.ToString(),
                        label = m.Label,
                        updatedAt = m.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                    })
                    .ToList()
            }
        };

        return ToStruct(result);
    }

    private (string DagId, bool Ok) ResolveDagId(string sessionId)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            return ("", false);

        if (!_sessions.TryGet(sessionId, out var session))
        {
            // Best-effort: fallback to "dagId == sessionId".
            return (sessionId, false);
        }

        var dagId = string.IsNullOrWhiteSpace(session.DagId) ? session.Id : session.DagId.Trim();
        return (dagId, true);
    }

    private static Dictionary<string, string> TakeSmallTags(Google.Protobuf.Collections.MapField<string, string> tags)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tags == null || tags.Count == 0) return dict;

        foreach (var kv in tags
                     .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                     .Take(MaxTagsPerNode))
        {
            dict[kv.Key.Trim()] = Bound((kv.Value ?? string.Empty).Trim(), MaxTagValueChars);
        }

        return dict;
    }

    private static string Bound(string s, int max)
    {
        s = (s ?? string.Empty).Trim();
        if (s.Length <= max) return s;
        return s[..max];
    }

    private static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonParser.Default.Parse<Struct>(json);
    }
}


