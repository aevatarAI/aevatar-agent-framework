namespace VibeResearching.Api.Vibe;

// ============================================================
//  Multi-Stage Verification Models
//
//  Inspired by hypothesis_promotion_loop_hpa.yaml:
//  - Scout phase: 2 workers for quick refutation detection
//  - Prover phase: 5 workers for proof verification
// ============================================================

/// <summary>
/// Defines a verification worker with a specific role and angle.
/// </summary>
public sealed record VerificationWorker(
    string Id,
    string Role,
    string Angle
);

/// <summary>
/// Result from a single verification worker.
/// </summary>
public sealed record VerificationWorkerResult(
    string WorkerId,
    bool Accept,
    string Reason,
    string RawOutput,
    string? SystemPrompt = null,
    string? UserPrompt = null
);

/// <summary>
/// Aggregated result from a verification phase.
/// </summary>
public sealed record VerificationPhaseResult(
    string PhaseName,
    IReadOnlyList<VerificationWorkerResult> Results,
    int AcceptCount,
    int RejectCount,
    bool PhasePass
);

/// <summary>
/// Final result from multi-stage verification.
/// </summary>
public sealed record MultiStageVerificationResult(
    VerificationPhaseResult? ScoutPhase,
    VerificationPhaseResult? ProverPhase,
    bool OverallPass,
    string Summary
);

/// <summary>
/// Scout workers for quick refutation detection.
/// </summary>
public static class ScoutWorkers
{
    public static readonly IReadOnlyList<VerificationWorker> All = new[]
    {
        new VerificationWorker(
            Id: "scout-0",
            Role: "Counterexample hunter",
            Angle: "Try to find a fatal counterexample or contradiction quickly."
        ),
        new VerificationWorker(
            Id: "scout-1",
            Role: "Missing-premise hunter",
            Angle: "Try to find the minimal missing assumption that breaks the hypothesis."
        )
    };
}

/// <summary>
/// Prover workers for proof verification.
/// </summary>
public static class ProverWorkers
{
    public static readonly IReadOnlyList<VerificationWorker> All = new[]
    {
        new VerificationWorker(
            Id: "prover-0",
            Role: "Direct prover",
            Angle: "Try to construct the shortest proof path."
        ),
        new VerificationWorker(
            Id: "prover-1",
            Role: "Algebraic manipulator",
            Angle: "Try algebraic/rewriting transformations; simplify aggressively."
        ),
        new VerificationWorker(
            Id: "prover-2",
            Role: "Dependency minimalist",
            Angle: "Try to reduce dependency set; prefer proofs close to axioms."
        ),
        new VerificationWorker(
            Id: "prover-3",
            Role: "Case-split specialist",
            Angle: "Try a structured case analysis; look for missing branches."
        ),
        new VerificationWorker(
            Id: "prover-4",
            Role: "Proof auditor",
            Angle: "Audit for hidden leaps; insist on explicit justification."
        )
    };

    /// <summary>
    /// Minimum number of accept votes required to pass prover phase.
    /// </summary>
    public const int MinAcceptCount = 3;
}
