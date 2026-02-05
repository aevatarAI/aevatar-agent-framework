namespace Aevatar.VibeResearching.Sessions.DTOs;

/// <summary>
/// Input DTO for creating a new research session.
/// </summary>
public sealed class CreateSessionDto
{
    /// <summary>
    /// Optional provider name for LLM selection.
    /// If null/empty, the system default provider is used.
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Optional initial agent-provider mappings to apply immediately after session creation.
    /// Keys are agent names (e.g. "planner"), values are provider namespaces (e.g. "user:abc123").
    /// When null or empty, agents use the default resolution chain.
    /// </summary>
    public Dictionary<string, string>? InitialAgentProviders { get; init; }
}
