using Microsoft.Extensions.Configuration;
using ScientificResearchAssistant.Vibe.Tools;

namespace ScientificResearchAssistant.Vibe;

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

        AllowDangerousTools = _pythonEnabled;

        SystemPrompt =
            """
            You are a verifier.

            Goal:
            - Verify the most critical claims using concrete checks.

            Rules:
            - If you cannot verify with available sources/tools, say "NOT VERIFIED" and state what is missing.
            - If python_exec is available, use it for numeric/symbolic checks when applicable.
            - Keep output short and structured:
              - Claim
              - Check
              - Result (VERIFIED / NOT VERIFIED / INCONCLUSIVE)
              - Notes
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


