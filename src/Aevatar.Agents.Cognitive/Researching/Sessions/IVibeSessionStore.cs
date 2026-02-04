using VibeResearching.Contracts.Sessions;

namespace Aevatar.Agents.Cognitive.Researching.Sessions;

// ============================================================
//  IVibeSessionStore
//
//  A small persistence abstraction for listing/loading/saving
//  Vibe sessions. Implementations live in app layer.
// ============================================================
public interface IVibeSessionStore
{
    Task<VibeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default);
    Task SaveAsync(VibeSessionRecord record, CancellationToken ct = default);
    Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<VibeSessionRecord>> ListAsync(CancellationToken ct = default);
}

