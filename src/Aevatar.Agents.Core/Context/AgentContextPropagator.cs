using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Core.Runtime;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Automatically propagates context through event publishing.
/// Integrates with IEventPublisher to inject context into EventEnvelope.
/// </summary>
public class AgentContextPropagator
{
    private readonly IAgentContextAccessor _contextAccessor;
    private readonly AgentContextPropagationOptions _options;

    /// <summary>
    /// Creates a new context propagator with default options.
    /// </summary>
    public AgentContextPropagator(IAgentContextAccessor contextAccessor)
        : this(contextAccessor, AgentContextPropagationOptions.Default)
    {
    }

    /// <summary>
    /// Creates a new context propagator with custom options.
    /// </summary>
    public AgentContextPropagator(
        IAgentContextAccessor contextAccessor,
        AgentContextPropagationOptions options)
    {
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _options = options ?? AgentContextPropagationOptions.Default;
    }

    /// <summary>
    /// Inject current context into EventEnvelope before publishing.
    /// </summary>
    public void InjectContext(EventEnvelope envelope)
    {
        // 1) Business/context metadata (existing behavior)
        var context = _contextAccessor.Context;
        if (context != null)
        {
        var metadata = AgentContextSerializer.Serialize(context, _options);
        foreach (var (key, value) in metadata)
        {
            envelope.ContextMetadata[key] = value;
        }
        }

        // 2) Interruptible run metadata (opt-in by ambient RunContextScope)
        var run = RunContextScope.Value;
        if (run == null) return;

        // Do not overwrite if caller explicitly set keys.
        envelope.ContextMetadata.TryAdd(
            RunContextScope.MetadataKeys.RunId,
            new ContextValue { StringValue = run.RunId });
        envelope.ContextMetadata.TryAdd(
            RunContextScope.MetadataKeys.RunScopeId,
            new ContextValue { StringValue = run.ScopeId });
    }

    /// <summary>
    /// Extract context from EventEnvelope and return a new context instance.
    /// Does not modify the current ambient context - use AgentContextScope for that.
    /// </summary>
    public IAgentContext? ExtractContext(EventEnvelope envelope)
    {
        if (envelope.ContextMetadata.Count == 0)
            return null;

        var context = new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(envelope.ContextMetadata, context, _options);
        return context;
    }

    /// <summary>
    /// Extract context from EventEnvelope and apply to current ambient context.
    /// Warning: This modifies the current ambient context. Use AgentContextScope for safe scoped access.
    /// </summary>
    public void ApplyContext(EventEnvelope envelope)
    {
        if (envelope.ContextMetadata.Count == 0) return;

        var context = _contextAccessor.Context ?? new AsyncLocalAgentContext();
        AgentContextSerializer.Deserialize(envelope.ContextMetadata, context, _options);
        _contextAccessor.Context = context;
    }
}

