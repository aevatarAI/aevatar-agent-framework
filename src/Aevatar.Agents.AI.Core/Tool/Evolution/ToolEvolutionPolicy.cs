namespace Aevatar.Agents.AI.Tool.Evolution;

// ============================================================
//  Tool Evolution Policy
//
//  中文 + ASCII:
//  - Replace / Canary 两种策略，保持策略显式化。
// ============================================================
/// <summary>
/// Tool evolution strategy.
/// </summary>
public enum ToolEvolutionStrategy
{
    Replace = 0,
    Canary = 1
}

/// <summary>
/// Policy for registering evolved tools.
/// </summary>
public sealed class ToolEvolutionPolicy
{
    /// <summary>
    /// Evolution strategy (replace or canary).
    /// </summary>
    public ToolEvolutionStrategy Strategy { get; set; } = ToolEvolutionStrategy.Replace;

    /// <summary>
    /// Canary percentage (0-1).
    /// </summary>
    public double CanaryPercentage { get; set; } = 0.1;

    /// <summary>
    /// Failure rate threshold to rollback canary.
    /// </summary>
    public double RollbackFailureRate { get; set; } = 0.5;

    /// <summary>
    /// Minimum calls before evaluating rollback.
    /// </summary>
    public int MinCallsBeforeRollback { get; set; } = 20;
}
