using VibeResearching.Api.Materials;
using VibeResearching.Api.Workspace;

namespace VibeResearching.Api.Sessions;

// ============================================================
//  ResearchWorkspaceState (server-side, JSON for AG-UI STATE_*)
//
//  Notes:
//  - This is NOT agent state (no cross-runtime requirement here).
//  - Keep it small and deterministic; avoid dumping full source texts.
// ============================================================

public sealed class ResearchWorkspaceState
{
    public string Kind { get; init; } = "sra.workspace";
    public required string SessionId { get; init; }

    // File-SSoT snapshot (DAG facts + facts_proposed counts).
    public KnowledgeState Knowledge { get; set; } = new();

    public MaterialsState Materials { get; set; } = new();
    public VibeState Vibe { get; set; } = new();
}

public sealed class KnowledgeState
{
    // DAG knowledge nodes count (facts are DAG nodes now).
    public int FactsCount { get; set; }
    public int FactsProposedCount { get; set; }

    // Relative paths under workspace/sessions/{sessionId}/ (bounded).
    public List<string> FactsProposedRecent { get; set; } = new();
}

public sealed class MaterialsState
{
    public string RootDir { get; set; } = string.Empty;

    public string LoadedAt { get; set; } = string.Empty; // ISO 8601

    public List<MaterialMeta> Items { get; set; } = new();

    public string ContextPreview { get; set; } = string.Empty;
}

public sealed class VibeState
{
    public string LastRunId { get; set; } = string.Empty;
    public string LastGoal { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new();
}

public sealed class MaterialMeta
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
    public required string Kind { get; init; }
}

// ============================================================
//  WorkspaceProjection
//
//  说明：
//  - 统一 materials + knowledge 的投影逻辑，避免重复与漂移
// ============================================================
internal static class WorkspaceProjection
{
    public static void ApplyMaterials(ResearchWorkspaceState workspace, MaterialsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(snapshot);

        var materials = workspace.Materials;
        materials.RootDir = $"dag:{snapshot.DagId}";
        materials.LoadedAt = snapshot.LoadedAt.ToString("O");
        materials.Items = new List<MaterialMeta>(capacity: snapshot.Facts.Count);

        foreach (var x in snapshot.Facts)
        {
            materials.Items.Add(new MaterialMeta
            {
                Id = x.Id,
                Title = x.Title,
                RelativePath = x.RelativePath,
                Kind = x.Kind
            });
        }

        materials.ContextPreview = Trunc(snapshot.RenderedContext, 2000);
    }

    public static void ApplyKnowledge(ResearchWorkspaceState workspace, WorkspaceScanResult scan, MaterialsSnapshot? materials)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scan);

        var k = workspace.Knowledge;
        k.FactsCount = materials?.Facts?.Count ?? 0;
        k.FactsProposedCount = scan.FactsProposedCount;
        k.FactsProposedRecent = scan.FactsProposedRecent;
    }

    private static string Trunc(string? s, int maxChars)
    {
        var t = (s ?? string.Empty).Replace("\r", "").Trim();
        if (t.Length <= maxChars) return t;
        return t[..maxChars];
    }
}


