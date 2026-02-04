namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Domain interface for materials service.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface IMaterialsService
{
    /// <summary>
    /// Loads materials snapshot for session.
    /// </summary>
    Task<MaterialsSnapshot> LoadAsync(string sessionId, string dagId, string query, CancellationToken ct);
}
