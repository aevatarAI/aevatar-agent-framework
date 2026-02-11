namespace SisyphusMaker.Dtos;

/// <summary>
/// Prompt definition: system instruction + user prompt template with {{placeholder}} variables.
/// </summary>
public sealed record InlinePrompt
{
    /// <summary>System-level instruction for the LLM.</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>User-level prompt template with {{placeholder}} variables.</summary>
    public string UserPromptTemplate { get; init; } = string.Empty;
}
