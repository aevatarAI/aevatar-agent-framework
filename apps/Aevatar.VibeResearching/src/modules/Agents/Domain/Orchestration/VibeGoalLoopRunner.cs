using Microsoft.Extensions.Logging;
using Aevatar.Agents.AGUI;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Agents.Orchestration;

namespace Aevatar.VibeResearching.Agents;

// ============================================================
//  VibeGoalLoopRunner (outer loop)
//
//  Purpose:
//  - Repeat Vibe single-round orchestrator until:
//      - goal verifier passes (Task 10), OR
//      - budget exhausted (max_iterations / max_total_duration), OR
//      - cancellation
//
//  Notes:
//  - This runner is best-effort and UI-friendly: never crash server due to projection.
//  - Session.RunLock MUST be held by caller (ResearchRunExecutor) to avoid concurrent runs.
// ============================================================

public sealed class VibeGoalLoopRunner
{
    private const int DefaultMaxIterations = 3;
    private const int DefaultMaxTotalDurationMs = 5 * 60 * 1000; // 5min

    private readonly VibeOrchestrator _vibe;
    private readonly ILogger<VibeGoalLoopRunner> _logger;

    public VibeGoalLoopRunner(
        VibeOrchestrator vibe,
        ILogger<VibeGoalLoopRunner> logger)
    {
        _vibe = vibe ?? throw new ArgumentNullException(nameof(vibe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal async Task<VibeLoopResult> ExecuteUntilGoalAsync(
        ResearchSession session,
        string runId,
        SessionInputInDto input,
        string question,
        MaterialsSnapshot materials,
        string? providerOverride,
        Action<string> emitAssistantDelta,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(materials);
        emitAssistantDelta ??= _ => { };

        var loop = input.Loop;
        var maxIterations = Math.Clamp(loop?.MaxIterations ?? DefaultMaxIterations, 1, 100);
        var maxTotalMs = Math.Clamp(loop?.MaxTotalDurationMs ?? DefaultMaxTotalDurationMs, 1_000, 60 * 60 * 1000);

        var startedAt = DateTimeOffset.UtcNow;
        var stopReason = "limit";
        var executed = 0;

        for (var i = 1; i <= maxIterations; i++)
        {
            ct.ThrowIfCancellationRequested();

            var elapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
            if (elapsedMs >= maxTotalMs)
            {
                stopReason = "timeout";
                break;
            }

            var stepName = $"vibe.loop.round_{i}";
            session.Events.Publish(new StepStartedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                StepName = stepName
            });

            try
            {
                await _vibe.ExecuteOneRoundAsync(
                    session,
                    runId,
                    input,
                    question,
                    materials,
                    providerOverride,
                    emitAssistantDelta,
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort: surface error to UI but continue until budget exhausted.
                _logger.LogError(ex, "[VibeLoop] round {Round} failed: {Message}", i, ex.Message);
                session.Events.Publish(new CustomEvent
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Name = "aevatar.vibe.loop_round_error",
                    Value = new
                    {
                        round = i,
                        message = ex.Message
                    }
                });
            }

            session.Events.Publish(new StepFinishedEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                StepName = stepName
            });

            executed = i;

            // NOTE:
            // - Verifier wiring is implemented in Task 10.
            // - Until then, we always continue until budget exhaustion.
        }

        return new VibeLoopResult
        {
            Ok = stopReason == "completed",
            StopReason = stopReason,
            IterationsExecuted = executed,
            MaxIterations = maxIterations,
            MaxTotalDurationMs = maxTotalMs
        };
    }
}

internal sealed class VibeLoopResult
{
    public bool Ok { get; init; }
    public string StopReason { get; init; } = "limit"; // completed | timeout | limit | cancelled | failed
    public int IterationsExecuted { get; init; }
    public int MaxIterations { get; init; }
    public int MaxTotalDurationMs { get; init; }
}

