namespace Aevatar.AxiomReasoning.EventStreaming.Events;

public record ErrorEvent : AxiomEvent
{
    public string Message { get; init; } = "";
    public string? StackTrace { get; init; }
}