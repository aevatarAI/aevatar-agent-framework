namespace ScientificResearchAssistant.Api.Materials;

// ============================================================
//  MaterialsOptions
//
//  Purpose:
//  - Configure where the system loads "axioms" and "references" from.
//
//  Notes:
//  - For MVP we load from local filesystem (privacy-friendly).
//  - Paths are resolved relative to the system root:
//      scientific-research-assistant/
// ============================================================

public sealed class MaterialsOptions
{
    public const string SectionName = "Materials";

    /// <summary>
    /// Root directory under the system folder.
    /// Default: scientific-research-assistant/materials
    /// </summary>
    public string RootDir { get; init; } = "materials";

    public int MaxFiles { get; init; } = 200;

    /// <summary>
    /// Max chars read per file (hard cap; avoids turning Memory into a blob store).
    /// </summary>
    public int MaxFileChars { get; init; } = 200_000;

    /// <summary>
    /// Max chars injected into LLM system prompt as "materials context".
    /// </summary>
    public int MaxContextChars { get; init; } = 18_000;

    public int MaxPerDocChars { get; init; } = 6_000;

    // ------------------------------------------------------------
    //  Optional: allow the system to write "verified notes" back to materials/
    // ------------------------------------------------------------
    public bool AllowWrite { get; init; } = false;

    /// <summary>
    /// Subdirectory (under RootDir) where server-written notes are placed.
    /// </summary>
    public string WriteDir { get; init; } = "notes";

    public int MaxWriteChars { get; init; } = 200_000;
}


