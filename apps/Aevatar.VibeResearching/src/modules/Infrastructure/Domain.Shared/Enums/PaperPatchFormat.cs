namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Format for paper patch proposals.
/// Maps to PaperPatchFormat in proto contracts.
/// </summary>
public enum PaperPatchFormat
{
    /// <summary>Unspecified patch format.</summary>
    Unspecified = 0,

    /// <summary>Deterministic replace-span patch (line-based replacement).</summary>
    ReplaceSpan = 1,

    /// <summary>Standard unified diff format.</summary>
    UnifiedDiff = 2
}
