using System.Threading;
using Aevatar.Agents.Cognitive.Primitives;

namespace Aevatar.Agents.Cognitive.Execution.Run;

public interface IWorkflowRunStepModule
{
    string Name { get; }
    int Priority { get; }

    bool CanHandle(StepDefinition step);
    bool CanHandleType(string stepType);

    Task<PrimitiveResult> ExecuteAsync(
        WorkflowRunContext context,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct);
}

public abstract class WorkflowRunStepModuleBase : IWorkflowRunStepModule
{
    public abstract string Name { get; }
    public virtual int Priority => 0;
    public abstract string StepType { get; }

    public virtual bool CanHandle(StepDefinition step)
        => CanHandleType(step.Type ?? string.Empty);

    public virtual bool CanHandleType(string stepType)
        => string.Equals(stepType, StepType, StringComparison.OrdinalIgnoreCase);

    public abstract Task<PrimitiveResult> ExecuteAsync(
        WorkflowRunContext context,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct);
}
