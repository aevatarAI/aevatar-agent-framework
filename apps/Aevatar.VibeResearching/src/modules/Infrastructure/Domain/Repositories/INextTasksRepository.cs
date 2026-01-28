using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for next tasks management.
/// Next tasks represent actionable items and research directions.
/// </summary>
public interface INextTasksRepository
{
    /// <summary>
    /// Gets the file path for the next tasks snapshot.
    /// </summary>
    string GetTasksPath(string sessionId);

    /// <summary>
    /// Loads the next tasks snapshot.
    /// </summary>
    Task<SraNextTasksSnapshot> LoadAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Saves the next tasks snapshot.
    /// </summary>
    Task<SraNextTasksSnapshot> SaveAsync(string sessionId, SraNextTasksSnapshot snapshot, CancellationToken ct);
}
