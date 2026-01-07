namespace ScientificResearchAssistant.Api.Sessions;

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

    // File-SSoT snapshot (facts / facts_proposed / sources counts).
    public KnowledgeState Knowledge { get; set; } = new();

    public MaterialsState Materials { get; set; } = new();
    public VibeState Vibe { get; set; } = new();
}

public sealed class KnowledgeState
{
    public int FactsCount { get; set; }
    public int FactsProposedCount { get; set; }
    public int SourcesCount { get; set; }

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


