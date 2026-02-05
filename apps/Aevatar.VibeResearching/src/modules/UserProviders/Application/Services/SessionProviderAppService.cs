using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.ValueObjects;
using Aevatar.VibeResearching.UserProviders.DTOs;
using Aevatar.VibeResearching.UserProviders.Repositories;
using Aevatar.VibeResearching.UserProviders.Services;
using Aevatar.VibeResearching.UserProviders.ValueObjects;
using Volo.Abp.Users;

namespace Aevatar.VibeResearching.UserProviders.Application.Services;

/// <summary>
/// Application service for session-level agent-provider mapping management.
/// </summary>
public sealed class SessionProviderAppService : ISessionProviderAppService
{
    private readonly IAgentProvidersRepository _agentProvidersRepo;
    private readonly IProviderResolutionService _resolutionService;
    private readonly IUserLlmProviderRepository _providerRepo;
    private readonly ICurrentUser _currentUser;

    public SessionProviderAppService(
        IAgentProvidersRepository agentProvidersRepo,
        IProviderResolutionService resolutionService,
        IUserLlmProviderRepository providerRepo,
        ICurrentUser currentUser)
    {
        _agentProvidersRepo = agentProvidersRepo;
        _resolutionService = resolutionService;
        _providerRepo = providerRepo;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<AgentProvidersResponseDto> GetAgentProvidersAsync(
        string sessionId, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var snapshot = await _agentProvidersRepo.LoadAsync(sessionId, ct);

        if (snapshot == null || snapshot.Map.Count == 0)
        {
            return new AgentProvidersResponseDto
            {
                Version = 0,
                UpdatedAt = string.Empty,
                Map = new Dictionary<string, AgentProviderDetailDto>()
            };
        }

        var enrichedMap = await BuildEnrichedMapAsync(snapshot, userId, ct);

        return new AgentProvidersResponseDto
        {
            Version = snapshot.Version,
            UpdatedAt = snapshot.UpdatedAt,
            Map = enrichedMap
        };
    }

    /// <inheritdoc />
    public async Task<AgentProvidersResponseDto> UpdateAgentProvidersAsync(
        string sessionId, UpdateAgentProvidersDto input, CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();

        foreach (var (agentName, nsString) in input.Map)
        {
            if (string.IsNullOrWhiteSpace(nsString))
            {
                // Remove the mapping (set to "default" to trigger removal in the repository)
                await _agentProvidersRepo.UpsertAsync(sessionId, agentName, "default", ct);
            }
            else
            {
                // Validate namespace references a valid provider
                var ns = ProviderNamespace.Parse(nsString);
                if (ns.IsUser)
                {
                    var provider = await _providerRepo.GetByIdAndUserAsync(ns.Identifier, userId, ct);
                    if (provider == null)
                        throw new KeyNotFoundException($"User provider '{ns.Identifier}' not found.");
                }

                await _agentProvidersRepo.UpsertAsync(sessionId, agentName, nsString, ct);
            }
        }

        // Return the updated state
        return await GetAgentProvidersAsync(sessionId, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableProviderDto>> GetAvailableProvidersAsync(
        CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        var providers = await _resolutionService.GetAvailableProvidersAsync(userId, ct);

        return providers.Select(p => new AvailableProviderDto
        {
            Namespace = p.Namespace,
            Name = p.Name,
            ProviderType = p.ProviderType,
            DefaultModel = p.DefaultModel,
            Source = p.Source,
            IsDefault = p.IsDefault
        }).ToList();
    }

    private async Task<Dictionary<string, AgentProviderDetailDto>> BuildEnrichedMapAsync(
        AgentProvidersSnapshot snapshot, Guid userId, CancellationToken ct)
    {
        var enrichedMap = new Dictionary<string, AgentProviderDetailDto>();
        foreach (var (agentName, nsString) in snapshot.Map)
        {
            if (string.IsNullOrWhiteSpace(nsString))
                continue;

            var ns = ProviderNamespace.Parse(nsString);
            var detail = await ResolveProviderDetailAsync(ns, userId, ct);
            if (detail != null)
            {
                enrichedMap[agentName] = detail;
            }
        }
        return enrichedMap;
    }

    private async Task<AgentProviderDetailDto?> ResolveProviderDetailAsync(
        ProviderNamespace ns, Guid userId, CancellationToken ct)
    {
        if (ns.IsUser)
        {
            var provider = await _providerRepo.GetByIdAndUserAsync(ns.Identifier, userId, ct);
            if (provider == null)
                return null;

            return new AgentProviderDetailDto
            {
                Namespace = ns.ToString(),
                Name = provider.Name,
                ProviderType = provider.ProviderType,
                Model = provider.DefaultModel
            };
        }

        // Platform provider: return basic info from namespace
        return new AgentProviderDetailDto
        {
            Namespace = ns.ToString(),
            Name = ns.Identifier,
            ProviderType = "Platform",
            Model = ""
        };
    }

    private Guid GetCurrentUserId()
    {
        return _currentUser.Id
            ?? throw new UnauthorizedAccessException("User is not authenticated.");
    }
}
