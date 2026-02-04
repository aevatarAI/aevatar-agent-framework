namespace Aevatar.VibeResearching.Sessions.Services;

/// <summary>
/// Domain service interface for session UI trace recording.
/// Implementation lives in infrastructure layer.
/// </summary>
public interface ISessionUiTraceService
{
    /// <summary>
    /// Attaches event recorder to session for UI tracing.
    /// </summary>
    void Attach(object session);
}
