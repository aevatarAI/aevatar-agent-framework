namespace Aevatar.Agents.Sessions;

public sealed class CognitiveSessionOptions
{
    /// <summary>
    /// Workflow directory (defaults to ~/.aevatar/workflows).
    /// </summary>
    public string? WorkflowsDirectory { get; set; }

    /// <summary>
    /// Whether to lazily create role agents.
    /// </summary>
    public bool LazyLoadRoles { get; set; } = true;
}
