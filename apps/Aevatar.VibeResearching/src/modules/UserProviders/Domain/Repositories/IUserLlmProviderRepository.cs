using Aevatar.VibeResearching.UserProviders.Entities;

namespace Aevatar.VibeResearching.UserProviders.Repositories;

/// <summary>
/// Repository interface for user LLM provider persistence.
/// </summary>
public interface IUserLlmProviderRepository
{
    Task<UserLlmProvider?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<UserLlmProvider?> GetByIdAndUserAsync(string id, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<UserLlmProvider>> GetByUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserLlmProvider?> GetDefaultByUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserLlmProvider?> GetByUserAndNameAsync(Guid userId, string name, CancellationToken ct = default);
    Task<int> GetCountByUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserLlmProvider> InsertAsync(UserLlmProvider entity, CancellationToken ct = default);
    Task<UserLlmProvider> UpdateAsync(UserLlmProvider entity, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task UnsetDefaultByUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserLlmProvider?> GetOldestByUserAsync(Guid userId, CancellationToken ct = default);
    Task<UserLlmProvider?> GetCodexByUserAsync(Guid userId, CancellationToken ct = default);
}
