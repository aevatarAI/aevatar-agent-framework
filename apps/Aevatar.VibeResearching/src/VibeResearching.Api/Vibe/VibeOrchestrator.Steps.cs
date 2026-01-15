using Aevatar.Agents.AGUI;
using VibeResearching.Api.Sessions;

namespace VibeResearching.Api.Vibe;

internal sealed partial class VibeOrchestrator
{
    // ============================================================
    //  Step wrapper helpers
    //
    //  中文说明：
    //  - `StepStartedEvent` / `StepFinishedEvent` 是编排的最小可观测单元
    //  - best-effort stage 必须保证：无论成功/失败都能发出 finished
    //  - cancellation 需要向上抛出（run 取消应立即停止）
    // ============================================================

    private async Task RunStepBestEffortAsync(
        ResearchSession session,
        string stepName,
        Func<CancellationToken, Task> action,
        CancellationToken ct)
    {
        session.Events.Publish(new StepStartedEvent { Timestamp = NowMs(), StepName = stepName });
        try
        {
            await action(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // best-effort only
        }
        finally
        {
            session.Events.Publish(new StepFinishedEvent { Timestamp = NowMs(), StepName = stepName });
        }
    }
}


