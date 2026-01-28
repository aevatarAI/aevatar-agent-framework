using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.Delivery;

public sealed class NextTasksRepository : INextTasksRepository
{
    private readonly DeliveryCenterRepository _center;

    public NextTasksRepository(DeliveryCenterRepository center)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
    }

    public string GetTasksPath(string sessionId)
        => _center.GetPaths(sessionId).Tasks;

    public Task<SraNextTasksSnapshot> LoadAsync(string sessionId, CancellationToken ct)
        => _center.LoadTasksAsync(sessionId, ct);

    public Task<SraNextTasksSnapshot> SaveAsync(string sessionId, SraNextTasksSnapshot snapshot, CancellationToken ct)
        => _center.SaveTasksAsync(sessionId, snapshot, ct);
}
