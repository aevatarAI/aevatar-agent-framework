using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local runtime <see cref="IMessageStreamProvider"/> backed by <see cref="LocalMessageStreamRegistry"/>.
/// </summary>
public sealed class LocalMessageStreamProvider : IMessageStreamProvider
{
    private readonly LocalMessageStreamRegistry _registry;

    public LocalMessageStreamProvider(LocalMessageStreamRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public IMessageStream GetStream(string agentId, string? category = null)
        => _registry.GetOrCreateStream(agentId);
}

