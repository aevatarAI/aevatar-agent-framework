namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Result of scanning a workspace for knowledge artifacts.
/// </summary>
public sealed record WorkspaceScanResult
{
    public required string SessionId { get; init; }
    public int FactsProposedCount { get; init; }

    public List<string> FactsProposedRecent { get; init; } = new();
}
