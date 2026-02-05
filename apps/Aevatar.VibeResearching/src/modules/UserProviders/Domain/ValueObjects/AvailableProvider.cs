namespace Aevatar.VibeResearching.UserProviders.ValueObjects;

/// <summary>
/// Represents a provider available for selection (user or platform).
/// Used by the aggregated available-providers API.
/// </summary>
public sealed record AvailableProvider(
    string Namespace,
    string Name,
    string ProviderType,
    string DefaultModel,
    string Source,
    bool IsDefault);
