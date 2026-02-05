using Aevatar.VibeResearching.UserProviders.Entities;

namespace Aevatar.VibeResearching.UserProviders.Repositories;

/// <summary>
/// Repository interface for user Codex OAuth token persistence.
/// </summary>
public interface IUserCodexTokenRepository
{
    Task<UserCodexToken?> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserCodexToken> UpsertAsync(UserCodexToken entity, CancellationToken ct = default);
    Task DeleteByUserAsync(Guid userId, CancellationToken ct = default);
}
