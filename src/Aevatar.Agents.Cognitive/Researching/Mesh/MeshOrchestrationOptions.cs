namespace Aevatar.Agents.Cognitive.Researching.Mesh;

// ============================================================
//  Mesh Orchestration Options (Configurable)
//
//  Purpose:
//  - Feature-flag mesh-driven worker collaboration (Option B).
//  - Control mesh-driven worker execution.
//
//  Notes:
//  - Default is enabled (requested by user). This changes the out-of-box behavior.
// ============================================================

public sealed class MeshOrchestrationOptions
{
    public const string SectionName = "Vibe:MeshOrchestration";

    /// <summary>
    /// Enable mesh-driven orchestration for the worker phase.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

}


