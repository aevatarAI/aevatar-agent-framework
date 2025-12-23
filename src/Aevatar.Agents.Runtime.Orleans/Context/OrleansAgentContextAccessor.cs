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
            // - 这里的语义必须是“替换”(replace)而不是“合并”(merge)，否则 scope 恢复会残留旧 key。
            // - value == null 代表清空当前上下文。
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

