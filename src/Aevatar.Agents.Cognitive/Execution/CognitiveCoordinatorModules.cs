using System.Threading;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Microsoft.Extensions.Logging;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;

namespace Aevatar.Agents.Cognitive.Execution;

public abstract class CognitiveStepModuleBase : IEventModule, ICognitiveStepModule, IRouteBypassModule
{
    public abstract string Name { get; }
    public virtual int Priority => 0;
    public abstract string StepType { get; }

    public virtual bool CanHandle(StepDefinition step)
        => CanHandleType(step.Type ?? string.Empty);

    public virtual bool CanHandleType(string stepType)
        => string.Equals(stepType, StepType, StringComparison.OrdinalIgnoreCase);

    public abstract Task<PrimitiveResult> ExecuteAsync(
        CoordinatorAgent agent,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct);

    // Step modules do not participate in EventEnvelope routing by default.
    public virtual bool CanHandle(EventEnvelope envelope) => false;
    public virtual Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
        => Task.CompletedTask;
}

public sealed class CoordinatorStepModule : CognitiveStepModuleBase
{
    private readonly string _name;
    private readonly string _stepType;
    private readonly Func<CoordinatorAgent, StepDefinition, string?, string?, CancellationToken, Task<PrimitiveResult>> _execute;

    public CoordinatorStepModule(
        string name,
        string stepType,
        Func<CoordinatorAgent, StepDefinition, string?, string?, CancellationToken, Task<PrimitiveResult>> execute)
    {
        _name = name;
        _stepType = stepType;
        _execute = execute;
    }

    public override string Name => _name;
    public override string StepType => _stepType;

    public override Task<PrimitiveResult> ExecuteAsync(
        CoordinatorAgent agent,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
        => _execute(agent, step, preRenderedPrompt, preRenderedSystem, ct);
}

public sealed class CoordinatorWorkflowEventModule : IEventModule
{
    public const string ModuleName = "coordinator_workflow_loop";
    public string Name => ModuleName;
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
        => envelope.Payload?.Is(StartWorkflowRequestEvent.Descriptor) == true;

    public async Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        if (envelope.Payload == null)
            return;

        if (host.Agent is not CoordinatorAgent coordinator)
        {
            host.Logger.LogWarning("[{Module}] Agent {AgentId} is not CoordinatorAgent",
                Name, host.AgentId);
            return;
        }

        var evt = envelope.Payload.Unpack<StartWorkflowRequestEvent>();
        await coordinator.HandleStartWorkflowRequest(evt);
    }
}

public sealed class CoordinatorParallelEventModule : IEventModule
{
    public const string ModuleName = "coordinator_parallel_events";
    public string Name => ModuleName;
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
        => envelope.Payload?.Is(StepCompletedEventProto.Descriptor) == true;

    public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        if (envelope.Payload == null)
            return Task.CompletedTask;

        if (host.Agent is not CoordinatorAgent coordinator)
        {
            host.Logger.LogWarning("[{Module}] Agent {AgentId} is not CoordinatorAgent",
                Name, host.AgentId);
            return Task.CompletedTask;
        }

        var evt = envelope.Payload.Unpack<StepCompletedEventProto>();
        return coordinator.HandleStepCompletedEvent(evt);
    }
}
