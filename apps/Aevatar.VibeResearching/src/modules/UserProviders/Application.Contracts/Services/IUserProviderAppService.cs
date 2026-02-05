using Aevatar.VibeResearching.UserProviders.DTOs;

namespace Aevatar.VibeResearching.UserProviders.Services;

/// <summary>
/// Application service for user LLM provider CRUD operations.
/// </summary>
public interface IUserProviderAppService
{
    Task<IReadOnlyList<UserLlmProviderDto>> GetListAsync(CancellationToken ct = default);
    Task<UserLlmProviderDto> CreateAsync(CreateUserProviderDto input, CancellationToken ct = default);
    Task<UserLlmProviderDto> UpdateAsync(string id, UpdateUserProviderDto input, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SetDefaultAsync(SetDefaultProviderDto input, CancellationToken ct = default);
    Task<ProviderTestResultDto> TestAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<ProviderModelDto>> GetModelsAsync(string id, int limit = 50, CancellationToken ct = default);
}
