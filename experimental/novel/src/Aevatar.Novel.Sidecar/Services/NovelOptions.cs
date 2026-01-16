namespace Aevatar.Novel.Sidecar.Services;

// ============================================================
//  NovelOptions
//  - Local-only configuration (not a cross-boundary message).
//  - SSOT directory is configured here for the sidecar runtime.
// ============================================================

public sealed class NovelOptions
{
    public const string SectionName = "Novel";

    /// <summary>
    /// Project root directory (SSOT) that contains chapter .txt and artifact .md files.
    /// </summary>
    public string? ProjectRoot { get; init; }
}


