namespace Aevatar.Agents.Cognitive.Researching.Materials;

// ============================================================
//  MaterialFile
//
//  A small, UI-friendly "material" projection used to inject
//  bounded context into LLM prompts.
// ============================================================
public sealed class MaterialFile
{
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string Content { get; init; } = "";
}

