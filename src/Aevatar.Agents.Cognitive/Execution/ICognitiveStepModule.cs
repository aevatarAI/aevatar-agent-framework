using System.Threading;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Primitives;

namespace Aevatar.Agents.Cognitive.Execution;

public interface ICognitiveStepModule
{
    bool CanHandle(StepDefinition step);
    bool CanHandleType(string stepType);
    Task<PrimitiveResult> ExecuteAsync(
        CoordinatorAgent agent,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct);
}
