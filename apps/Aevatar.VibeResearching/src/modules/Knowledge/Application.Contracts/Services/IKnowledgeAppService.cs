using Volo.Abp.Application.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Application service for knowledge DAG and facts management.
/// </summary>
public interface IKnowledgeAppService : IApplicationService
{
    /// <summary>
    /// Gets the current DAG snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>DAG snapshot</returns>
    Task<SraDagSnapshot> GetDagAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new fact proposal.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="input">Fact data</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Created fact proposal</returns>
    Task<FactProposal> SaveFactAsync(string sessionId, SaveFactDto input, CancellationToken ct = default);

    /// <summary>
    /// Records a vote on a fact proposal.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="factId">Fact identifier</param>
    /// <param name="input">Vote data</param>
    /// <param name="ct">Cancellation token</param>
    Task VoteFactAsync(string sessionId, string factId, FactVoteDto input, CancellationToken ct = default);

    /// <summary>
    /// Records a programmatic verification result for a fact.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="factId">Fact identifier</param>
    /// <param name="input">Verification data</param>
    /// <param name="ct">Cancellation token</param>
    Task VerifyFactAsync(string sessionId, string factId, FactVerificationDto input, CancellationToken ct = default);

    /// <summary>
    /// Promotes a fact from proposal to the DAG.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="factId">Fact identifier</param>
    /// <param name="input">Promotion parameters</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Decision result if promotion completed, null if still pending</returns>
    Task<FactDecision?> PromoteFactAsync(string sessionId, string factId, PromoteFactDto input, CancellationToken ct = default);

    /// <summary>
    /// Explains a DAG node with dependency closure.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="nodeId">Node identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>DAG explain result</returns>
    Task<SraDagExplain> ExplainNodeAsync(string sessionId, string nodeId, CancellationToken ct = default);
}
