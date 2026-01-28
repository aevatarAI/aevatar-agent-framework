using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for research goals management.
/// Goals represent high-level objectives that guide the research process.
/// </summary>
public interface IGoalsRepository
{
    /// <summary>
    /// Gets the file path for the goals snapshot.
    /// </summary>
    string GetGoalsPath(string sessionId);

    /// <summary>
    /// Loads the goals snapshot.
    /// </summary>
    Task<SraGoalsSnapshot> LoadAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Saves the goals snapshot.
    /// </summary>
    Task<SraGoalsSnapshot> SaveAsync(string sessionId, SraGoalsSnapshot snapshot, CancellationToken ct);
}
