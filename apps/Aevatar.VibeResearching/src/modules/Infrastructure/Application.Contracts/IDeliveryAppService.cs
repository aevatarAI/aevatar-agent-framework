using Volo.Abp.Application.Services;
using Aevatar.VibeResearching.Agents.Contracts.Collab;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Application service for research deliverables management.
/// </summary>
public interface IDeliveryAppService : IApplicationService
{
    /// <summary>
    /// Gets the research brief snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Research brief snapshot</returns>
    Task<SraResearchBriefSnapshot> GetBriefAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the delivery center snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Delivery center snapshot</returns>
    Task<SraDeliveryCenterSnapshot> GetDeliveryAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the goals snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Goals snapshot</returns>
    Task<SraGoalsSnapshot> GetGoalsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the conclusion cards snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Conclusion cards snapshot</returns>
    Task<SraConclusionCardsSnapshot> GetConclusionCardsAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the evidence table snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Evidence table snapshot</returns>
    Task<SraEvidenceTableSnapshot> GetEvidenceTableAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Gets the next tasks snapshot for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Next tasks snapshot</returns>
    Task<SraNextTasksSnapshot> GetNextTasksAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Updates goals for a session.
    /// </summary>
    /// <param name="sessionId">Session identifier</param>
    /// <param name="goals">Updated goals snapshot</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateGoalsAsync(string sessionId, SraGoalsSnapshot goals, CancellationToken ct = default);
}
