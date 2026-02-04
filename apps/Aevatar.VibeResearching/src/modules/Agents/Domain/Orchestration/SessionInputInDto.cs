namespace Aevatar.VibeResearching.Agents.Orchestration;

/// <summary>
/// Input DTO for session execution containing user input and execution parameters.
/// </summary>
public class SessionInputInDto
{
    /// <summary>
    /// Optional request ID for tracking.
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// User message/question for this session input.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Execution mode: chat, vibe, milestone, vibe_loop, vibe_researching, etc.
    /// </summary>
    public string? Mode { get; set; }

    /// <summary>
    /// Optional provider name override for LLM selection.
    /// </summary>
    public string? ProviderName { get; set; }

    /// <summary>
    /// Paths to attachments (files, images, etc.) provided by user.
    /// </summary>
    public List<string> AttachmentPaths { get; set; } = new();

    /// <summary>
    /// Additional parameters to pass to agents.
    /// </summary>
    public Dictionary<string, object> ToAgents { get; set; } = new();

    /// <summary>
    /// Loop execution configuration (goal loop).
    /// </summary>
    public LoopConfig? Loop { get; set; }

    /// <summary>
    /// Loop configuration for goal-based execution.
    /// </summary>
    public class LoopConfig
    {
        /// <summary>
        /// Maximum number of iterations to run.
        /// </summary>
        public int MaxIterations { get; set; }

        /// <summary>
        /// Maximum total duration in milliseconds.
        /// </summary>
        public int MaxTotalDurationMs { get; set; }
    }
}
