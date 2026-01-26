namespace VibeResearching.Api.Vibe;

// ============================================================
//  Multi-Stage Verification Models
//
//  Inspired by hypothesis_promotion_loop_hpa.yaml:
//  - Pre-processing: Extract, select, and decompose hypotheses
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

// ============================================================
//  Pre-Processing Models (Phase 0)
// ============================================================

/// <summary>
/// A hypothesis extracted from reasoner output.
/// </summary>
public sealed record ExtractedHypothesis(
    string Id,
    string Statement,
    string? Context,
    double? Confidence
);

/// <summary>
/// A knowledge item extracted from reasoner output (axiom, theorem, definition, or hypothesis).
/// </summary>
public sealed record ExtractedKnowledgeItem(
    string Id,
    string Statement,
    string Type, // "axiom", "theorem", "definition", or "hypothesis"
    string? Context,
    double? Confidence
);

/// <summary>
/// Result of hypothesis extraction (Step 1).
/// Now includes axioms, theorems, definitions, and hypotheses.
/// </summary>
public sealed record HypothesisExtractionResult(
    IReadOnlyList<ExtractedHypothesis> Hypotheses,
    IReadOnlyList<ExtractedKnowledgeItem> Axioms,
    IReadOnlyList<ExtractedKnowledgeItem> Theorems,
    IReadOnlyList<ExtractedKnowledgeItem> Definitions,
    IReadOnlyList<ExtractedKnowledgeItem> AllKnowledgeItems, // Combined list of all types
    string RawOutput,
    string? SystemPrompt,
    string? UserPrompt
);

/// <summary>
/// Result of hypothesis selection (Step 2).
/// </summary>
public sealed record HypothesisSelectionResult(
    ExtractedHypothesis SelectedHypothesis,
    string SelectionReason,
    string RawOutput,
    string? SystemPrompt,
    string? UserPrompt
);

/// <summary>
/// A dependency theorem from DAG.
/// </summary>
public sealed record DependencyTheorem(
    string Id,
    string Statement,
    int DerivationOrder
);

/// <summary>
/// Result of hypothesis decomposition (Step 3).
/// </summary>
public sealed record HypothesisDecompositionResult(
    ExtractedHypothesis Hypothesis,
    IReadOnlyList<DependencyTheorem> Dependencies,
    IReadOnlyList<string> DerivationPath,
    string DecompositionReason,
    string RawOutput,
    string? SystemPrompt,
    string? UserPrompt
);

/// <summary>
/// Aggregated result from pre-processing phase.
/// </summary>
public sealed record PreProcessingResult(
    HypothesisExtractionResult? ExtractionResult,
    HypothesisSelectionResult? SelectionResult,
    HypothesisDecompositionResult? DecompositionResult,
    bool Success,
    string? ErrorMessage
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
/// Result from verifying a single hypothesis in the loop (includes Step 2, Step 3, Phase 1, Phase 2).
/// </summary>
public sealed record HypothesisVerificationResult(
    ExtractedHypothesis Hypothesis,
    HypothesisSelectionResult? SelectionResult,
    HypothesisDecompositionResult? DecompositionResult,
    VerificationPhaseResult? ScoutPhase,
    VerificationPhaseResult? ProverPhase,
    bool VerificationPassed
);

/// <summary>
/// Final result from multi-stage verification.
/// </summary>
public sealed record MultiStageVerificationResult(
    PreProcessingResult? PreProcessing,
    VerificationPhaseResult? ScoutPhase,
    VerificationPhaseResult? ProverPhase,
    bool OverallPass,
    string Summary,
    IReadOnlyList<ExtractedHypothesis> VerifiedHypotheses,
    IReadOnlyList<HypothesisVerificationResult> LoopVerificationResults
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
