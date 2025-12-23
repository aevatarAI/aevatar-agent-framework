using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Default IAgentContextAccessor using AsyncLocal for async flow preservation.
/// Similar to IHttpContextAccessor in ASP.NET Core.
/// </summary>
public class AsyncLocalAgentContextAccessor : IAgentContextAccessor
{
    private static readonly AsyncLocal<IAgentContext?> Current = new();

    /// <inheritdoc />
    public IAgentContext? Context
    {
        get => Current.Value;
        set => Current.Value = value;
    }

    /// <summary>
    /// Get or create current context.
    /// </summary>
    public IAgentContext GetOrCreate()
    {
        return Context ??= new AsyncLocalAgentContext();
    }
}

/// <summary>
/// Extension methods for IAgentContextAccessor.
/// </summary>
public static class AgentContextAccessorExtensions
{
    /// <summary>
    /// Get existing context or create a new one.
    /// </summary>
    public static IAgentContext GetOrCreate(this IAgentContextAccessor accessor)
    {
        return accessor.Context ??= new AsyncLocalAgentContext();
    }
}

