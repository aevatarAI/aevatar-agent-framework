namespace SisyphusMaker.Dtos;

/// <summary>
/// Verification request for POST /api/verify.
/// Caller provides prompt (system + user template), variables, and optional config.
/// </summary>
public sealed record VerifyRequest
{
    /// <summary>Prompt with system instruction and user template.</summary>
    public required InlinePrompt Prompt { get; init; }

    /// <summary>Flat string-to-string variable map for template rendering.</summary>
    public Dictionary<string, string>? Variables { get; init; }

    /// <summary>Optional per-request engine configuration overrides.</summary>
    public MakerConfig? Config { get; init; }
}
