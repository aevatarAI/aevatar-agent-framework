using Aevatar.VibeResearching.UserProviders.ValueObjects;

namespace Aevatar.VibeResearching.UserProviders.Services;

/// <summary>
/// Resolves the effective LLM provider for a given context.
/// Implements the 3-layer resolution chain:
///   Layer 1: Session agent mapping
///   Layer 2: User default provider
///   Layer 3: Platform default provider
/// </summary>
public interface IProviderResolutionService
{
    /// <summary>
    /// Resolves the effective provider for a specific agent in a session.
    /// </summary>
    Task<ResolvedProviderResult> ResolveAsync(
        string sessionId,
        string agentName,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all available providers for a user (user + platform merged).
    /// </summary>
    Task<IReadOnlyList<AvailableProvider>> GetAvailableProvidersAsync(
        Guid userId,
        CancellationToken ct = default);
}
