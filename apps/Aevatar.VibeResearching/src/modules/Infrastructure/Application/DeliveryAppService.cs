using Volo.Abp.Application.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for research deliverables management.
/// Delegates to delivery-related repositories.
/// </summary>
public class DeliveryAppService : ApplicationService, IDeliveryAppService
{
    private readonly IBriefRepository _briefRepository;
    private readonly IDeliveryCenterRepository _deliveryRepository;
    private readonly IGoalsRepository _goalsRepository;
    private readonly IConclusionCardsRepository _conclusionCardsRepository;
    private readonly IEvidenceTableRepository _evidenceTableRepository;
    private readonly INextTasksRepository _nextTasksRepository;

    public DeliveryAppService(
        IBriefRepository briefRepository,
        IDeliveryCenterRepository deliveryRepository,
        IGoalsRepository goalsRepository,
        IConclusionCardsRepository conclusionCardsRepository,
        IEvidenceTableRepository evidenceTableRepository,
        INextTasksRepository nextTasksRepository)
    {
        _briefRepository = briefRepository;
        _deliveryRepository = deliveryRepository;
        _goalsRepository = goalsRepository;
        _conclusionCardsRepository = conclusionCardsRepository;
        _evidenceTableRepository = evidenceTableRepository;
        _nextTasksRepository = nextTasksRepository;
    }

    /// <inheritdoc/>
    public async Task<SraResearchBriefSnapshot> GetBriefAsync(string sessionId, CancellationToken ct = default)
    {
        return await _briefRepository.LoadAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<SraDeliveryCenterSnapshot> GetDeliveryAsync(string sessionId, CancellationToken ct = default)
    {
        return await _deliveryRepository.GetSnapshotAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<SraGoalsSnapshot> GetGoalsAsync(string sessionId, CancellationToken ct = default)
    {
        return await _goalsRepository.LoadAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<SraConclusionCardsSnapshot> GetConclusionCardsAsync(string sessionId, CancellationToken ct = default)
    {
        return await _conclusionCardsRepository.LoadAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<SraEvidenceTableSnapshot> GetEvidenceTableAsync(string sessionId, CancellationToken ct = default)
    {
        return await _evidenceTableRepository.LoadAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task<SraNextTasksSnapshot> GetNextTasksAsync(string sessionId, CancellationToken ct = default)
    {
        return await _nextTasksRepository.LoadAsync(sessionId, ct);
    }

    /// <inheritdoc/>
    public async Task UpdateGoalsAsync(string sessionId, SraGoalsSnapshot goals, CancellationToken ct = default)
    {
        await _goalsRepository.SaveAsync(sessionId, goals, ct);
    }
}
