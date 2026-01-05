namespace Aevatar.Trade.Infrastructure.DecisionEngines;

/// <summary>
/// Direct LLM decision engine (Coordinator-local).
/// Implemented as a thin wrapper around a chat delegate.
/// </summary>
public sealed class DirectLlmDecisionEngine : ITradingDecisionEngine
{
    private readonly Func<string, CancellationToken, Task<string>> _chatAsync;

    public DirectLlmDecisionEngine(Func<string, CancellationToken, Task<string>> chatAsync)
    {
        _chatAsync = chatAsync ?? throw new ArgumentNullException(nameof(chatAsync));
    }

    public string Name => "Direct";

    public Task<string> GetDecisionJsonAsync(string prompt, string? cycleId = null, CancellationToken ct = default)
        => _chatAsync(prompt, ct);
}


