using System.Runtime.CompilerServices;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions.Tracing;

namespace Aevatar.Agents.Cognitive.AgUi;

// ============================================================
//  Cognitive -> AG-UI Event Stream (framework-level helper)
//
//  Purpose:
//  - Provide a reusable projection from unified ExecutionTraceEvent
//    into AG-UI events (STEP + CUSTOM).
//  - Keep it runtime-agnostic: caller decides how to source events and
//    where to publish (SSE/WebSocket/etc).
// ============================================================
public static class CognitiveAgUiEventStream
{
    public static async IAsyncEnumerable<AgUiEvent> BuildAsync(
        IAsyncEnumerable<ExecutionTraceEvent> source,
        IReadOnlyList<AgUiEvent>? initialEvents = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (initialEvents is { Count: > 0 })
        {
            for (var i = 0; i < initialEvents.Count; i++)
            {
                var e = initialEvents[i];
                if (e != null)
                    yield return e;
            }
        }

        await foreach (var evt in source.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<AgUiEvent> mapped;
            try
            {
                mapped = AgUiTraceProjector.Map(evt);
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < mapped.Count; i++)
            {
                yield return mapped[i];
            }
        }
    }
}
