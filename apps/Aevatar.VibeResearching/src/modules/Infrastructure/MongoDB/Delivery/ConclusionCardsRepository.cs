using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.Delivery;

public sealed class ConclusionCardsRepository : IConclusionCardsRepository
{
    private readonly DeliveryCenterRepository _center;

    public ConclusionCardsRepository(DeliveryCenterRepository center)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
    }

    public string GetConclusionsPath(string sessionId)
        => _center.GetPaths(sessionId).Conclusions;

    public Task<SraConclusionCardsSnapshot> LoadAsync(string sessionId, CancellationToken ct)
        => _center.LoadConclusionsAsync(sessionId, ct);

    public Task<SraConclusionCardsSnapshot> SaveAsync(string sessionId, SraConclusionCardsSnapshot snapshot, CancellationToken ct)
        => _center.SaveConclusionsAsync(sessionId, snapshot, ct);
}
