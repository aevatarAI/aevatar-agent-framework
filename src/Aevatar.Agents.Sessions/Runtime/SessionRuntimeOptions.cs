namespace Aevatar.Agents.Sessions.Runtime;

public sealed class SessionRuntimeOptions
{
    /// <summary>
    /// Default system prompt for session agents.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Default temperature for session agents.
    /// </summary>
    public double Temperature { get; set; } = 0.6;

    /// <summary>
    /// Default max output tokens for session agents.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 1200;

    /// <summary>
    /// Default stream chunk batch size.
    /// </summary>
    public int StreamChunkEveryN { get; set; } = 1;

    /// <summary>
    /// Max snapshot messages retained for AG-UI snapshots.
    /// </summary>
    public int MaxSnapshotMessages { get; set; } = 60;

    /// <summary>
    /// Preferred agent role (for primary role selection).
    /// </summary>
    public string? AgentRole { get; set; } = "sisyphus";

    /// <summary>
    /// Default workflow name.
    /// </summary>
    public string? WorkflowName { get; set; }

    /// <summary>
    /// Cognitive workflow worker pool size.
    /// </summary>
    public int WorkflowWorkerCount { get; set; } = 5;

    /// <summary>
    /// Enable agent YAML configuration.
    /// </summary>
    public bool EnableAgentYaml { get; set; } = true;

    /// <summary>
    /// Enable workflow YAML bootstrap.
    /// </summary>
    public bool EnableWorkflowYaml { get; set; } = true;

    /// <summary>
    /// Session idle timeout in minutes.
    /// </summary>
    public int SessionIdleTimeoutMinutes { get; set; } = 20;

    /// <summary>
    /// Session cleanup interval in seconds.
    /// </summary>
    public int SessionCleanupIntervalSeconds { get; set; } = 120;
}
