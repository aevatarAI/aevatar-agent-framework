using Microsoft.Extensions.Configuration;
using ScientificResearchAssistant.Vibe.Tools;

namespace ScientificResearchAssistant.Vibe;

// ============================================================
//  VibeReasonerAgent
//
//  Role:
//  - Produce reasoning grounded in provided materials (NotebookLM-style sources).
//  - Optionally run python_exec for verification (opt-in, dangerous).
//
//  Borrowed from Aevatar.AxiomReasoning (spirit):
//  - "Do not change the core workflow; project at boundary."
//  Here: we keep a simple LLM agent, but make it grounded and verifiable.
// ============================================================

public sealed class VibeReasonerAgent : VibeAgentBase
{
    private readonly bool _pythonEnabled;
    private readonly int _pythonTimeoutMs;
    private readonly int _pythonMaxOutputChars;

    public VibeReasonerAgent(IConfiguration configuration)
    {
        var python = configuration?.GetSection("Python");
        _pythonEnabled = python?.GetValue<bool?>("Enabled") ?? false;
        _pythonTimeoutMs = python?.GetValue<int?>("TimeoutMs") ?? 15_000;
        _pythonMaxOutputChars = python?.GetValue<int?>("MaxOutputChars") ?? 8_000;

        // Dangerous tools are hidden by default; opt-in via config.
        AllowDangerousTools = _pythonEnabled;

        SystemPrompt =
            """
            You are a research reasoner grounded in provided materials (sources).

            Inputs:
            - A user question
            - "Materials context" (sources) when present

            Rules:
            - Ground every non-trivial claim in either:
              (a) a material id like [material:...], or
              (b) clearly marked as a hypothesis.
            - If the materials do not support a claim, say so and ask for a missing source.
            - When computation is needed, use python_exec (if available) to verify.
            - Keep reasoning structured and concise; output should be readable in Markdown.
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


