namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Domain service interface for DAG consensus runner.
/// Runs consensus validation/synthesis on DAG mutation candidates.
/// </summary>
public interface IDagConsensusService
{
    /// <summary>
    /// Runs consensus validation/synthesis on a DAG mutation candidate.
    /// </summary>
    Task<ConsensusResult> RunAsync(ConsensusInput input, CancellationToken ct);
}

/// <summary>
/// Input data for consensus validation.
/// </summary>
public record ConsensusInput
{
    public string SessionId { get; init; } = string.Empty;
    public string MutationCandidate { get; init; } = string.Empty;
}

/// <summary>
/// Result of consensus validation.
/// </summary>
public record ConsensusResult
{
    public bool IsValid { get; init; }
    public string Reasoning { get; init; } = string.Empty;
    public List<string> Issues { get; init; } = new();
}

/// <summary>
/// Default pass-through implementation.
/// Always accepts mutations (no validation gate).
/// </summary>
public sealed class DefaultDagConsensusService : IDagConsensusService
{
    public Task<ConsensusResult> RunAsync(ConsensusInput input, CancellationToken ct) =>
        Task.FromResult(new ConsensusResult { IsValid = true, Reasoning = "pass-through (no consensus gate configured)" });
}
