namespace Aevatar.VibeResearching.Agents.Mesh.Services;

/// <summary>
/// Empty implementation of IGlobalAgentYamlRegistry.
/// Returns no known roles until YAML agent definitions are loaded.
/// </summary>
public sealed class EmptyGlobalAgentYamlRegistry : IGlobalAgentYamlRegistry
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    public IReadOnlySet<string> GetKnownRoles() => Empty;

    public bool HasRole(string? role) => false;

    public string? TryGetProvider(string? role) => null;
}
