namespace Aevatar.Agents.Abstractions.Context;

/// <summary>
/// Provides access to current agent context.
/// Similar to IHttpContextAccessor in ASP.NET Core.
/// </summary>
public interface IAgentContextAccessor
{
    /// <summary>
    /// Current agent context. May be null if not in an agent execution scope.
    /// </summary>
    IAgentContext? Context { get; set; }
}

