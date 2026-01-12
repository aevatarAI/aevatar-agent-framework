using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Agents.Core.Helpers;

// ============================================================
//  MemoryGraphStore automatic injector
//
//  WHY:
//  - MemoryGraph is a portable Protobuf artifact (Layer 4.2), but the store is DI.
//  - Agents/tools may need to load graphs for explainability without coupling to host code.
//
//  HOW:
//  - If an agent declares a writable property named `MemoryGraphStore`
//    of type `IMemoryGraphStore`, we inject it from DI (best-effort).
// ============================================================
public static class MemoryGraphStoreInjector
{
    public static void InjectMemoryGraphStore(IGAgent? agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var prop = agent.GetType().GetProperty(
            "MemoryGraphStore",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (prop == null || !prop.CanWrite)
            return;

        if (prop.PropertyType != typeof(IMemoryGraphStore))
            return;

        try
        {
            var store = serviceProvider.GetService(typeof(IMemoryGraphStore)) as IMemoryGraphStore;
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


