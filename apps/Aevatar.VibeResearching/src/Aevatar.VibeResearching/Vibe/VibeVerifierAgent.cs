using Microsoft.Extensions.Configuration;
using VibeResearching.Vibe.Tools;

namespace VibeResearching.Vibe;

// ============================================================
//  VibeVerifierAgent
//
//  Role:
//  - Hard verification of key claims.
//  - If python_exec is enabled, use it to validate computations.
//  - Supports multi-stage verification with different worker roles.
//
//  Output:
//  - JSON with: accept (bool), reason (string)
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

        // Default system prompt (will be overridden by worker-specific prompts)
        SystemPrompt = DefaultSystemPrompt;
    }

    /// <summary>
    /// Default system prompt for backward compatibility.
    /// </summary>
    public const string DefaultSystemPrompt =
        """
        You are a verifier.

        Goal:
        - Verify the most critical claims using concrete checks.

        Rules:
        - If you cannot verify with available evidence/tools, say "NOT VERIFIED" and state what is missing.
        - If python_exec is available, use it for numeric/symbolic checks when applicable.
        - Keep output short and structured:
          - Claim
          - Check
          - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
          - Notes
        """;

    /// <summary>
    /// Build a worker-specific system prompt for multi-stage verification.
    /// </summary>
    public static string BuildWorkerSystemPrompt(string workerId, string role, string angle)
    {
        return $"""
            You are {role} ({workerId}).
            Angle: {angle}

            Task:
            Evaluate the reasoning output from the Reasoner. Your goal is to determine if the reasoning is valid.

            CRITICAL OUTPUT REQUIREMENTS:
            - Return ONLY valid JSON (no markdown, no code blocks, no commentary, no ```json tags).
            - Output the JSON object EXACTLY ONCE. Do NOT repeat any fields, values, or fragments.
            - Do NOT append anything after the closing brace of the JSON object.
            - After outputting the closing brace, STOP immediately. Do NOT continue with any text.

            Evaluation Rules:
            - accept=true if the reasoning is logically sound and consistent with the provided materials/context.
            - accept=false if you find:
              1. Explicit contradiction with provided facts
              2. A fatal counterexample
              3. A crucial missing assumption that invalidates the reasoning
              4. A logical leap that cannot be justified

            IMPORTANT:
            - "Requires additional knowledge to prove rigorously" does NOT mean accept=false.
            - Only reject if the reasoning CONTRADICTS the facts or is logically inconsistent.
            - If the reasoning is plausible and consistent, set accept=true even if you cannot fully verify every step.

            Output JSON schema:
            {"{"}
              "worker_id": "{workerId}",
              "accept": bool,
              "reason": string
            {"}"}
            """;
    }

    // ============================================================
    //  Pre-Processing Phase Prompts (Phase 0)
    // ============================================================

    /// <summary>
    /// Step 1: Extract all hypotheses from reasoner output.
    /// </summary>
    public static string BuildHypothesisExtractionPrompt()
    {
        return """
            You are a Hypothesis Extractor.

            Task:
            Analyze the reasoner output and extract ALL hypotheses, claims, or propositions that need verification.

            CRITICAL OUTPUT REQUIREMENTS:
            - Return ONLY valid JSON (no markdown, no code blocks, no commentary).
            - Output the JSON object EXACTLY ONCE.
            - Do NOT append anything after the closing brace.

            Extraction Rules:
            - Extract each distinct hypothesis/claim/proposition as a separate item.
            - Include mathematical statements, logical claims, and factual assertions.
            - Assign a unique ID to each hypothesis (H1, H2, H3, ...).
            - Estimate confidence (0.0-1.0) based on how well-supported the claim appears.
            - Include relevant context that helps understand the hypothesis.

            Output JSON schema:
            {
              "hypotheses": [
                {
                  "id": "H1",
                  "statement": "The exact statement of the hypothesis",
                  "context": "Relevant context or background",
                  "confidence": 0.8
                }
              ],
              "total_count": number
            }
            """;
    }

    /// <summary>
    /// Step 2: Select the easiest hypothesis to verify.
    /// </summary>
    public static string BuildHypothesisSelectionPrompt()
    {
        return """
            You are a Hypothesis Selector.

            Task:
            From the list of extracted hypotheses, select the ONE that is EASIEST to verify as correct.

            CRITICAL OUTPUT REQUIREMENTS:
            - Return ONLY valid JSON (no markdown, no code blocks, no commentary).
            - Output the JSON object EXACTLY ONCE.
            - Do NOT append anything after the closing brace.

            Selection Criteria (in order of priority):
            1. Has clear, well-defined terms and conditions
            2. Can be verified using available DAG facts/theorems
            3. Has minimal external dependencies
            4. Is self-contained and doesn't require complex chains of reasoning
            5. Has higher confidence score from extraction

            Output JSON schema:
            {
              "selected_hypothesis_id": "H1",
              "selected_statement": "The exact statement",
              "selection_reason": "Why this hypothesis is easiest to verify",
              "verification_approach": "Brief description of how to verify it"
            }
            """;
    }

    /// <summary>
    /// Step 3: Decompose hypothesis and find DAG dependencies.
    /// </summary>
    public static string BuildHypothesisDecompositionPrompt()
    {
        return """
            You are a Hypothesis Decomposer.

            Task:
            Decompose the selected hypothesis and identify the dependency theorems from the DAG that support it.
            Output the dependencies in derivation order (from axioms/base facts to the hypothesis).

            CRITICAL OUTPUT REQUIREMENTS:
            - Return ONLY valid JSON (no markdown, no code blocks, no commentary).
            - Output the JSON object EXACTLY ONCE.
            - Do NOT append anything after the closing brace.

            Decomposition Rules:
            1. Identify all DAG nodes (axioms, theorems, facts) that the hypothesis depends on.
            2. Order them by derivation sequence (what needs to be established first).
            3. Explain the logical chain from dependencies to the hypothesis.
            4. Note any gaps where additional assumptions might be needed.

            Output JSON schema:
            {
              "hypothesis_id": "H1",
              "hypothesis_statement": "The statement being decomposed",
              "dependencies": [
                {
                  "id": "T1",
                  "statement": "Statement of the dependency",
                  "derivation_order": 1
                }
              ],
              "derivation_path": ["T1", "T2", "T3", "H1"],
              "decomposition_reason": "Explanation of the logical chain",
              "gaps": ["Any identified gaps or missing assumptions"]
            }
            """;
    }

    /// <summary>
    /// Build worker-specific system prompt for Scout phase with decomposed hypothesis.
    /// </summary>
    public static string BuildScoutWorkerPromptWithDecomposition(
        string workerId, 
        string role, 
        string angle,
        string decomposedHypothesis,
        string derivationPath)
    {
        return $"""
            You are {role} ({workerId}).
            Angle: {angle}

            Task:
            Evaluate the following DECOMPOSED hypothesis. The hypothesis has been broken down with its dependency chain.

            === DECOMPOSED HYPOTHESIS ===
            {decomposedHypothesis}

            === DERIVATION PATH ===
            {derivationPath}
            === END ===

            Your goal is to determine if this hypothesis is valid given its dependencies.

            CRITICAL OUTPUT REQUIREMENTS:
            - Return ONLY valid JSON (no markdown, no code blocks, no commentary).
            - Output the JSON object EXACTLY ONCE.
            - Do NOT append anything after the closing brace.

            Evaluation Rules:
            - accept=true if the hypothesis logically follows from its dependencies.
            - accept=false if you find:
              1. A contradiction with the dependencies
              2. A fatal counterexample
              3. A missing step in the derivation that cannot be bridged
              4. An invalid logical leap

            IMPORTANT:
            - Focus on the derivation path provided.
            - Check if each step in the path is valid.
            - "Requires external knowledge" does NOT mean accept=false if the logic is sound.

            Output JSON schema:
            {"{"}
              "worker_id": "{workerId}",
              "accept": bool,
              "reason": string
            {"}"}
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


