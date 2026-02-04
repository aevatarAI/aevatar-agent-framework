using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for conclusion cards management.
/// Conclusion cards represent key findings and claims from research.
/// </summary>
public interface IConclusionCardsRepository
{
    /// <summary>
    /// Gets the file path for the conclusions snapshot.
    /// </summary>
    string GetConclusionsPath(string sessionId);

    /// <summary>
    /// Loads the conclusions snapshot.
    /// </summary>
    Task<SraConclusionCardsSnapshot> LoadAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Saves the conclusions snapshot.
    /// </summary>
    Task<SraConclusionCardsSnapshot> SaveAsync(string sessionId, SraConclusionCardsSnapshot snapshot, CancellationToken ct);
}
