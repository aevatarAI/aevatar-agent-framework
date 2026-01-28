namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Target file for paper patch proposals.
/// Maps to PaperTargetFile in proto contracts.
/// </summary>
public enum PaperTargetFile
{
    /// <summary>Unspecified target file.</summary>
    Unspecified = 0,

    /// <summary>Target is the paper outline.</summary>
    Outline = 1,

    /// <summary>Target is the paper draft.</summary>
    Draft = 2
}
