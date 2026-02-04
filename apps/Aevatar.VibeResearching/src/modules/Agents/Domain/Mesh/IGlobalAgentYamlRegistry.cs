namespace Aevatar.VibeResearching.Agents.Mesh;

/// <summary>
/// Domain interface for global agent YAML registry.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IGlobalAgentYamlRegistry
{
    /// <summary>
    /// Gets all known role names.
    /// </summary>
    IReadOnlySet<string> GetKnownRoles();

    /// <summary>
    /// Checks if a role exists.
    /// </summary>
    bool HasRole(string? role);

    /// <summary>
    /// Tries to get the provider name for a role from its YAML config.
    /// Returns null if no provider is configured or role doesn't exist.
    /// </summary>
    string? TryGetProvider(string? role);
}
