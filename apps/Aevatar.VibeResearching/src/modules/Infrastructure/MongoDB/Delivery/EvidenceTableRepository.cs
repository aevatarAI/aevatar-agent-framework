using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB.Delivery;

public sealed class EvidenceTableRepository : IEvidenceTableRepository
{
    private readonly DeliveryCenterRepository _center;

    public EvidenceTableRepository(DeliveryCenterRepository center)
    {
        _center = center ?? throw new ArgumentNullException(nameof(center));
    }

    public string GetEvidencePath(string sessionId)
        => _center.GetPaths(sessionId).Evidence;

    public Task<SraEvidenceTableSnapshot> LoadAsync(string sessionId, CancellationToken ct)
        => _center.LoadEvidenceAsync(sessionId, ct);

    public Task<SraEvidenceTableSnapshot> SaveAsync(string sessionId, SraEvidenceTableSnapshot snapshot, CancellationToken ct)
        => _center.SaveEvidenceAsync(sessionId, snapshot, ct);
}
