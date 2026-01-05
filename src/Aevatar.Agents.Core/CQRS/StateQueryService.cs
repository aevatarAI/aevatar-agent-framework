using Aevatar.Agents.Abstractions.CQRS;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.CQRS;

/// <summary>
/// Default implementation of <see cref="IStateQueryService"/> backed by <see cref="IStateIndexService"/>.
/// <para/>
/// NOTE:
/// - This is framework-level (in src/) so tools and apps can share the same query facade.
/// - The actual index implementation is provided by plugins (e.g. Elasticsearch).
/// </summary>
public class StateQueryService : IStateQueryService
{
    private readonly IStateIndexService _indexService;
    private readonly ILogger<StateQueryService> _logger;

    public StateQueryService(IStateIndexService indexService, ILogger<StateQueryService> logger)
    {
        _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default)
    {
        try
        {
            return await _indexService.GetByIdAsync(agentType, agentId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying state by id: {AgentType}/{AgentId}", agentType, agentId);
            throw;
        }
    }

    public async Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default)
    {
        try
        {
            return await _indexService.QueryAsync(query, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying states: {AgentType}", query?.AgentType);
            throw;
        }
    }

    public async Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default)
    {
        try
        {
            return await _indexService.CountAsync(agentType, queryString, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting states: {AgentType}", agentType);
            throw;
        }
    }
}


