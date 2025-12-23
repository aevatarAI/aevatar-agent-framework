using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Scoped context that restores previous context on dispose.
/// Use this in HandleEventAsync to prevent context leakage between events.
/// </summary>
/// <example>
/// <code>
/// using (new AgentContextScope(accessor, newContext))
/// {
///     // newContext is active here
///     await ProcessEvent(envelope);
/// }
/// // Previous context is restored here
/// </code>
/// </example>
public readonly struct AgentContextScope : IDisposable
{
    private readonly IAgentContextAccessor? _accessor;
    private readonly IAgentContext? _previous;
    private readonly IReadOnlyDictionary<string, object?>? _previousSnapshot;
    private readonly bool _restoreByReference;
    private readonly bool _hasScope;

    /// <summary>
    /// Creates a new context scope that sets the new context and restores the previous on dispose.
    /// </summary>
    /// <param name="accessor">The context accessor to modify</param>
    /// <param name="newContext">The new context to set</param>
    public AgentContextScope(IAgentContextAccessor accessor, IAgentContext? newContext)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _previous = accessor.Context;
        _previousSnapshot = _previous?.GetAll();
        _accessor.Context = newContext;
        _restoreByReference = ReferenceEquals(_accessor.Context, newContext);
        _hasScope = true;
    }

    /// <summary>
    /// Private constructor for empty scope (default struct value).
    /// </summary>
    private AgentContextScope(bool hasScope)
    {
        _accessor = null;
        _previous = null;
        _previousSnapshot = null;
        _restoreByReference = false;
        _hasScope = hasScope;
    }

    /// <summary>
    /// Creates an empty scope that does nothing on dispose.
    /// Useful for conditional scoping.
    /// </summary>
    public static AgentContextScope Empty => new(false);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_hasScope && _accessor != null)
        {
            // ============================================================
            //  Restore semantics
            //
            //  - AsyncLocal accessor: restore previous context instance by reference
            //  - Orleans accessor(bridge): accessor.Context won't equal newContext, must restore by snapshot
            // ============================================================
            if (_restoreByReference)
            {
                _accessor.Context = _previous;
                return;
            }

            if (_previous == null)
            {
                _accessor.Context = null;
                return;
            }

            // Snapshot restore: apply previous entries as a whole
            var snapshotContext = new AsyncLocalAgentContext();
            if (_previousSnapshot != null)
            {
                snapshotContext.Import(_previousSnapshot);
            }
            _accessor.Context = snapshotContext;
        }
    }
}

/// <summary>
/// Extension methods for creating agent context scopes.
/// </summary>
public static class AgentContextScopeExtensions
{
    /// <summary>
    /// Creates a scoped context from an EventEnvelope.
    /// Extracts context metadata and sets it as the current context for the scope.
    /// </summary>
    /// <param name="accessor">The context accessor</param>
    /// <param name="envelope">The EventEnvelope containing context metadata</param>
    /// <returns>A disposable scope that restores the previous context</returns>
    public static AgentContextScope CreateScope(
        this IAgentContextAccessor accessor,
        EventEnvelope envelope)
    {
        if (envelope.ContextMetadata.Count == 0)
            return AgentContextScope.Empty;

        var context = new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(envelope.ContextMetadata, context);
        return new AgentContextScope(accessor, context);
    }

    /// <summary>
    /// Creates a scoped context with a new context instance.
    /// </summary>
    /// <param name="accessor">The context accessor</param>
    /// <param name="context">The context to set for the scope</param>
    /// <returns>A disposable scope that restores the previous context</returns>
    public static AgentContextScope CreateScope(
        this IAgentContextAccessor accessor,
        IAgentContext? context)
    {
        if (context == null)
            return AgentContextScope.Empty;

        return new AgentContextScope(accessor, context);
    }
}

