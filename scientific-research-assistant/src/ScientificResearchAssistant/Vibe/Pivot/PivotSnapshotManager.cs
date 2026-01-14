using System.Collections.Concurrent;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Knowledge.Graph.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScientificResearchAssistant.Vibe.Pivot.Messages;
using ScientificResearchAssistant.Vibe.Pivot.Models;

namespace ScientificResearchAssistant.Vibe.Pivot;

/// <summary>
/// Manages pivot snapshots for rollback capability.
/// </summary>
public sealed class PivotSnapshotManager : IPivotSnapshotManager
{
    private readonly IKnowledgeGraphClientFactory _clientFactory;
    private readonly PivotOptions _options;
    private readonly ILogger<PivotSnapshotManager> _logger;

    // Session -> PivotId -> Snapshot
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PivotSnapshotMetadata>> _snapshots = new();

    public PivotSnapshotManager(
        IKnowledgeGraphClientFactory clientFactory,
        IOptions<PivotOptions> options,
        ILogger<PivotSnapshotManager> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _options = options?.Value ?? new PivotOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PivotSnapshotMetadata> CreateSnapshotAsync(
        string sessionId,
        string pivotId,
        string directionSummary,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pivotId);

        var client = _clientFactory.CreateClient(sessionId);

        // Capture the current graph state
        var snapshot = await client.GetGraphSnapshotAsync(cancellationToken);

        // Create snapshot metadata
        var metadata = PivotSnapshotMetadata.Create(
            sessionId,
            pivotId,
            snapshot,
            directionSummary ?? string.Empty,
            _options.RollbackWindowMinutes);

        // Store the snapshot
        var sessionSnapshots = _snapshots.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, PivotSnapshotMetadata>());
        sessionSnapshots[pivotId] = metadata;

        _logger.LogInformation(
            "Created snapshot {SnapshotId} for pivot {PivotId} in session {SessionId}, expires at {ExpiresAt}",
            metadata.SnapshotId, pivotId, sessionId, metadata.ExpiresAt);

        // Cleanup expired snapshots (best-effort)
        CleanupExpiredSnapshots(sessionId);

        return metadata;
    }

    /// <inheritdoc />
    public PivotSnapshotMetadata? GetSnapshot(string sessionId, string pivotId)
    {
        if (_snapshots.TryGetValue(sessionId, out var sessionSnapshots) &&
            sessionSnapshots.TryGetValue(pivotId, out var snapshot))
        {
            return snapshot;
        }

        return null;
    }

    /// <inheritdoc />
    public PivotSnapshotMetadata? GetMostRecentSnapshot(string sessionId)
    {
        if (!_snapshots.TryGetValue(sessionId, out var sessionSnapshots))
        {
            return null;
        }

        return sessionSnapshots.Values
            .Where(s => s.IsValid)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<RollbackResponse> ExecuteRollbackAsync(
        RollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sessionId = request.SessionId;
        var pivotId = request.PivotId;

        // Find the snapshot to rollback to
        var snapshot = string.IsNullOrWhiteSpace(pivotId)
            ? GetMostRecentSnapshot(sessionId)
            : GetSnapshot(sessionId, pivotId);

        if (snapshot == null)
        {
            _logger.LogWarning("No snapshot found for rollback: session={SessionId}, pivot={PivotId}", sessionId, pivotId);
            return new RollbackResponse
            {
                SessionId = sessionId,
                PivotId = pivotId ?? string.Empty,
                Success = false,
                ErrorMessage = PivotMessages.RollbackExpired_EN
            };
        }

        if (!snapshot.IsValid)
        {
            _logger.LogWarning("Snapshot {SnapshotId} has expired for rollback", snapshot.SnapshotId);
            return new RollbackResponse
            {
                SessionId = sessionId,
                PivotId = snapshot.PivotId,
                Success = false,
                ErrorMessage = PivotMessages.RollbackExpired_EN
            };
        }

        _logger.LogInformation(
            "Starting rollback for pivot {PivotId} in session {SessionId}, preserveNewCompleted={PreserveNew}",
            snapshot.PivotId, sessionId, request.PreserveNewCompleted);

        try
        {
            var client = _clientFactory.CreateClient(sessionId);

            // Get current state to compare
            var currentSnapshot = await client.GetGraphSnapshotAsync(cancellationToken);

            // Calculate differences
            var originalNodeIds = snapshot.Snapshot.AllNodes.Select(n => n.Id).ToHashSet();
            var currentNodeIds = currentSnapshot.AllNodes.Select(n => n.Id).ToHashSet();

            // Nodes to remove (created after pivot)
            var newNodeIds = currentNodeIds.Except(originalNodeIds).ToList();

            // Nodes to restore (cancelled by pivot)
            var restoredCount = 0;
            var preservedNewCount = 0;

            // Restore cancelled nodes
            foreach (var originalNode in snapshot.Snapshot.AllNodes)
            {
                var currentNode = currentSnapshot.AllNodes.FirstOrDefault(n => n.Id == originalNode.Id);

                // Skip if node doesn't exist or hasn't changed
                if (currentNode == null)
                {
                    continue;
                }

                // Restore if it was cancelled or superseded
                if (currentNode.PivotStatus == PivotNodeStatus.Cancelled ||
                    currentNode.PivotStatus == PivotNodeStatus.Superseded)
                {
                    // Get node type - KnowledgeNode has NodeType, PlanNode uses Generic
                    var nodeType = originalNode is KnowledgeNode kn ? kn.NodeType : KnowledgeNodeType.Generic;

                    await client.UpsertNodeAsync(
                        originalNode.Id,
                        nodeType,
                        pivotStatus: originalNode.PivotStatus,
                        directionContext: originalNode.DirectionContext,
                        cancellationToken: cancellationToken);

                    restoredCount++;
                }
            }

            // Handle new nodes (created after pivot)
            var currentKnowledgeNodeIds = currentSnapshot.KnowledgeNodes.Select(n => n.Id).ToHashSet();
            foreach (var newNodeId in newNodeIds)
            {
                var newNode = currentSnapshot.AllNodes.FirstOrDefault(n => n.Id == newNodeId);
                if (newNode == null)
                {
                    continue;
                }

                if (request.PreserveNewCompleted && currentKnowledgeNodeIds.Contains(newNodeId))
                {
                    // Preserve completed knowledge nodes
                    preservedNewCount++;
                    _logger.LogDebug("Preserving new knowledge node {NodeId}", newNodeId);
                }
                else
                {
                    // Remove new plan nodes
                    await client.RemoveNodeAsync(newNodeId, cancellationToken);
                    _logger.LogDebug("Removed new node {NodeId}", newNodeId);
                }
            }

            // Remove the used snapshot
            RemoveSnapshot(sessionId, snapshot.PivotId);

            _logger.LogInformation(
                "Rollback completed for pivot {PivotId}: restored={Restored}, preservedNew={PreservedNew}",
                snapshot.PivotId, restoredCount, preservedNewCount);

            return new RollbackResponse
            {
                SessionId = sessionId,
                PivotId = snapshot.PivotId,
                Success = true,
                RestoredNodeCount = restoredCount,
                PreservedNewNodeCount = preservedNewCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed for pivot {PivotId}", snapshot.PivotId);
            return new RollbackResponse
            {
                SessionId = sessionId,
                PivotId = snapshot.PivotId,
                Success = false,
                ErrorMessage = PivotMessages.GetErrorPivotFailed(ex.Message, useChinese: false)
            };
        }
    }

    /// <inheritdoc />
    public void RemoveSnapshot(string sessionId, string pivotId)
    {
        if (_snapshots.TryGetValue(sessionId, out var sessionSnapshots))
        {
            sessionSnapshots.TryRemove(pivotId, out _);
            _logger.LogDebug("Removed snapshot for pivot {PivotId} in session {SessionId}", pivotId, sessionId);
        }
    }

    /// <inheritdoc />
    public int CleanupExpiredSnapshots(string sessionId)
    {
        var count = 0;

        if (_snapshots.TryGetValue(sessionId, out var sessionSnapshots))
        {
            var expiredPivotIds = sessionSnapshots
                .Where(kvp => !kvp.Value.IsValid)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var pivotId in expiredPivotIds)
            {
                if (sessionSnapshots.TryRemove(pivotId, out _))
                {
                    count++;
                }
            }

            if (count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired snapshots for session {SessionId}", count, sessionId);
            }
        }

        return count;
    }
}

/// <summary>
/// Interface for managing pivot snapshots.
/// </summary>
public interface IPivotSnapshotManager
{
    /// <summary>
    /// Creates a snapshot of the current graph state before a pivot operation.
    /// </summary>
    Task<PivotSnapshotMetadata> CreateSnapshotAsync(
        string sessionId,
        string pivotId,
        string directionSummary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific snapshot by session and pivot ID.
    /// </summary>
    PivotSnapshotMetadata? GetSnapshot(string sessionId, string pivotId);

    /// <summary>
    /// Gets the most recent valid snapshot for a session.
    /// </summary>
    PivotSnapshotMetadata? GetMostRecentSnapshot(string sessionId);

    /// <summary>
    /// Executes a rollback operation using a stored snapshot.
    /// </summary>
    Task<RollbackResponse> ExecuteRollbackAsync(
        RollbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a snapshot from storage.
    /// </summary>
    void RemoveSnapshot(string sessionId, string pivotId);

    /// <summary>
    /// Cleans up expired snapshots for a session.
    /// </summary>
    int CleanupExpiredSnapshots(string sessionId);
}
