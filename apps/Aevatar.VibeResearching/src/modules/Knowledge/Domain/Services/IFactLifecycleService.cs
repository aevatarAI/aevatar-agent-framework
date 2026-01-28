using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Domain service interface for fact lifecycle management.
/// Manages the full lifecycle: proposal → voting/verification → decision → DAG promotion.
/// </summary>
public interface IFactLifecycleService
{
    /// <summary>
    /// Creates a new fact proposal in the session workspace.
    /// </summary>
    Task<FactProposal> CreateProposalAsync(
        string sessionId,
        string title,
        string content,
        IEnumerable<string>? evidencePaths,
        string proposedBy,
        CancellationToken ct);

    /// <summary>
    /// Records a reviewer vote for a fact proposal.
    /// </summary>
    Task RecordVoteAsync(string sessionId, FactVote vote, CancellationToken ct);

    /// <summary>
    /// Records a hard verification result for a fact proposal.
    /// </summary>
    Task RecordVerificationAsync(string sessionId, FactVerification verification, CancellationToken ct);

    /// <summary>
    /// Evaluates votes/verifications and promotes the fact to the DAG if accepted.
    /// Returns null if no final decision can be made yet.
    /// </summary>
    Task<FactDecision?> EvaluateAndPromoteAsync(
        string sessionId,
        string dagId,
        string factId,
        string finalizedBy,
        CancellationToken ct);
}
