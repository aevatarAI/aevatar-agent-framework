namespace Aevatar.VibeResearching.Sessions.ValueObjects;

/// <summary>
/// Snapshot of agent provider configuration.
/// Maps agent role names to provider names.
/// </summary>
public sealed record AgentProvidersSnapshot(
    int Version,
    string UpdatedAt,
    Dictionary<string, string> Map);
