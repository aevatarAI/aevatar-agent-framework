namespace SisyphusMaker.Dtos;

/// <summary>
/// Final verification result emitted as the SSE "result" event.
/// </summary>
public sealed record VerificationResponse
{
    /// <summary>Whether the knowledge node passed verification.</summary>
    public bool IsPassed { get; init; }

    /// <summary>Consensus reason from the winning proposal.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Confidence score from the winning proposal (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Detailed failure reason, null if passed.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Full voting breakdown.</summary>
    public required VoteDetail VoteDetail { get; init; }

    /// <summary>Total duration in milliseconds.</summary>
    public long DurationMs { get; set; }
}

/// <summary>
/// Detailed breakdown of the consensus voting process.
/// </summary>
public sealed record VoteDetail
{
    /// <summary>Number of parallel workers used.</summary>
    public int WorkerCount { get; init; }

    /// <summary>Total voting rounds executed.</summary>
    public int TotalRounds { get; init; }

    /// <summary>Whether consensus was achieved or best-effort majority used.</summary>
    public bool ConsensusReached { get; init; }

    /// <summary>Individual worker votes from the final round.</summary>
    public required List<WorkerVote> WorkerVotes { get; init; }

    /// <summary>Per-round vote distribution summaries.</summary>
    public required List<VoteRoundSummary> Rounds { get; init; }
}

/// <summary>
/// An individual worker's vote in the consensus process.
/// </summary>
public sealed record WorkerVote
{
    /// <summary>Worker identifier (0-based).</summary>
    public int WorkerId { get; init; }

    /// <summary>Whether this worker voted pass.</summary>
    public bool VotedPass { get; init; }

    /// <summary>Worker's reasoning.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>Worker's confidence score (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>SHA256 hash of the worker's response for vote grouping.</summary>
    public string ResponseHash { get; init; } = string.Empty;
}

/// <summary>
/// Summary of a single voting round.
/// </summary>
public sealed record VoteRoundSummary
{
    /// <summary>Round number (1-based).</summary>
    public int Round { get; init; }

    /// <summary>Vote distribution: response hash to vote count.</summary>
    public required Dictionary<string, int> Distribution { get; init; }

    /// <summary>Whether consensus was reached in this round.</summary>
    public bool ConsensusReached { get; init; }

    /// <summary>Winning response hash if consensus reached, null otherwise.</summary>
    public string? WinnerHash { get; init; }
}
