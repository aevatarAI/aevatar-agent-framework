using Microsoft.Extensions.Logging;
using Aevatar.Agents.Knowledge.Graph.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.VibeResearching.Agents.Tools;

namespace Aevatar.VibeResearching.Agents.ReviewAgent;

/// <summary>
/// Core implementation of the Review Agent service.
/// Manages knowledge node validation lifecycle, state, and settings.
/// </summary>
public sealed class ReviewAgentService : IReviewAgentService
{
    private readonly IOptionsMonitor<ReviewAgentOptions> _optionsMonitor;
    private readonly IVibeGraphAccess _graphAccess;
    private readonly IKnowledgeNodeVerifier? _verifier;
    private readonly ILogger<ReviewAgentService> _logger;

    private readonly ReviewAgentState _state;
    private readonly object _stateLock = new();

    // Runtime settings override (T068 - allows updates without restart)
    private ReviewAgentOptions? _runtimeSettings;
    private readonly object _settingsLock = new();

    // Event handler for progress reporting (wired by hosted service)
    // Parameters: nodeId, nodeLabel, explainContent, nodesReviewed, nodesPending, nodesDeactivated, result
    public Func<string, string, string?, int, int, int, ReviewResult?, Task>? OnNodeProgress { get; set; }
    public Func<string, string, string, bool, Task>? OnTokenStream { get; set; }

    public ReviewAgentService(
        IOptionsMonitor<ReviewAgentOptions> optionsMonitor,
        IVibeGraphAccess graphAccess,
        ILogger<ReviewAgentService> logger,
        IKnowledgeNodeVerifier? verifier = null)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _graphAccess = graphAccess ?? throw new ArgumentNullException(nameof(graphAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _verifier = verifier;

        _state = new ReviewAgentState();
    }

    /// <inheritdoc />
    public ReviewAgentState GetState()
    {
        lock (_stateLock)
        {
            return _state.Clone();
        }
    }

    /// <inheritdoc />
    public ReviewAgentOptions GetSettings()
    {
        // Return runtime override if set, otherwise fall back to config
        lock (_settingsLock)
        {
            return _runtimeSettings ?? _optionsMonitor.CurrentValue;
        }
    }

    /// <inheritdoc />
    public Task<ReviewAgentOptions> UpdateSettingsAsync(ReviewAgentOptions options)
    {
        // T068: Store runtime override for immediate effect without restart
        lock (_settingsLock)
        {
            _runtimeSettings = options;
        }

        _logger.LogInformation(
            "Settings updated (runtime override): Interval={Interval}m, OutOfDate={OutOfDate}m, ToDelete={ToDelete}m, LLM={LLM}",
            options.IterationIntervalMinutes,
            options.OutOfDateThresholdMinutes,
            options.ToDeleteThresholdMinutes,
            options.LLMProviderName);

        return Task.FromResult(options);
    }

    /// <inheritdoc />
    public async Task<ReviewIteration> RunReviewRoundAsync(CancellationToken ct = default)
    {
        var iterationId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation("Starting review round {IterationId}", iterationId);

        // Update state to working
        lock (_stateLock)
        {
            _state.Status = ReviewAgentStatus.WorkingReviewRound;
            _state.CurrentIterationId = iterationId;
            _state.ErrorMessage = null;
            _state.ResetCounters();
        }

        var entries = new List<ReviewLogEntry>();
        var nodesValid = 0;
        var nodesDeactivated = 0;

        try
        {
            var settings = GetSettings();
            var outOfDateThreshold = TimeSpan.FromMinutes(settings.OutOfDateThresholdMinutes);

            // Get stale nodes (topologically sorted - ancestors first)
            var staleNodes = await _graphAccess.GetStaleKnowledgeNodesAsync(outOfDateThreshold, ct);

            _logger.LogInformation("Found {Count} stale nodes to review", staleNodes.Count);

            lock (_stateLock)
            {
                _state.NodesPending = staleNodes.Count;
            }

            foreach (var node in staleNodes)
            {
                ct.ThrowIfCancellationRequested();

                // Build explanation content for SSE events
                var explainContent = BuildExplanationContent(node);

                // Publish node progress
                var reviewed = entries.Count;
                var pending = staleNodes.Count - reviewed;
                if (OnNodeProgress != null)
                {
                    await OnNodeProgress(node.Id, node.CoreDescription, explainContent, reviewed, pending, nodesDeactivated, null);
                }

                // Verify the node
                var verificationResult = await VerifyNodeAsync(node, ct);

                // Update progress
                lock (_stateLock)
                {
                    _state.NodesReviewed++;
                    _state.NodesPending--;
                    if (verificationResult.Result == ReviewResult.Failed)
                    {
                        _state.NodesDeactivated++;
                    }
                }

                var entry = new ReviewLogEntry
                {
                    EntryId = $"{iterationId}_{node.Id}",
                    NodeId = node.Id,
                    NodeLabel = node.CoreDescription,
                    ExplainContent = explainContent,
                    Dependencies = node.DependsOn.ToList(),
                    ReviewResult = verificationResult.Result,
                    DeactivatedReason = verificationResult.FailureReason,
                    Timestamp = DateTimeOffset.UtcNow,
                    VerificationContent = verificationResult.VerificationContent
                };

                entries.Add(entry);

                // Also add to state for API access during iteration
                lock (_stateLock)
                {
                    _state.CurrentIterationEntries.Add(entry);
                }

                if (verificationResult.Passed)
                {
                    nodesValid++;

                    // Update node's last_reviewed_at
                    await _graphAccess.UpdateNodeReviewStatusAsync(
                        node.SessionId,
                        node.Id,
                        DateTimeOffset.UtcNow,
                        ct);
                }
                else
                {
                    nodesDeactivated++;

                    // Deactivate the node and its descendants
                    await _graphAccess.DeactivateNodeWithDescendantsAsync(
                        node.SessionId,
                        node.Id,
                        verificationResult.FailureReason ?? "Failed verification",
                        DateTimeOffset.UtcNow,
                        ct);
                }

                // Publish node completion progress
                if (OnNodeProgress != null)
                {
                    await OnNodeProgress(node.Id, node.CoreDescription, explainContent, reviewed + 1, pending - 1, nodesDeactivated, verificationResult.Result);
                }
            }

            var completedAt = DateTimeOffset.UtcNow;

            // Create iteration record
            var iteration = new ReviewIteration
            {
                IterationId = iterationId,
                StartedAt = startedAt,
                CompletedAt = completedAt,
                NodesReviewed = entries.Count,
                NodesValid = nodesValid,
                NodesDeactivated = nodesDeactivated,
                NodesRemoved = 0,
                Entries = entries
            };

            // Update state to idle
            lock (_stateLock)
            {
                _state.Status = ReviewAgentStatus.Idle;
                _state.CurrentIterationId = null;
                _state.LastCompletedAt = completedAt;
                _state.NextScheduledAt = completedAt.AddMinutes(settings.IterationIntervalMinutes);
            }

            _logger.LogInformation(
                "Review round {IterationId} completed: {Reviewed} reviewed, {Valid} valid, {Deactivated} deactivated",
                iterationId, entries.Count, nodesValid, nodesDeactivated);

            return iteration;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Review round {IterationId} was cancelled", iterationId);
            lock (_stateLock)
            {
                _state.Status = ReviewAgentStatus.Idle;
                _state.CurrentIterationId = null;
            }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review round {IterationId} failed with error", iterationId);
            lock (_stateLock)
            {
                _state.Status = ReviewAgentStatus.Error;
                _state.CurrentIterationId = null;
                _state.ErrorMessage = ex.Message;
            }
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> RunCleanupRoundAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting cleanup round");

        // Update state to cleanup
        lock (_stateLock)
        {
            _state.Status = ReviewAgentStatus.WorkingCleanupRound;
            _state.ErrorMessage = null;
        }

        try
        {
            var settings = GetSettings();
            var toDeleteThreshold = TimeSpan.FromMinutes(settings.ToDeleteThresholdMinutes);

            // Get nodes that have been deactivated long enough for deletion
            var nodesToCleanup = await _graphAccess.GetNodesForCleanupAsync(toDeleteThreshold, ct);

            _logger.LogInformation("Found {Count} deactivated nodes eligible for cleanup", nodesToCleanup.Count);

            if (nodesToCleanup.Count == 0)
            {
                lock (_stateLock)
                {
                    _state.Status = ReviewAgentStatus.Idle;
                }
                return 0;
            }

            var nodeIds = nodesToCleanup.Select(n => n.Id).ToList();
            var removedCount = await _graphAccess.RemoveDeactivatedNodesAsync(nodeIds, ct);

            lock (_stateLock)
            {
                _state.Status = ReviewAgentStatus.Idle;
                _state.NodesRemoved += removedCount;
            }

            _logger.LogInformation("Cleanup round completed: {Removed} nodes removed", removedCount);

            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup round failed with error");
            lock (_stateLock)
            {
                _state.Status = ReviewAgentStatus.Error;
                _state.ErrorMessage = ex.Message;
            }
            throw;
        }
    }

    /// <inheritdoc />
    public Task<IterationListResponse> GetIterationsAsync(int limit = 50, int offset = 0)
    {
        // TODO: Implement with IReviewAgentStorage
        // For now, return empty list
        var response = new IterationListResponse
        {
            Iterations = [],
            Total = 0,
            Limit = limit,
            Offset = offset
        };

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task<ReviewIteration?> GetIterationAsync(string iterationId)
    {
        // TODO: Implement with IReviewAgentStorage
        // For now, return null
        return Task.FromResult<ReviewIteration?>(null);
    }

    /// <inheritdoc />
    public void SetNextScheduledAt(DateTimeOffset nextScheduledAt)
    {
        lock (_stateLock)
        {
            _state.NextScheduledAt = nextScheduledAt;
        }
    }

    /// <inheritdoc />
    public void SetError(string errorMessage)
    {
        lock (_stateLock)
        {
            _state.Status = ReviewAgentStatus.Error;
            _state.ErrorMessage = errorMessage;
        }
    }

    /// <inheritdoc />
    public ReviewGraphData GetGraphData()
    {
        try
        {
            // Get all knowledge nodes from the graph
            // Use synchronous wrapper since this is called from sync API endpoint
            var settings = GetSettings();
            var outOfDateThreshold = TimeSpan.FromMinutes(settings.OutOfDateThresholdMinutes);

            // Get all stale nodes (this includes nodes that need review)
            var staleNodesTask = _graphAccess.GetStaleKnowledgeNodesAsync(outOfDateThreshold, CancellationToken.None);
            staleNodesTask.Wait();
            var staleNodes = staleNodesTask.Result;

            // Get nodes pending cleanup (deactivated)
            var toDeleteThreshold = TimeSpan.FromMinutes(settings.ToDeleteThresholdMinutes);
            var cleanupNodesTask = _graphAccess.GetNodesForCleanupAsync(toDeleteThreshold, CancellationToken.None);
            cleanupNodesTask.Wait();
            var cleanupNodes = cleanupNodesTask.Result;

            // Combine all nodes into a dictionary to avoid duplicates
            var nodeMap = new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal);
            foreach (var node in staleNodes)
            {
                nodeMap.TryAdd(node.Id, node);
            }
            foreach (var node in cleanupNodes)
            {
                nodeMap.TryAdd(node.Id, node);
            }

            // Convert to ReviewGraphNode
            var nodes = nodeMap.Values.Select(n => new ReviewGraphNode
            {
                NodeId = n.Id,
                Label = n.CoreDescription,
                SessionId = n.SessionId,
                IsActivated = n.IsActivated,
                LastReviewedAt = n.LastReviewedAt,
                DeactivatedTimestamp = n.DeactivatedTimestamp,
                DeactivatedReason = n.DeactivatedReason,
                DependsOn = n.DependsOn.ToList(),
            }).ToList();

            // Build edges from dependencies
            var nodeIds = new HashSet<string>(nodeMap.Keys, StringComparer.Ordinal);
            var edges = new List<ReviewGraphEdge>();
            foreach (var node in nodeMap.Values)
            {
                foreach (var depId in node.DependsOn)
                {
                    // Only include edges where both nodes exist in our graph
                    if (nodeIds.Contains(depId))
                    {
                        edges.Add(new ReviewGraphEdge
                        {
                            FromId = depId,
                            ToId = node.Id,
                            Type = "depends_on",
                        });
                    }
                }
            }

            return new ReviewGraphData
            {
                Nodes = nodes,
                Edges = edges,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get graph data for review visualization");
            return new ReviewGraphData
            {
                Nodes = [],
                Edges = [],
            };
        }
    }

    /// <summary>
    /// Verifies a knowledge node using the configured verifier or defaults to passing.
    /// </summary>
    private async Task<VerificationResult> VerifyNodeAsync(KnowledgeNode node, CancellationToken ct)
    {
        // If no verifier is configured, all nodes pass (for testing/development)
        if (_verifier == null)
        {
            _logger.LogDebug("No verifier configured, node {NodeId} passes by default", node.Id);
            return new VerificationResult
            {
                Passed = true,
                Result = ReviewResult.Passed,
                Duration = TimeSpan.Zero
            };
        }

        var settings = GetSettings();

        // Build explanation content from node fields
        var explanationContent = BuildExplanationContent(node);

        // Get dependency labels for context
        var dependencyLabels = await GetDependencyLabelsAsync(node, ct);

        var input = new VerificationInput
        {
            NodeId = node.Id,
            NodeLabel = node.CoreDescription,
            ExplanationContent = explanationContent,
            DependencyIds = node.DependsOn,
            DependencyLabels = dependencyLabels,
            LLMProviderName = settings.LLMProviderName,
            TimeoutSeconds = settings.PerNodeTimeoutSeconds
        };

        // Create progress adapter for token streaming
        IProgress<VerificationProgress>? progress = null;
        if (OnTokenStream != null)
        {
            progress = new Progress<VerificationProgress>(async vp =>
            {
                await OnTokenStream(vp.AgentId, vp.AgentRole, vp.Token, vp.IsComplete);
            });
        }

        return await _verifier.VerifyAsync(input, progress, ct);
    }

    private static string BuildExplanationContent(KnowledgeNode node)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(node.DetailedDescription))
        {
            parts.Add($"**Detailed Description:**\n{node.DetailedDescription}");
        }

        if (!string.IsNullOrWhiteSpace(node.Proof))
        {
            parts.Add($"**Proof:**\n{node.Proof}");
        }

        if (!string.IsNullOrWhiteSpace(node.DerivationProcess))
        {
            parts.Add($"**Derivation Process:**\n{node.DerivationProcess}");
        }

        if (node.References.Count > 0)
        {
            parts.Add($"**References:**\n{string.Join("\n", node.References.Select(r => $"- {r}"))}");
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : node.CoreDescription;
    }

    private async Task<IReadOnlyList<string>> GetDependencyLabelsAsync(KnowledgeNode node, CancellationToken ct)
    {
        var labels = new List<string>();

        foreach (var depId in node.DependsOn)
        {
            try
            {
                // Get dependency node labels from graph
                // For now, we use the IDs as placeholder - full implementation would query graph
                labels.Add(depId);
            }
            catch
            {
                labels.Add(depId);
            }
        }

        return labels;
    }

    /// <inheritdoc />
    public Task<object> GetStatusAsync(CancellationToken ct = default)
    {
        var state = GetState();
        return Task.FromResult<object>(state);
    }

    /// <inheritdoc />
    public Task<object> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = GetSettings();
        return Task.FromResult<object>(settings);
    }

    /// <inheritdoc />
    public async Task<object> UpdateSettingsAsync(object settings, CancellationToken ct = default)
    {
        if (settings is ReviewAgentOptions options)
        {
            return await UpdateSettingsAsync(options);
        }

        throw new ArgumentException("Invalid settings type. Expected ReviewAgentOptions.", nameof(settings));
    }

    /// <inheritdoc />
    public Task<object> GetReviewGraphAsync(CancellationToken ct = default)
    {
        var graphData = GetGraphData();
        return Task.FromResult<object>(graphData);
    }

    /// <inheritdoc />
    public async Task<object> TriggerReviewAsync(CancellationToken ct = default)
    {
        var iteration = await RunReviewRoundAsync(ct);
        return iteration;
    }
}
