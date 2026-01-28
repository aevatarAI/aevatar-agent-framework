using Microsoft.Extensions.Logging;
using Aevatar.VibeResearching.Agents.Contracts.Collab;
using Aevatar.VibeResearching.Agents.Contracts.Sessions;

namespace Aevatar.VibeResearching.Infrastructure;

/// <summary>
/// Repository interface for delivery center operations including conclusions, evidence, and tasks.
/// </summary>
public interface IDeliveryCenterRepository
{
    /// <summary>
    /// Gets the file paths for all delivery center files.
    /// </summary>
    (string Conclusions, string Evidence, string Tasks, string DeliverySnapshot) GetPaths(string sessionId);

    /// <summary>
    /// Loads the delivery center snapshot.
    /// </summary>
    Task<SraDeliveryCenterSnapshot> LoadDeliverySnapshotAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Loads the conclusions snapshot.
    /// </summary>
    Task<SraConclusionCardsSnapshot> LoadConclusionsAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Loads the evidence table snapshot.
    /// </summary>
    Task<SraEvidenceTableSnapshot> LoadEvidenceAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Loads the tasks snapshot.
    /// </summary>
    Task<SraNextTasksSnapshot> LoadTasksAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Loads all delivery artifacts in a single call.
    /// </summary>
    Task<(SraDeliveryCenterSnapshot Delivery, SraConclusionCardsSnapshot Conclusions, SraEvidenceTableSnapshot Evidence, SraNextTasksSnapshot Tasks)>
        LoadAllAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Saves the delivery center snapshot.
    /// </summary>
    Task<SraDeliveryCenterSnapshot> SaveDeliverySnapshotAsync(string sessionId, SraDeliveryCenterSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Saves the conclusions snapshot.
    /// </summary>
    Task<SraConclusionCardsSnapshot> SaveConclusionsAsync(string sessionId, SraConclusionCardsSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Saves the evidence table snapshot.
    /// </summary>
    Task<SraEvidenceTableSnapshot> SaveEvidenceAsync(string sessionId, SraEvidenceTableSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Saves the tasks snapshot.
    /// </summary>
    Task<SraNextTasksSnapshot> SaveTasksAsync(string sessionId, SraNextTasksSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Gets a bounded snapshot formatted for UI display.
    /// </summary>
    Task<object> GetSnapshotForUiAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Gets the delivery center snapshot (alias for LoadDeliverySnapshotAsync).
    /// </summary>
    Task<SraDeliveryCenterSnapshot> GetSnapshotAsync(string sessionId, CancellationToken ct);
}
