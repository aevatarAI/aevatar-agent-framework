namespace VibeResearching.Api.Vibe.Mesh;

// ============================================================
//  Mesh Orchestration Options (Configurable)
//
//  Purpose:
//  - Feature-flag mesh-driven worker collaboration (Option B).
//  - Control fallback behavior on mesh compile/validation failures.
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

    /// <summary>
    /// Behavior when mesh compilation/validation fails.
    /// Allowed: "fallback" | "fail"
    /// Default: "fallback"
    /// </summary>
    public string OnCompileError { get; set; } = "fallback";
}


