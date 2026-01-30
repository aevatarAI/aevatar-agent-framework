using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace Aevatar.VibeResearching.Api.ReviewAgent.Storage;

/// <summary>
/// Initializes Neo4j indexes required for Review Agent queries.
/// This is a no-op when using InMemory graph backend.
/// </summary>
/// <remarks>
/// Indexes created (see ReviewAgentNeo4jIndexes.cypher):
/// - review_agent_stale: (is_activated, last_reviewed_at) - T082
/// - review_agent_cleanup: (is_activated, deactivated_timestamp) - T083
/// </remarks>
public sealed class ReviewAgentIndexInitializer
{
    private readonly IDriver? _driver;
    private readonly ILogger<ReviewAgentIndexInitializer> _logger;

    public ReviewAgentIndexInitializer(
        ILogger<ReviewAgentIndexInitializer> logger,
        IDriver? driver = null)
    {
        _logger = logger;
        _driver = driver;
    }

    /// <summary>
    /// Ensures all Review Agent indexes exist in Neo4j.
    /// Safe to call multiple times - uses IF NOT EXISTS.
    /// </summary>
    public async Task EnsureIndexesAsync(CancellationToken cancellationToken = default)
    {
        if (_driver == null)
        {
            _logger.LogDebug("Neo4j driver not available - skipping index initialization (using InMemory backend)");
            return;
        }

        _logger.LogInformation("Ensuring Review Agent Neo4j indexes exist");

        var indexQueries = new[]
        {
            // T082: Stale node queries
            """
            CREATE INDEX review_agent_stale IF NOT EXISTS
            FOR (n:KnowledgeNode)
            ON (n.is_activated, n.last_reviewed_at)
            """,

            // T083: Cleanup queries
            """
            CREATE INDEX review_agent_cleanup IF NOT EXISTS
            FOR (n:KnowledgeNode)
            ON (n.is_activated, n.deactivated_timestamp)
            """,

            // Session-scoped queries
            """
            CREATE INDEX review_agent_session IF NOT EXISTS
            FOR (n:KnowledgeNode)
            ON (n.session_id)
            """,

            // Active nodes by session
            """
            CREATE INDEX review_agent_active_session IF NOT EXISTS
            FOR (n:KnowledgeNode)
            ON (n.session_id, n.is_activated)
            """
        };

        await using var session = _driver.AsyncSession();

        foreach (var query in indexQueries)
        {
            try
            {
                await session.RunAsync(query);
                _logger.LogDebug("Index created or already exists: {Query}", query.Split('\n')[0].Trim());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create index: {Query}", query.Split('\n')[0].Trim());
            }
        }

        _logger.LogInformation("Review Agent Neo4j index initialization complete");
    }
}
