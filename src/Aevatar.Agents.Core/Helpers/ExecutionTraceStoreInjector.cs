using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;

namespace Aevatar.Agents.Core.Helpers;

// ============================================================
//  ExecutionTraceStore automatic injector
//
//  WHY:
//  - ExecutionTrace is a unified Protobuf contract, but store is a DI service.
//  - Agents may want to export traces without coupling to host application code.
//
//  HOW:
//  - If an agent declares a writable property named `ExecutionTraceStore`
//    of type `IExecutionTraceStore`, we inject it from DI.
// ============================================================
public static class ExecutionTraceStoreInjector
{
    public static void InjectExecutionTraceStore(IGAgent? agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var agentType = agent.GetType();

        var prop = agentType.GetProperty(
            "ExecutionTraceStore",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (prop == null || !prop.CanWrite)
            return;

        if (prop.PropertyType != typeof(IExecutionTraceStore))
            return;

        try
        {
            var store = serviceProvider.GetService(typeof(IExecutionTraceStore)) as IExecutionTraceStore;
            if (store == null)
                return;

            prop.SetValue(agent, store);
        }
        catch
        {
            // Ignore injection failures (best-effort).
        }
    }
}


