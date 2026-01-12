namespace Aevatar.AxiomReasoning.EventStreaming.Events;

public abstract record AxiomEvent
{
    public string SessionId { get; init; } = "";
    public string Type => GetType().Name;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}