using Aevatar.Agents.AI.Abstractions.Configuration;

namespace Aevatar.VibeResearching.UserProviders.ValueObjects;

/// <summary>
/// Result of the 3-layer provider resolution.
/// Contains the ready-to-use LLMProviderConfig for factory creation.
/// </summary>
public sealed record ResolvedProviderResult(
    string ProviderNamespace,
    string ProviderType,
    string Model,
    string? Endpoint,
    string ResolutionSource,
    LLMProviderConfig ProviderConfig);
