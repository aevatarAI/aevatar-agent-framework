namespace VibeResearching.Api.Materials;

// ============================================================
//  MaterialsOptions
//
//  Purpose:
//  - Configure how DAG knowledge nodes are projected into "materials context".
//
//  Notes:
//  - Facts are DAG knowledge nodes (no file-based facts/sources directories).
// ============================================================

public sealed class MaterialsOptions
{
    public const string SectionName = "Materials";

    /// <summary>
    /// Max DAG nodes to consider as materials (bounded).
    /// </summary>
    public int MaxFiles { get; init; } = 200;

    /// <summary>
    /// Max chars read per DAG node content (hard cap).
    /// </summary>
    public int MaxFileChars { get; init; } = 200_000;

    /// <summary>
    /// Max chars injected into LLM system prompt as "materials context".
    /// </summary>
    public int MaxContextChars { get; init; } = 18_000;

    public int MaxPerDocChars { get; init; } = 6_000;

    // ------------------------------------------------------------
    //  Optional: allow the system to write facts into DAG
    // ------------------------------------------------------------
    public bool AllowWrite { get; init; } = false;

    public int MaxWriteChars { get; init; } = 200_000;
}


