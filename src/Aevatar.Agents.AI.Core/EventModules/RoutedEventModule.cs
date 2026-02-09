using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.AI.Core;

public sealed class RoutedEventModule : IEventModule
{
    private readonly IEventModule _inner;
    private readonly EventRoute[] _routes;
    private readonly IEventRouteEvaluator _evaluator;

    public RoutedEventModule(IEventModule inner, EventRoute[] routes, IEventRouteEvaluator evaluator)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _routes = routes ?? Array.Empty<EventRoute>();
        _evaluator = evaluator ?? new DefaultEventRouteEvaluator();
    }

    public string Name => _inner.Name;
    public int Priority => _inner.Priority;

    public bool CanHandle(EventEnvelope envelope)
    {
        if (!_inner.CanHandle(envelope))
            return false;

        if (_routes.Length == 0)
            return true;

        return _routes.Any(r =>
            string.Equals(r.TargetModule, Name, StringComparison.OrdinalIgnoreCase) &&
            r.Matches(envelope, _evaluator));
    }

    public Task HandleAsync(EventEnvelope envelope, IEventModuleHost host, CancellationToken ct)
        => _inner.HandleAsync(envelope, host, ct);
}
