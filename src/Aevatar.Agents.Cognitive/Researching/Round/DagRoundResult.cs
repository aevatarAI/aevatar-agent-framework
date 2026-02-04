using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Round;

// ============================================================
//  DagRoundResult
//
//  Outcome summary for the "dag_apply" phase of a researching round.
// ============================================================
public sealed record DagRoundResult(
    bool Accepted,
    bool Blocked,
    SraDagMutation? Candidate,
    SraDagMutation? AcceptedMutation,
    string? StagedPath,
    IReadOnlyList<string> RedFlags,
    string? ArtifactPath);

