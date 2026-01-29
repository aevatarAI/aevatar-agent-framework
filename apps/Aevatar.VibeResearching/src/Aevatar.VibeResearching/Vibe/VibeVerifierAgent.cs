using Microsoft.Extensions.Configuration;
using VibeResearching.Vibe.Tools;

namespace VibeResearching.Vibe;

// ============================================================
//  VibeVerifierAgent
//
//  Role:
//  - Hard verification of key claims.
//  - If python_exec is enabled, use it to validate computations.
//
//  Output:
//  - Markdown with: claim, method, result, and any caveats.
// ============================================================

public sealed class VibeVerifierAgent : VibeAgentBase
{
    private readonly bool _pythonEnabled;
    private readonly int _pythonTimeoutMs;
    private readonly int _pythonMaxOutputChars;

    public VibeVerifierAgent(IConfiguration configuration)
    {
        var python = configuration?.GetSection("Python");
        _pythonEnabled = python?.GetValue<bool?>("Enabled") ?? false;
        _pythonTimeoutMs = python?.GetValue<int?>("TimeoutMs") ?? 15_000;
        _pythonMaxOutputChars = python?.GetValue<int?>("MaxOutputChars") ?? 8_000;

        // Verifier should be stateless across calls:
        // - Avoid cross-round bleed and reduce token/state growth.
        // - Quorum consensus may reuse multiple verifier instances.
        EnableChatHistoryInState = false;
        EnableChatHistoryCompaction = false;
        ChatHistoryMaxMessages = 0;
        ChatHistorySummaryMaxChars = 0;

        AllowDangerousTools = _pythonEnabled;

        SystemPrompt = GetSystemPrompt();
    }

    public static string GetSystemPrompt()
    {
        return """
            You are a verifier.

            Goal:
            - Verify the most critical claims using concrete checks.

            Rules:
            - If a hypothesis is CONSISTENT with the axioms/facts and represents a reasonable mathematical claim, it should be considered as "VERIFIED", even if proving it rigorously would require additional mathematical knowledge (theta series, mass formulas, combinatorial theorems, etc.).
            - Only consider "NOT VERIFIED"if the hypothesis CONTRADICTS the axioms/facts or is logically inconsistent.
            - If python_exec is available, use it for numeric/symbolic checks when applicable.
            - Keep output short and structured:
              - Claim
              - Check
              - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
              - Notes
            """;
    }

    public static string GetScoutSystemPrompt(string focus)
    {
        var focusSpecific = focus switch
        {
            "counterexample" => """
                Focus: Counterexample Detection

                Your specific task:
                - Search for concrete examples where the claim fails.
                - Use python_exec (if available) to test edge cases.
                - Look for boundary conditions, special cases, or exceptions.
                - If you find ANY counterexample, recommend BLOCK.
                """,
            "premise" => """
                Focus: Missing Premise Detection

                Your specific task:
                - Identify assumptions or facts that are used but not explicitly stated.
                - Check if all dependencies are properly cited.
                - Look for logical gaps in the reasoning chain.
                - If you find CRITICAL missing premises, recommend BLOCK.
                """,
            _ => ""
        };

        return $"""
            You are a verification scout in a multi-stage verification process.

            Goal:
            - Quickly detect counterexamples and missing premises in the reasoning chain.
            - Focus on fast, high-confidence detection rather than deep proof.

            Role:
            - You are the FIRST stage of verification (Scout phase).
            - Your job is to quickly identify obvious problems before deeper verification.

            Rules:
            - Look for COUNTEREXAMPLES: concrete cases where the claim fails.
            - Look for MISSING PREMISES: assumptions or facts that are used but not stated.
            - If python_exec is available, use it for quick counterexample searches.
            - Keep output VERY SHORT and focused:
              - Counterexamples found (if any)
              - Missing premises identified (if any)
              - Quick assessment: PROCEED / BLOCK
            - If you find clear counterexamples or critical missing premises, recommend BLOCK.
            - If no obvious issues, recommend PROCEED to Prover phase.

            Output format:
            - Counterexamples: [list any found]
            - Missing Premises: [list any identified]
            - Recommendation: PROCEED / BLOCK
            - Brief Reason: [one sentence]

            {focusSpecific}
            """;
    }

    public static string GetProverSystemPrompt()
    {
        return """
            You are a verification prover in a multi-stage verification process.

            Goal:
            - Verify the reasoning process correctness through deep analysis.
            - Provide detailed verification of claims and hypotheses.

            Role:
            - You are the SECOND stage of verification (Prover phase).
            - Your job is to perform thorough verification after Scout phase screening.

            Rules:
            - If a hypothesis is CONSISTENT with the axioms/facts and represents a reasonable mathematical claim, it should be considered as "VERIFIED", even if proving it rigorously would require additional mathematical knowledge (theta series, mass formulas, combinatorial theorems, etc.).
            - Only consider "NOT VERIFIED" if the hypothesis CONTRADICTS the axioms/facts or is logically inconsistent.
            - If python_exec is available, use it for numeric/symbolic checks when applicable.
            - Keep output structured:
              - Claim
              - Check
              - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
              - Notes
            """;
    }

    public static string GetProofExtractionSystemPrompt()
    {
        return """
            You are a proof extractor in a multi-stage verification process.

            Goal:
            - Extract and synthesize proof information from verification results.
            - Provide a concise, structured proof summary if verification passed.

            Role:
            - You are the FINAL stage of verification (Proof Extraction phase).
            - Your job is to synthesize proof information from Scout and Prover phases.

            Rules:
            - ONLY extract proof if verification PASSED (Overall Result: PASSED).
            - If verification FAILED, return empty proof (Proof: "").
            - Extract key verification methods, checks, and reasoning from Prover workers' outputs.
            - Synthesize a concise proof summary (max 1200 characters) that explains:
              - Key verification methods used
              - Critical checks performed
              - Reasoning that supports the verification
            - Focus on the most important and convincing aspects of the verification.
            - If multiple Prover workers verified, synthesize their common verification approaches.

            Output format:
            - If verification PASSED: Provide proof summary
            - If verification FAILED: Return empty string (Proof: "")
            """;
    }

    protected override async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        await base.RegisterToolsAsync(cancellationToken);

        if (_pythonEnabled)
        {
            await RegisterToolAsync(
                new PythonExecTool(new PythonExecToolOptions
                {
                    TimeoutMs = _pythonTimeoutMs,
                    MaxOutputChars = _pythonMaxOutputChars
                }),
                cancellationToken: cancellationToken);
        }
    }
}


