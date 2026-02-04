namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Constants for delivery center and research outputs.
/// </summary>
public static class DeliveryConsts
{
    /// <summary>
    /// Maximum number of conclusion cards.
    /// </summary>
    public const int MaxConclusions = 32;

    /// <summary>
    /// Maximum number of evidence items.
    /// </summary>
    public const int MaxEvidence = 80;

    /// <summary>
    /// Maximum number of next task items.
    /// </summary>
    public const int MaxTasks = 64;

    /// <summary>
    /// Maximum number of conclusions to display in UI.
    /// </summary>
    public const int MaxConclusionsForDisplay = 12;

    /// <summary>
    /// Maximum number of evidence items to display in UI.
    /// </summary>
    public const int MaxEvidenceForDisplay = 20;

    /// <summary>
    /// Maximum number of tasks to display in UI.
    /// </summary>
    public const int MaxTasksForDisplay = 20;

    /// <summary>
    /// Maximum number of evidence paths per conclusion.
    /// </summary>
    public const int MaxEvidencePathsPerConclusion = 30;

    /// <summary>
    /// Maximum number of related DAG nodes per conclusion.
    /// </summary>
    public const int MaxRelatedNodesPerConclusion = 30;

    /// <summary>
    /// Maximum characters for conclusion claim.
    /// </summary>
    public const int MaxConclusionClaimLength = 220;

    /// <summary>
    /// Maximum characters for conclusion notes.
    /// </summary>
    public const int MaxConclusionNotesLength = 600;

    /// <summary>
    /// Maximum characters for evidence title.
    /// </summary>
    public const int MaxEvidenceTitleLength = 160;

    /// <summary>
    /// Maximum characters for evidence excerpt.
    /// </summary>
    public const int MaxEvidenceExcerptLength = 280;

    /// <summary>
    /// Maximum characters for task title.
    /// </summary>
    public const int MaxTaskTitleLength = 160;

    /// <summary>
    /// Maximum characters for task detail.
    /// </summary>
    public const int MaxTaskDetailLength = 280;

    /// <summary>
    /// Maximum characters for delivery changed summary.
    /// </summary>
    public const int MaxChangedSummaryLength = 4000;

    /// <summary>
    /// File name for conclusions snapshot.
    /// </summary>
    public const string ConclusionsFile = "conclusions.json";

    /// <summary>
    /// File name for evidence table snapshot.
    /// </summary>
    public const string EvidenceFile = "evidence.json";

    /// <summary>
    /// File name for tasks snapshot.
    /// </summary>
    public const string TasksFile = "tasks.json";

    /// <summary>
    /// File name for delivery center snapshot.
    /// </summary>
    public const string DeliverySnapshotFile = "delivery_snapshot.json";

    /// <summary>
    /// File name for research brief.
    /// </summary>
    public const string BriefFile = "brief.json";

    /// <summary>
    /// Maximum characters for rewritten question in brief.
    /// </summary>
    public const int MaxRewrittenQuestionLength = 1200;

    /// <summary>
    /// Maximum characters for scope in brief.
    /// </summary>
    public const int MaxScopeLength = 2000;

    /// <summary>
    /// Maximum characters for success criteria in brief.
    /// </summary>
    public const int MaxSuccessCriteriaLength = 1200;

    /// <summary>
    /// Maximum number of terms in brief.
    /// </summary>
    public const int MaxTerms = 40;

    /// <summary>
    /// Maximum number of milestones in brief.
    /// </summary>
    public const int MaxMilestones = 20;

    /// <summary>
    /// Maximum number of bullets (assumptions, risks, uncertainties).
    /// </summary>
    public const int MaxBullets = 80;

    /// <summary>
    /// Maximum characters per bullet.
    /// </summary>
    public const int MaxBulletLength = 800;

    /// <summary>
    /// File name for goals snapshot.
    /// </summary>
    public const string GoalsFile = "goals.json";

    /// <summary>
    /// Maximum number of goals.
    /// </summary>
    public const int MaxGoals = 200;

    /// <summary>
    /// Maximum characters for goal text.
    /// </summary>
    public const int MaxGoalTextLength = 2000;
}
