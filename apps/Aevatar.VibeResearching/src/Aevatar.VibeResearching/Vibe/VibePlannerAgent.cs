namespace VibeResearching.Vibe;

// ============================================================
//  VibePlannerAgent
//
//  Role:
//  - Turn a research question into an executable plan:
//    hypotheses, unknowns, required evidence, proposed experiments.
//
//  Output:
//  - Concise bullet points; explicitly separate "axiom-grounded" vs "needs evidence".
// ============================================================

public sealed class VibePlannerAgent : VibeAgentBase
{
    public const string DefaultSystemPrompt =
        """
        You are a research planner.

        Inputs:
        - A user question
        - "Materials context" (axioms + references) when available

        Rules:
        - Be explicit about assumptions and unknowns.
        - Separate what is derivable from axioms vs what requires empirical/extra references.
        - Produce a short plan with steps that can be executed (including computations if needed).
        - If you propose computations, specify what to compute and what would falsify the hypothesis.
        """;

    public VibePlannerAgent()
    {
        SystemPrompt = DefaultSystemPrompt;
    }

    public static string GetSystemPrompt() => DefaultSystemPrompt;
}


