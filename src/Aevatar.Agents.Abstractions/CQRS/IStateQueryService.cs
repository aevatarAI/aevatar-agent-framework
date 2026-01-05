using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.Abstractions.CQRS;

/// <summary>
/// CQRS state query service.
/// <para/>
/// This is a thin facade over <see cref="IStateIndexService"/> to:
/// - provide a stable query surface for callers (HTTP, tools, etc.)
/// - decouple callers from index implementation details
/// </summary>
public interface IStateQueryService
{
    /// <summary>
    /// Get a single agent state document by ID.
    /// </summary>
    Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default);

    /// <summary>
    /// Query agent states within an agent type index.
    /// </summary>
    Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default);

    /// <summary>
    /// Count documents matching query.
    /// </summary>
    Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default);
}


