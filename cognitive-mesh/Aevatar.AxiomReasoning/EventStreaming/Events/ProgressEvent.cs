namespace Aevatar.AxiomReasoning.EventStreaming.Events;

public record ProgressEvent : AxiomEvent
{
    public string Phase { get; init; } = "";
    public string? Message { get; init; }
    public int ProgressPercent { get; init; }

    // Logical worker id (e.g. "coordinator", "worker-0"...), for UI grouping
    public string? WorkerId { get; init; }

    // DSL step meta (for debugging / UI)
    public int? Depth { get; init; }
    public string? StepId { get; init; }
    public string? StepType { get; init; }
    public string? StepStatus { get; init; }

    // Readable content (best-effort)
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
    public string? AssistantResponsePreview { get; init; }
    public string? AssistantResponse { get; init; }

    // Failure details (when stepStatus == Failed)
    public string? Error { get; init; }

    // Streaming meta (PaperReview-like)
    public string? ProviderName { get; init; }
    public int? TokenIndex { get; init; }
    public string? TokenDelta { get; init; }

    // vote
    public int VoteRound { get; init; }
    public int VoteMaxRounds { get; init; }
    public int VoteK { get; init; }
    public int VoteCurrentVotes { get; init; }

    // parallel
    public int ParallelTotal { get; init; }
    public int ParallelCompleted { get; init; }
    public int ParallelFailed { get; init; }

    // totals
    public int TotalLlmCalls { get; init; }
    public long TotalTokens { get; init; }
}