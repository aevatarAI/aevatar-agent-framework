namespace Aevatar.AxiomReasoning.EventStreaming.Events;

public record ResultEvent : AxiomEvent
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int TotalLlmCalls { get; init; }
    public long TotalTokens { get; init; }
}