using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Runtime.Orleans.Context;

/// <summary>
/// Orleans-specific context accessor that bridges with Orleans.Runtime.RequestContext.
/// Provides IAgentContextAccessor interface for Orleans runtime.
/// </summary>
public class OrleansAgentContextAccessor : IAgentContextAccessor
{
    // Thread-local bridge instance to avoid creating multiple instances per request
    private static readonly AsyncLocal<OrleansAgentContextBridge?> BridgeInstance = new();

    /// <inheritdoc />
    public IAgentContext? Context
    {
        get => GetOrCreateBridge();
        set
        {
            // NOTE:
            // - The semantics here must be "replace" not "merge", otherwise scope restoration will leave old keys.
            // - value == null means clear current context.
            var bridge = GetOrCreateBridge();

            if (value == null)
            {
                bridge.Clear();
                return;
            }

            bridge.Clear();
            bridge.Import(value.GetAll());
        }
    }

    private static OrleansAgentContextBridge GetOrCreateBridge()
    {
        return BridgeInstance.Value ??= new OrleansAgentContextBridge();
    }

    /// <summary>
    /// Get or create current context.
    /// </summary>
    public IAgentContext GetOrCreate()
    {
        return GetOrCreateBridge();
    }
}

