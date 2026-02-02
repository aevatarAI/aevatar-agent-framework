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
    public static string GetSystemPrompt()
    {
        return """
            You are a research planner.

            Inputs:
            - A user question (which may contain a milestone goal that specifies what needs to be executed)
            - "Materials context" (axioms + references) when available

            Rules:
            - Be explicit about assumptions and unknowns.
            - Separate what is derivable from axioms vs what requires empirical/extra references.
            - Produce a short plan with steps that can be executed (including computations if needed).
            - If you propose computations, specify what to compute and what would falsify the hypothesis.
            - CRITICAL: If the question contains a milestone goal (e.g., "extract pages X-Y", "verify Z", "identify A"), 
              you MUST create a plan that ACTUALLY EXECUTES those specific tasks. Do not just evaluate whether they're done - 
              create a plan to DO them.
            """;
    }

    public VibePlannerAgent()
    {
        SystemPrompt = GetSystemPrompt();
    }
}


