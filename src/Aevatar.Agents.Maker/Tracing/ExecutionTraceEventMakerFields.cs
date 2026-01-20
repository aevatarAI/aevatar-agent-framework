namespace Aevatar.Agents.Abstractions.Tracing;

// ============================================================
//  ExecutionTraceEvent Fields (Maker Extension)
//
//  Maker / Consensus / Proposal specific fields for execution trace events.
// ============================================================
public static class ExecutionTraceEventMakerFields
{
    // Voting / consensus
    public const string VoteRound = "vote_round";
    public const string VoteMaxRounds = "vote_max_rounds";
    public const string VoteK = "vote_k";
    public const string VoteCurrentVotes = "vote_current_votes";
    public const string WinnerProposalId = "winner_proposal_id";
    public const string WinnerHash = "winner_hash";
    public const string WinnerVotes = "winner_votes";
    public const string WinnerRunnerUpVotes = "winner_runner_up_votes";
    public const string WinnerClusterCount = "winner_cluster_count";
    public const string WinnerSemantic = "winner_semantic";
    public const string WinnerIsConsensus = "winner_is_consensus";

    // Worker / proposal
    public const string WorkerId = "worker_id";
    public const string ProposalId = "proposal_id";
}
