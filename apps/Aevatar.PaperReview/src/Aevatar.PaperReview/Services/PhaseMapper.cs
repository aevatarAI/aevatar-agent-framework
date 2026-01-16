using Aevatar.PaperReview.Models;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  PHASE MAPPER - 可复用的阶段映射器
// ============================================================

/// <summary>
/// 阶段映射器 - 将 Cognitive DSL 阶段映射为 ReviewPhase。
/// </summary>
public static class PhaseMapper
{
    private static readonly (string keyword, ReviewPhase phase)[] StepIdMappings =
    [
        ("check_atomic", ReviewPhase.Assessing),
        ("decompose", ReviewPhase.Decomposing),
        ("compose", ReviewPhase.Composing),
        ("solve", ReviewPhase.Solving),
        ("execute", ReviewPhase.Executing),
    ];

    private static readonly (string prefix, ReviewPhase phase)[] PhasePrefixMappings =
    [
        ("ANALYZE", ReviewPhase.Assessing),
        ("ASSESS", ReviewPhase.Assessing),
        ("DECOMPOSE", ReviewPhase.Decomposing),
        ("COMPOSE", ReviewPhase.Composing),
        ("SOLVE", ReviewPhase.Solving),
        ("EXECUTE", ReviewPhase.Executing),
        ("START", ReviewPhase.Starting),
        ("LOAD", ReviewPhase.Starting),
        ("INIT", ReviewPhase.Starting),
        ("VOTING", ReviewPhase.Voting),
        ("VOTE", ReviewPhase.Voting),
        ("COMPLETE", ReviewPhase.Completed),
        ("RESULT", ReviewPhase.Completed),
        ("FAIL", ReviewPhase.Failed),
        ("ERROR", ReviewPhase.Failed),
    ];

    private static readonly Dictionary<string, ReviewPhase> StepTypeMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vote"] = ReviewPhase.Voting,
            ["fan_out"] = ReviewPhase.Executing,
            ["parallel"] = ReviewPhase.Executing,
            ["llm_call"] = ReviewPhase.Solving,
        };

    public static ReviewPhase Map(string phase, string stepType, string stepId)
    {
        var stepIdLower = stepId.ToLowerInvariant();
        var phaseUpper = phase.ToUpperInvariant();

        // 1. Check stepId keywords first (most specific)
        foreach (var (keyword, mappedPhase) in StepIdMappings)
            if (stepIdLower.Contains(keyword))
                return mappedPhase;

        // 2. Check phase prefix
        foreach (var (prefix, mappedPhase) in PhasePrefixMappings)
            if (phaseUpper.StartsWith(prefix) || phaseUpper.Contains(prefix))
                return mappedPhase;

        // 3. Check stepType mapping
        if (StepTypeMappings.TryGetValue(stepType, out var typeMappedPhase))
            return typeMappedPhase;
        
        // 4. Default to Voting (safe fallback during vote steps) instead of Solving
        // This prevents premature "Solving" display during decompose voting
        return ReviewPhase.Voting;
    }
}