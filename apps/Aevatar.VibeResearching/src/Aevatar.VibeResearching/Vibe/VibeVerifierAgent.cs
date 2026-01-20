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


