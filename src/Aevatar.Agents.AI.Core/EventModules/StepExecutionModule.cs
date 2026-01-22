using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.AI.Core;

public sealed class StepExecutionModule : IEventModule
{
    private readonly IStepExecutionHandler _handler;

    public StepExecutionModule(IStepExecutionHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public string Name => "step_execution_handler";
    public int Priority => 0;

    public bool CanHandle(EventEnvelope envelope)
        => _handler.CanHandle(envelope);

    public async Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
    {
        var result = await _handler.HandleAsync(envelope, host.Agent, ct);
        if (result?.Response == null)
            return;

        await host.PublishAsync(result.Response, result.Direction, ct);
    }
}
