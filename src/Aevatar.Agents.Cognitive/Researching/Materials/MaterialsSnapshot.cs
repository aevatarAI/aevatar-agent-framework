namespace Aevatar.Agents.Cognitive.Researching.Materials;

// ============================================================
//  MaterialsSnapshot
//
//  Bounded, deterministic "materials" projection used by the
//  researching workflow. Kept as a plain C# type (local-only).
// ============================================================
public sealed class MaterialsSnapshot
{
    public string SessionId { get; init; } = "";
    public string DagId { get; init; } = "";
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<MaterialFile> Facts { get; init; } = new();
    public string RenderedContext { get; init; } = "";
}

