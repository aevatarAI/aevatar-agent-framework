using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for research brief operations.
/// The research brief defines the scope, success criteria, terms, and milestones for a session.
/// </summary>
public interface IBriefRepository
{
    /// <summary>
    /// Gets the file path for the research brief.
    /// </summary>
    string GetBriefPath(string sessionId);

    /// <summary>
    /// Loads the research brief snapshot.
    /// </summary>
    Task<SraResearchBriefSnapshot> LoadAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Saves the research brief snapshot.
    /// </summary>
    Task<SraResearchBriefSnapshot> SaveAsync(string sessionId, SraResearchBriefSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Updates only the milestones in the brief, preserving all other fields.
    /// Used for dynamic milestone modification (e.g., direction change).
    /// </summary>
    Task<SraResearchBriefSnapshot> UpdateMilestonesAsync(
        string sessionId,
        List<SraResearchMilestone> newMilestones,
        CancellationToken ct);
}
