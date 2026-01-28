namespace Aevatar.VibeResearching.Agents;

/// <summary>
/// Constants for vibe orchestration and research workflows.
/// </summary>
public static class OrchestrationConsts
{
    /// <summary>
    /// Maximum number of rounds to display in trace/summary.
    /// </summary>
    public const int MaxRoundsForDisplay = 100;

    /// <summary>
    /// Maximum number of agent summaries per round.
    /// </summary>
    public const int MaxAgentSummariesPerRound = 20;

    /// <summary>
    /// Maximum number of highlights per agent.
    /// </summary>
    public const int MaxHighlightsPerAgent = 10;

    /// <summary>
    /// Maximum number of next actions per agent.
    /// </summary>
    public const int MaxNextActionsPerAgent = 10;

    /// <summary>
    /// Maximum number of referenced paths per agent.
    /// </summary>
    public const int MaxReferencedPathsPerAgent = 30;

    /// <summary>
    /// Maximum number of open questions per round.
    /// </summary>
    public const int MaxOpenQuestionsPerRound = 20;

    /// <summary>
    /// Maximum number of missing evidence items per round.
    /// </summary>
    public const int MaxMissingEvidencePerRound = 30;

    /// <summary>
    /// Maximum number of DAG changes per round.
    /// </summary>
    public const int MaxDagChangesPerRound = 100;
}
