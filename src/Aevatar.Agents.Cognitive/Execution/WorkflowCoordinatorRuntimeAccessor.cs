using System.Runtime.CompilerServices;
using System.Threading;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

internal static class WorkflowCoordinatorRuntimeAccessor
{
    private static readonly ConditionalWeakTable<RoleAIGAgent, WorkflowCoordinatorRuntime> Runtimes = new();

    public static WorkflowCoordinatorRuntime GetOrCreate(
        RoleAIGAgent agent,
        ILogger logger,
        Func<IMessage, EventDirection, CancellationToken, Task> publish)
    {
        if (Runtimes.TryGetValue(agent, out var runtime))
            return runtime;

        runtime = new WorkflowCoordinatorRuntime(agent, logger, publish);
        Runtimes.Add(agent, runtime);
        return runtime;
    }
}
