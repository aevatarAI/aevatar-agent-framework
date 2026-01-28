namespace Aevatar.VibeResearching.Knowledge;

/// <summary>
/// Input DTO for recording programmatic fact verification results.
/// </summary>
public sealed class FactVerificationDto
{
    /// <summary>
    /// Verifier agent identifier (required).
    /// </summary>
    public string? VerifierId { get; init; }

    /// <summary>
    /// Tool used for verification (e.g., "python_exec").
    /// </summary>
    public string? Tool { get; init; }

    /// <summary>
    /// Verification result: true = passed, false = failed (required).
    /// </summary>
    public bool? Result { get; init; }

    /// <summary>
    /// Artifact paths produced during verification.
    /// </summary>
    public List<string>? ArtifactPaths { get; init; }

    /// <summary>
    /// Small excerpt from verification logs for quick review.
    /// </summary>
    public string? LogExcerpt { get; init; }
}
