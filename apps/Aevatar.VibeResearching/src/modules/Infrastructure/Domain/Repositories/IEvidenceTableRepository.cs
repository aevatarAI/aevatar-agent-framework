using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for evidence table management.
/// Evidence table tracks all evidence artifacts and their relevance.
/// </summary>
public interface IEvidenceTableRepository
{
    /// <summary>
    /// Gets the file path for the evidence table snapshot.
    /// </summary>
    string GetEvidencePath(string sessionId);

    /// <summary>
    /// Loads the evidence table snapshot.
    /// </summary>
    Task<SraEvidenceTableSnapshot> LoadAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Saves the evidence table snapshot.
    /// </summary>
    Task<SraEvidenceTableSnapshot> SaveAsync(string sessionId, SraEvidenceTableSnapshot snapshot, CancellationToken ct);
}
