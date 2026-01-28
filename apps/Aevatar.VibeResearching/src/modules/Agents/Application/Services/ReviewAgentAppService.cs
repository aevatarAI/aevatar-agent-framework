using Aevatar.VibeResearching.Agents.Application.Contracts.Services;
using Aevatar.VibeResearching.Agents.ReviewAgent;
using Volo.Abp.Application.Services;

namespace Aevatar.VibeResearching.Agents.Application.Services;

/// <summary>
/// Application service for Review Agent operations.
/// Provides automated knowledge graph review and cleanup functionality.
/// </summary>
public class ReviewAgentAppService : ApplicationService, IReviewAgentAppService
{
    private readonly IReviewAgentService _reviewAgentService;
    private readonly IReviewAgentStorage _reviewAgentStorage;

    public ReviewAgentAppService(
        IReviewAgentService reviewAgentService,
        IReviewAgentStorage reviewAgentStorage)
    {
        _reviewAgentService = reviewAgentService;
        _reviewAgentStorage = reviewAgentStorage;
    }

    /// <inheritdoc/>
    public async Task<object> GetStatusAsync(CancellationToken ct = default)
    {
        return await _reviewAgentService.GetStatusAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetSettingsAsync(CancellationToken ct = default)
    {
        return await _reviewAgentService.GetSettingsAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<object> UpdateSettingsAsync(object settings, CancellationToken ct = default)
    {
        return await _reviewAgentService.UpdateSettingsAsync(settings, ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetIterationsAsync(int limit = 10, int offset = 0, CancellationToken ct = default)
    {
        return await _reviewAgentStorage.GetIterationsAsync(limit, offset, ct);
    }

    /// <inheritdoc/>
    public async Task<object?> GetIterationAsync(string iterationId, CancellationToken ct = default)
    {
        return await _reviewAgentStorage.GetIterationAsync(iterationId, ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetCurrentIterationEntriesAsync(CancellationToken ct = default)
    {
        return await _reviewAgentStorage.GetCurrentIterationEntriesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<object> GetReviewGraphAsync(CancellationToken ct = default)
    {
        return await _reviewAgentService.GetReviewGraphAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<object> TriggerReviewAsync(CancellationToken ct = default)
    {
        return await _reviewAgentService.TriggerReviewAsync(ct);
    }
}
