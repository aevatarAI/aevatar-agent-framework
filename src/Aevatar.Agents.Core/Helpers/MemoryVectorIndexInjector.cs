using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Memory;

namespace Aevatar.Agents.Core.Helpers;

// ============================================================
//  MemoryVectorIndex automatic injector
//
//  WHY:
//  - Enable agents/tools to do persistent semantic retrieval via DI boundary.
//
//  HOW:
//  - If an agent declares a writable property named `MemoryVectorIndex`
//    of type `IMemoryVectorIndex`, we inject it from DI.
// ============================================================
public static class MemoryVectorIndexInjector
{
    public static void InjectMemoryVectorIndex(IGAgent? agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var prop = agent.GetType().GetProperty(
            "MemoryVectorIndex",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (prop == null || !prop.CanWrite)
            return;

        if (prop.PropertyType != typeof(IMemoryVectorIndex))
            return;

        try
        {
            var index = serviceProvider.GetService(typeof(IMemoryVectorIndex)) as IMemoryVectorIndex;
            if (index == null)
                return;

            prop.SetValue(agent, index);
        }
        catch
        {
            // Ignore injection failures (best-effort).
        }
    }
}


