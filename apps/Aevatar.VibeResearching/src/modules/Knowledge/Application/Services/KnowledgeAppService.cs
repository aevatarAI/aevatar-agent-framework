using Volo.Abp.Application.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using ContractFactVoteValue = Aevatar.VibeResearching.Agents.Contracts.Collab.FactVoteValue;

namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Application service for knowledge DAG and facts management.
/// Delegates to DAG repository and fact lifecycle services.
/// </summary>
public class KnowledgeAppService : ApplicationService, IKnowledgeAppService
{
    private readonly IDagRepository _dagRepository;
    private readonly FactLifecycleService _factLifecycle;
    private readonly IDagExplainService _dagExplain;

    public KnowledgeAppService(
        IDagRepository dagRepository,
        FactLifecycleService factLifecycle,
        IDagExplainService dagExplain)
    {
        _dagRepository = dagRepository;
        _factLifecycle = factLifecycle;
        _dagExplain = dagExplain;
    }

    /// <inheritdoc/>
    public async Task<SraDagSnapshot> GetDagAsync(string sessionId, CancellationToken ct = default)
    {
        return await _dagRepository.LoadMergedSnapshotAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<FactProposal> SaveFactAsync(string sessionId, SaveFactDto input, CancellationToken ct = default)
    {
        return await _factLifecycle.CreateProposalAsync(
            sessionId,
            input.Title ?? string.Empty,
            input.Content ?? string.Empty,
            evidencePaths: input.EvidencePaths?.ToList(),
            proposedBy: input.ProposedBy ?? "api",
            ct);
    }

    /// <inheritdoc/>
    public async Task VoteFactAsync(string sessionId, string factId, FactVoteDto input, CancellationToken ct = default)
    {
        var voteRaw = (input.Vote ?? string.Empty).Trim().ToLowerInvariant();
        var voteValue = voteRaw switch
        {
            "approve" => ContractFactVoteValue.Approve,
            "reject" => ContractFactVoteValue.Reject,
            "needs_work" or "needswork" => ContractFactVoteValue.NeedsWork,
            _ => ContractFactVoteValue.Unspecified
        };

        var vote = new FactVote
        {
            FactId = factId,
            ReviewerId = input.ReviewerId ?? string.Empty,
            Vote = voteValue,
            Comment = input.Comment ?? string.Empty
        };

        await _factLifecycle.RecordVoteAsync(sessionId, vote, ct);
    }

    /// <inheritdoc/>
    public async Task VerifyFactAsync(string sessionId, string factId, FactVerificationDto input, CancellationToken ct = default)
    {
        var verification = new FactVerification
        {
            FactId = factId,
            VerifierId = input.VerifierId ?? string.Empty,
            Tool = input.Tool ?? "unknown",
            Result = input.Result ?? false,
            LogExcerpt = input.LogExcerpt ?? string.Empty
        };

        if (input.ArtifactPaths != null)
        {
            foreach (var path in input.ArtifactPaths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    verification.ArtifactPaths.Add(path);
                }
            }
        }

        await _factLifecycle.RecordVerificationAsync(sessionId, verification, ct);
    }

    /// <inheritdoc/>
    public async Task<FactDecision?> PromoteFactAsync(string sessionId, string factId, PromoteFactDto input, CancellationToken ct = default)
    {
        // NOTE: dagId is not in the DTO, we need to get it from session or use a default
        // For now, using sessionId as dagId (common pattern)
        return await _factLifecycle.EvaluateAndPromoteAsync(
            sessionId,
            dagId: sessionId,
            factId,
            finalizedBy: input.FinalizedBy ?? "api",
            ct);
    }

    /// <inheritdoc/>
    public async Task<SraDagExplain> ExplainNodeAsync(string sessionId, string nodeId, CancellationToken ct = default)
    {
        return await _dagExplain.ExplainNodeAsync(sessionId, nodeId, ct);
    }
}
