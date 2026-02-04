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
}
