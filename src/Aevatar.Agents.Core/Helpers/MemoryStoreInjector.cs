using System.Reflection;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Core.Helpers;

// ============================================================
//  MemoryStore automatic injector
//
//  WHY:
//  - MemoryEntry is a unified Protobuf contract, but store is a DI service.
//  - Agents can append memory entries without coupling to host application code.
//
//  HOW:
//  - If an agent declares a writable property named `MemoryStore`
//    of type `IMemoryStore`, we inject it from DI.
// ============================================================
public static class MemoryStoreInjector
{
    public static void InjectMemoryStore(IGAgent? agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var prop = agent.GetType().GetProperty(
            "MemoryStore",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (prop == null || !prop.CanWrite)
            return;

        if (prop.PropertyType != typeof(IMemoryStore))
            return;

        try
        {
            var store = serviceProvider.GetService(typeof(IMemoryStore)) as IMemoryStore;
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


