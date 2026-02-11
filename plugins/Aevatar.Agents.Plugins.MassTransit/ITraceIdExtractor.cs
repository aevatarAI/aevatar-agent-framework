using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// Pluggable trace ID extractor for MassTransit message dispatching.
/// 
/// Implementations can extract application-specific correlation/trace IDs
/// from event envelopes for logging and distributed tracing.
/// 
/// Register via DI to enable:
///   services.AddSingleton&lt;ITraceIdExtractor, MyAppTraceIdExtractor&gt;();
/// </summary>
public interface ITraceIdExtractor
{
    /// <summary>
    /// Try to extract a trace ID from the given event envelope.
    /// </summary>
    /// <param name="envelope">The parsed event envelope.</param>
    /// <param name="traceId">The extracted trace ID, or null if not applicable.</param>
    /// <returns>True if a trace ID was successfully extracted.</returns>
    bool TryExtract(EventEnvelope envelope, out string? traceId);
}
