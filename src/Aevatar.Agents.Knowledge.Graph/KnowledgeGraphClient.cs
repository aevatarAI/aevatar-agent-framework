using System.Text;
using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Knowledge.Graph.Services;
using Aevatar.Agents.Knowledge.Graph.Storage;
using Aevatar.Agents.Knowledge.Graph.Store;
using Aevatar.Agents.Knowledge.Graph.Validation;

namespace Aevatar.Agents.Knowledge.Graph;

/// <summary>
/// Main implementation of the knowledge graph client for scientific research assistants.
/// Each instance is scoped to a specific session.
/// </summary>
internal sealed class KnowledgeGraphClient : IKnowledgeGraphClient
{
    private readonly IKnowledgeGraphStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly DagValidator _dagValidator;
    private readonly GraphOperationLock _operationLock;

    public string SessionId { get; }

    public KnowledgeGraphClient(string sessionId, IKnowledgeGraphStore store, IFileStorage fileStorage, GraphOperationLock? operationLock = null)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _store = store;
        _fileStorage = fileStorage;
        _dagValidator = new DagValidator(store);
        _operationLock = operationLock ?? new GraphOperationLock();
    }

    // ========== Session Management ==========

    public Task<Session?> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        return _store.GetSessionAsync(SessionId, cancellationToken);
    }

    public async Task<Session> EndSessionAsync(SessionStatus status = SessionStatus.Completed, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        var session = await _store.GetSessionAsync(SessionId, cancellationToken);
        if (session == null)
        {
            throw GraphOperationException.SessionNotFound(SessionId);
        }

        // This will throw InvalidOperationException if session is not active
        session.End(status);

        await _store.UpdateSessionAsync(session, cancellationToken);
        return session;
    }

    // ========== Node Operations ==========

    public async Task<KnowledgeNode> AddNodeAsync(
        string nodeId,
        KnowledgeNodeType nodeType,
        string coreDescription,
        string detailedDescription,
        string? owner = null,
        string? proof = null,
        string? resourceFolderPath = null,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailedDescription);

        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        // Check for duplicate
        if (await _dagValidator.NodeExistsAsync(SessionId, nodeId, cancellationToken))
        {
            throw new DuplicateNodeException(nodeId);
        }

        var dependsOnList = dependsOn?.ToList() ?? [];

        // Validate all dependencies exist
        foreach (var depId in dependsOnList)
        {
            if (!await _dagValidator.NodeExistsAsync(SessionId, depId, cancellationToken))
            {
                throw new NodeNotFoundException(depId);
            }
        }

        // Validate no cycle would be created
        if (dependsOnList.Count > 0)
        {
            var isValid = await _dagValidator.ValidateNoCycleAsync(SessionId, nodeId, dependsOnList, cancellationToken);
            if (!isValid)
            {
                throw new CycleDetectedException(nodeId, dependsOnList[0]);
            }
        }

        // Upload folder to S3 if provided (folder will be zipped)
        string? resourceUri = null;
        if (!string.IsNullOrWhiteSpace(resourceFolderPath) && Directory.Exists(resourceFolderPath))
        {
            resourceUri = await _fileStorage.UploadFolderAsync(SessionId, resourceFolderPath, null, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var node = new KnowledgeNode
        {
            Id = nodeId,
            SessionId = SessionId,
            NodeType = nodeType,
            Owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim(),
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            Proof = proof,
            ResourceFolderPath = resourceFolderPath,
            ResourceUri = resourceUri,
            CreatedAt = now,
            UpdatedAt = now,
            DependsOn = dependsOnList
        };

        await _store.AddKnowledgeNodeAsync(node, cancellationToken);

        // Add edges: node -[DEPENDS_ON]-> dependency
        foreach (var depId in dependsOnList)
        {
            await _store.AddEdgeAsync(new KnowledgeEdge
            {
                FromId = nodeId,
                ToId = depId,
                CreatedAt = DateTimeOffset.UtcNow
            }, SessionId, cancellationToken);
        }

        return node;
    }

    public async Task<KnowledgeNode> UpsertNodeAsync(
        string nodeId,
        KnowledgeNodeType nodeType,
        string? coreDescription = null,
        string? detailedDescription = null,
        string? owner = null,
        string? proof = null,
        string? resourceFolderPath = null,
        PivotNodeStatus? pivotStatus = null,
        DateTimeOffset? cancelledAt = null,
        string? cancelledByPivotId = null,
        string? directionContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        var coreIn = (coreDescription ?? string.Empty).Trim();
        var detailIn = (detailedDescription ?? string.Empty).Trim();
        var proofIn = string.IsNullOrWhiteSpace(proof) ? null : proof.Trim();
        var ownerIn = (owner ?? string.Empty).Trim();
        var directionIn = string.IsNullOrWhiteSpace(directionContext) ? null : directionContext.Trim();
        var cancelledByIn = string.IsNullOrWhiteSpace(cancelledByPivotId) ? null : cancelledByPivotId.Trim();

        var now = DateTimeOffset.UtcNow;
        var existing = await _store.GetKnowledgeNodeAsync(SessionId, nodeId, cancellationToken);

        // Keep DependsOn in sync with edge-truth (best-effort).
        var deps = await _store.GetDependenciesAsync(SessionId, nodeId, cancellationToken);
        var depsList = deps
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        if (existing == null)
        {
            // Create: fall back to nodeId when core/detailed not provided.
            var core = coreIn.Length == 0 ? nodeId : coreIn;
            var detail = detailIn.Length == 0 ? core : detailIn;

            string? resourceUri = null;
            if (!string.IsNullOrWhiteSpace(resourceFolderPath) && Directory.Exists(resourceFolderPath))
            {
                resourceUri = await _fileStorage.UploadFolderAsync(SessionId, resourceFolderPath, null, cancellationToken);
            }

            var created = new KnowledgeNode
            {
                Id = nodeId,
                SessionId = SessionId,
                NodeType = nodeType,
                Owner = ownerIn.Length == 0 ? null : ownerIn,
                CoreDescription = core,
                DetailedDescription = detail,
                Proof = proofIn,
                ResourceFolderPath = resourceFolderPath,
                ResourceUri = resourceUri,
                CreatedAt = now,
                UpdatedAt = now,
                DependsOn = depsList,
                PivotStatus = pivotStatus ?? PivotNodeStatus.Active,
                CancelledAt = cancelledAt,
                CancelledByPivotId = cancelledByIn,
                DirectionContext = directionIn
            };

            await _store.AddKnowledgeNodeAsync(created, cancellationToken);
            return created;
        }

        // Update: empty inputs keep existing values.
        // NodeType: treat Generic as "unspecified" (do not downgrade).
        var mergedType = nodeType != KnowledgeNodeType.Generic ? nodeType : existing.NodeType;
        var mergedOwner = ownerIn.Length == 0 ? existing.Owner : ownerIn;
        var mergedCore = coreIn.Length == 0 ? existing.CoreDescription : coreIn;
        var mergedDetail = detailIn.Length == 0 ? existing.DetailedDescription : detailIn;
        var mergedProof = proofIn ?? existing.Proof;

        // Pivot fields: explicit values override existing, null keeps existing
        var mergedPivotStatus = pivotStatus ?? existing.PivotStatus;
        var mergedCancelledAt = cancelledAt ?? existing.CancelledAt;
        var mergedCancelledByPivotId = cancelledByIn ?? existing.CancelledByPivotId;
        var mergedDirectionContext = directionIn ?? existing.DirectionContext;

        // Upload folder to S3 if provided (folder will be zipped)
        // Note: We do not store the local folder path in the graph backend; only HTTPS URL is persisted.
        var mergedResourceUri = existing.ResourceUri;
        if (!string.IsNullOrWhiteSpace(resourceFolderPath) && Directory.Exists(resourceFolderPath))
        {
            mergedResourceUri = await _fileStorage.UploadFolderAsync(SessionId, resourceFolderPath, null, cancellationToken);
        }

        var updated = new KnowledgeNode
        {
            Id = existing.Id,
            SessionId = existing.SessionId,
            NodeType = mergedType,
            Owner = mergedOwner,
            CoreDescription = mergedCore,
            DetailedDescription = mergedDetail,
            Proof = mergedProof,
            ResourceFolderPath = resourceFolderPath,
            ResourceUri = mergedResourceUri,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now,
            DependsOn = depsList,
            Attestations = existing.Attestations,
            PivotStatus = mergedPivotStatus,
            CancelledAt = mergedCancelledAt,
            CancelledByPivotId = mergedCancelledByPivotId,
            DirectionContext = mergedDirectionContext
        };

        await _store.AddKnowledgeNodeAsync(updated, cancellationToken);
        return updated;
    }

    public async Task AddDependenciesAsync(
        string nodeId,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var deps = (dependsOn ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (deps.Count == 0)
        {
            return;
        }

        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        // Try to get node as either PlanNode or KnowledgeNode
        var planNode = await _store.GetPlanNodeAsync(SessionId, nodeId, cancellationToken);
        var knowledgeNode = planNode == null
            ? await _store.GetKnowledgeNodeAsync(SessionId, nodeId, cancellationToken)
            : null;

        if (planNode == null && knowledgeNode == null)
        {
            throw new NodeNotFoundException(nodeId);
        }

        var existingDeps = await _store.GetDependenciesAsync(SessionId, nodeId, cancellationToken);
        var existingSet = new HashSet<string>(
            existingDeps.Select(x => (x ?? string.Empty).Trim()).Where(x => x.Length > 0),
            StringComparer.Ordinal);

        // Validate deps exist + no-cycle, then add missing edges.
        foreach (var depId in deps)
        {
            if (existingSet.Contains(depId))
            {
                continue;
            }

            if (!await _dagValidator.NodeExistsAsync(SessionId, depId, cancellationToken))
            {
                throw new NodeNotFoundException(depId);
            }

            var ok = await _dagValidator.ValidateNoCycleAsync(SessionId, nodeId, [depId], cancellationToken);
            if (!ok)
            {
                throw new CycleDetectedException(nodeId, depId);
            }

            await _store.AddEdgeAsync(new KnowledgeEdge
            {
                FromId = nodeId,
                ToId = depId,
                CreatedAt = DateTimeOffset.UtcNow
            }, SessionId, cancellationToken);

            existingSet.Add(depId);
        }

        // Update node.dependsOn property for snapshot friendliness (edge-truth is authoritative).
        var mergedDeps = existingSet.OrderBy(x => x, StringComparer.Ordinal).ToList();

        if (planNode != null)
        {
            var updatedPlan = new PlanNode
            {
                Id = planNode.Id,
                SessionId = planNode.SessionId,
                CoreDescription = planNode.CoreDescription,
                DetailedDescription = planNode.DetailedDescription,
                Status = planNode.Status,
                ProgressText = planNode.ProgressText,
                Methodology = planNode.Methodology,
                SequentialOrder = planNode.SequentialOrder,
                Owner = planNode.Owner,
                DependsOn = mergedDeps,
                PivotStatus = planNode.PivotStatus,
                CancelledAt = planNode.CancelledAt,
                CancelledByPivotId = planNode.CancelledByPivotId,
                DirectionContext = planNode.DirectionContext,
                CreatedAt = planNode.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _store.AddPlanNodeAsync(updatedPlan, cancellationToken);
        }
        else if (knowledgeNode != null)
        {
            var updatedKnowledge = new KnowledgeNode
            {
                Id = knowledgeNode.Id,
                SessionId = knowledgeNode.SessionId,
                NodeType = knowledgeNode.NodeType,
                CoreDescription = knowledgeNode.CoreDescription,
                DetailedDescription = knowledgeNode.DetailedDescription,
                Proof = knowledgeNode.Proof,
                ResourceFolderPath = knowledgeNode.ResourceFolderPath,
                ResourceUri = knowledgeNode.ResourceUri,
                CreatedAt = knowledgeNode.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                Owner = knowledgeNode.Owner,
                DependsOn = mergedDeps,
                Attestations = knowledgeNode.Attestations,
                PivotStatus = knowledgeNode.PivotStatus,
                CancelledAt = knowledgeNode.CancelledAt,
                CancelledByPivotId = knowledgeNode.CancelledByPivotId,
                DirectionContext = knowledgeNode.DirectionContext
            };
            await _store.AddKnowledgeNodeAsync(updatedKnowledge, cancellationToken);
        }
    }

    public async Task<GraphSnapshot> GetGraphSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var planNodes = await _store.GetAllPlanNodesAsync(SessionId, cancellationToken);
        var knowledgeNodes = await _store.GetAllKnowledgeNodesAsync(SessionId, cancellationToken);
        var edges = await _store.GetAllEdgesAsync(SessionId, cancellationToken);

        return new GraphSnapshot
        {
            SessionId = SessionId,
            PlanNodes = planNodes.OrderBy(n => n.SequentialOrder).ThenBy(n => n.Id).ToList(),
            KnowledgeNodes = knowledgeNodes.OrderBy(n => n.NodeType).ThenBy(n => n.Id).ToList(),
            Edges = edges.OrderBy(e => e.FromId).ThenBy(e => e.ToId).ToList()
        };
    }

    [Obsolete("Use GetGraphSnapshotAsync instead. This method will be removed in a future version.")]
    public async Task<KnowledgeSnapshot> GetKnowledgeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetGraphSnapshotAsync(cancellationToken);
        return new KnowledgeSnapshot
        {
            SessionId = snapshot.SessionId,
            PlanNodes = snapshot.PlanNodes.ToList(),
            KnowledgeNodes = snapshot.KnowledgeNodes.ToList(),
            Edges = snapshot.Edges.ToList()
        };
    }

    private async Task<KnowledgeChain> GetKnowledgeChainAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var targetNode = await _store.GetKnowledgeNodeAsync(SessionId, nodeId, cancellationToken);
        if (targetNode == null)
        {
            throw new NodeNotFoundException(nodeId);
        }

        // BFS to build levels
        var levels = new List<KnowledgeChainLevel>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<KnowledgeNode>();
        var currentLevel = new List<string> { nodeId };

        while (currentLevel.Count > 0)
        {
            var levelNodes = new List<KnowledgeNode>();

            foreach (var id in currentLevel)
            {
                if (!visited.Add(id)) continue;

                var node = await _store.GetKnowledgeNodeAsync(SessionId, id, cancellationToken);
                if (node != null)
                {
                    levelNodes.Add(node);
                    chain.Add(node);
                }
            }

            if (levelNodes.Count > 0)
            {
                levels.Add(new KnowledgeChainLevel
                {
                    Depth = levels.Count,
                    Nodes = levelNodes
                });
            }

            // Get next level (dependencies of current level)
            var nextLevel = new List<string>();
            foreach (var id in currentLevel)
            {
                var deps = await _store.GetDependenciesAsync(SessionId, id, cancellationToken);
                foreach (var dep in deps)
                {
                    if (!visited.Contains(dep))
                    {
                        nextLevel.Add(dep);
                    }
                }
            }
            currentLevel = nextLevel;
        }

        return new KnowledgeChain
        {
            SessionId = SessionId,
            TargetNode = targetNode,
            Levels = levels,
            Chain = chain
        };
    }

    public async Task<KnowledgeChainDetails> GetKnowledgeChainDetailsAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var chain = await GetKnowledgeChainAsync(nodeId, cancellationToken);
        var description = FormatKnowledgePaper(chain);
        return new KnowledgeChainDetails
        {
            Chain = chain,
            Description = description
        };
    }

    public async Task<string> GenerateFullPaperAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetKnowledgeSnapshotAsync(cancellationToken);
        return FormatFullPaper(snapshot);
    }

    private static string FormatKnowledgePaper(KnowledgeChain chain)
    {
        var sb = new StringBuilder();

        // Title
        sb.AppendLine($"# {chain.TargetNode.CoreDescription}");
        sb.AppendLine();
        sb.AppendLine($"**Generated**: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Knowledge Chain Depth**: {chain.MaxDepth + 1} levels");
        sb.AppendLine($"**Total Knowledge Nodes**: {chain.TotalNodes}");
        sb.AppendLine();

        // Abstract - explain target node and derivation chain
        sb.AppendLine("## Abstract");
        sb.AppendLine();
        sb.AppendLine($"This paper presents the derivation chain for **{chain.TargetNode.CoreDescription}** (ID: `{chain.TargetNode.Id}`), ");
        sb.AppendLine($"a {chain.TargetNode.NodeType} in the knowledge graph.");
        sb.AppendLine();
        sb.AppendLine($"{chain.TargetNode.DetailedDescription}");
        sb.AppendLine();

        // Build derivation summary
        var foundationNodes = chain.Levels.Where(l => l.Depth == chain.MaxDepth).SelectMany(l => l.Nodes).ToList();
        if (foundationNodes.Count > 0)
        {
            sb.AppendLine("### Derivation Path");
            sb.AppendLine();
            sb.AppendLine("This knowledge is derived through the following reasoning chain:");
            sb.AppendLine();

            // Show derivation path from foundation to target
            foreach (var level in chain.Levels.AsEnumerable().Reverse())
            {
                var levelName = level.Depth == chain.MaxDepth ? "Foundation" :
                                level.Depth == 0 ? "Target" :
                                $"Level {chain.MaxDepth - level.Depth}";
                var nodeNames = string.Join(", ", level.Nodes.Select(n => n.CoreDescription));
                sb.AppendLine($"- **{levelName}**: {nodeNames}");
            }
            sb.AppendLine();
        }

        // Table of Contents
        sb.AppendLine("## Table of Contents");
        sb.AppendLine();
        var sectionNum = 1;
        foreach (var level in chain.Levels.AsEnumerable().Reverse())
        {
            foreach (var node in level.Nodes)
            {
                sb.AppendLine($"{sectionNum}. [{node.CoreDescription}](#{ToAnchor(node.Id)})");
                sectionNum++;
            }
        }
        sb.AppendLine();

        // Knowledge Nodes - from foundation to target
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Knowledge Nodes");
        sb.AppendLine();

        sectionNum = 1;
        foreach (var level in chain.Levels.AsEnumerable().Reverse())
        {
            var levelLabel = level.Depth == chain.MaxDepth ? "Foundation" :
                             level.Depth == 0 ? "Conclusion" :
                             $"Derivation Level {chain.MaxDepth - level.Depth}";

            sb.AppendLine($"### {levelLabel}");
            sb.AppendLine();

            foreach (var node in level.Nodes)
            {
                // Node header
                sb.AppendLine($"#### {sectionNum}. {node.CoreDescription} {{#{ToAnchor(node.Id)}}}");
                sb.AppendLine();

                // Basic info table
                sb.AppendLine("| Property | Value |");
                sb.AppendLine("|----------|-------|");
                sb.AppendLine($"| **ID** | `{node.Id}` |");
                sb.AppendLine($"| **Type** | {node.NodeType} |");
                sb.AppendLine();

                // Core Description
                sb.AppendLine("**Core Description**");
                sb.AppendLine();
                sb.AppendLine($"> {node.CoreDescription}");
                sb.AppendLine();

                // Detailed Description
                if (!string.IsNullOrWhiteSpace(node.DetailedDescription))
                {
                    sb.AppendLine("**Detailed Description**");
                    sb.AppendLine();
                    sb.AppendLine(node.DetailedDescription);
                    sb.AppendLine();
                }

                // Proof
                if (!string.IsNullOrWhiteSpace(node.Proof))
                {
                    sb.AppendLine("**Proof**");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(node.Proof);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                // Dependencies
                if (node.DependsOn.Count > 0)
                {
                    sb.AppendLine("**Depends On**");
                    sb.AppendLine();
                    foreach (var depId in node.DependsOn)
                    {
                        var depNode = chain.Chain.FirstOrDefault(n => n.Id == depId);
                        if (depNode != null)
                        {
                            sb.AppendLine($"- [{depNode.CoreDescription}](#{ToAnchor(depId)}) (`{depId}`)");
                        }
                        else
                        {
                            sb.AppendLine($"- `{depId}`");
                        }
                    }
                    sb.AppendLine();
                }

                // Resource URI
                if (!string.IsNullOrWhiteSpace(node.ResourceUri))
                {
                    sb.AppendLine("**Resources**");
                    sb.AppendLine();
                    sb.AppendLine($"- [Download Resource Files]({node.ResourceUri})");
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine();
                sectionNum++;
            }
        }

        // References section
        sb.AppendLine("## References");
        sb.AppendLine();
        var refNum = 1;
        foreach (var node in chain.Chain.Where(n => !string.IsNullOrWhiteSpace(n.ResourceUri)))
        {
            sb.AppendLine($"[{refNum}] **{node.CoreDescription}** (`{node.Id}`)");
            sb.AppendLine($"    - {node.ResourceUri}");
            sb.AppendLine();
            refNum++;
        }
        if (refNum == 1)
        {
            sb.AppendLine("*No external resources attached to this knowledge chain.*");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static string FormatFullPaper(KnowledgeSnapshot snapshot)
    {
        var sb = new StringBuilder();

        // Find root nodes (no dependencies - foundation knowledge)
        var rootNodes = snapshot.KnowledgeNodes.Where(n => n.DependsOn.Count == 0).ToList();

        // Find leaf nodes (no nodes depend on them - final conclusions)
        var nodeIdsWithDependents = snapshot.Edges.Select(e => e.ToId).ToHashSet();
        var leafNodes = snapshot.KnowledgeNodes.Where(n => !nodeIdsWithDependents.Contains(n.Id)).ToList();

        // Title
        sb.AppendLine("# Knowledge Graph Research Paper");
        sb.AppendLine();
        sb.AppendLine($"**Session**: {snapshot.SessionId}");
        sb.AppendLine($"**Generated**: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Total Knowledge Nodes**: {snapshot.NodeCount}");
        sb.AppendLine($"**Total Inference Relationships**: {snapshot.EdgeCount}");
        sb.AppendLine();

        // Abstract
        sb.AppendLine("## Abstract");
        sb.AppendLine();
        sb.AppendLine("This paper presents a comprehensive knowledge graph containing interconnected scientific knowledge. ");
        sb.AppendLine($"The graph consists of {snapshot.NodeCount} knowledge nodes connected by {snapshot.EdgeCount} inference relationships, ");
        sb.AppendLine($"with {rootNodes.Count} foundational axioms/definitions and {leafNodes.Count} derived conclusions.");
        sb.AppendLine();

        // Table of Contents by Type
        sb.AppendLine("## Knowledge Overview");
        sb.AppendLine();
        var nodesByType = snapshot.KnowledgeNodes.GroupBy(n => n.NodeType).OrderBy(g => g.Key);
        foreach (var group in nodesByType)
        {
            sb.AppendLine($"### {group.Key} ({group.Count()})");
            sb.AppendLine();
            foreach (var node in group)
            {
                sb.AppendLine($"- [{node.CoreDescription}](#{ToAnchor(node.Id)})");
            }
            sb.AppendLine();
        }

        // Build topological order for proper presentation
        var sortedNodes = TopologicalSort(snapshot);

        // Content sections
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Detailed Knowledge");
        sb.AppendLine();

        var sectionNum = 1;
        foreach (var node in sortedNodes)
        {
            sb.AppendLine($"### {sectionNum}. {node.CoreDescription} {{#{ToAnchor(node.Id)}}}");
            sb.AppendLine();
            sb.AppendLine($"**Type**: {node.NodeType}");
            sb.AppendLine($"**ID**: `{node.Id}`");
            sb.AppendLine($"**Created**: {node.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // Core Description
            sb.AppendLine("#### Summary");
            sb.AppendLine();
            sb.AppendLine(node.CoreDescription);
            sb.AppendLine();

            // Detailed Description
            sb.AppendLine("#### Detailed Description");
            sb.AppendLine();
            sb.AppendLine(node.DetailedDescription);
            sb.AppendLine();

            // Proof
            if (!string.IsNullOrWhiteSpace(node.Proof))
            {
                sb.AppendLine("#### Proof");
                sb.AppendLine();
                sb.AppendLine(node.Proof);
                sb.AppendLine();
            }

            // Dependencies
            if (node.DependsOn.Count > 0)
            {
                sb.AppendLine("#### Based On");
                sb.AppendLine();
                foreach (var depId in node.DependsOn)
                {
                    var depNode = snapshot.KnowledgeNodes.FirstOrDefault(n => n.Id == depId);
                    if (depNode != null)
                    {
                        sb.AppendLine($"- [{depNode.CoreDescription}](#{ToAnchor(depId)})");
                    }
                }
                sb.AppendLine();
            }

            // Resource
            if (!string.IsNullOrWhiteSpace(node.ResourceUri))
            {
                sb.AppendLine("#### Resources");
                sb.AppendLine();
                sb.AppendLine($"- [Download Resources]({node.ResourceUri})");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
            sectionNum++;
        }

        // References
        sb.AppendLine("## References");
        sb.AppendLine();
        var refNum = 1;
        foreach (var node in snapshot.KnowledgeNodes.Where(n => !string.IsNullOrWhiteSpace(n.ResourceUri)))
        {
            sb.AppendLine($"[{refNum}] {node.CoreDescription}: {node.ResourceUri}");
            refNum++;
        }
        if (refNum == 1)
        {
            sb.AppendLine("*No external resources attached.*");
        }

        return sb.ToString();
    }

    private static List<KnowledgeNode> TopologicalSort(KnowledgeSnapshot snapshot)
    {
        var result = new List<KnowledgeNode>();
        var visited = new HashSet<string>();
        var nodeMap = snapshot.KnowledgeNodes.ToDictionary(n => n.Id);

        void Visit(string nodeId)
        {
            if (visited.Contains(nodeId)) return;
            visited.Add(nodeId);

            if (nodeMap.TryGetValue(nodeId, out var node))
            {
                // Visit dependencies first
                foreach (var depId in node.DependsOn)
                {
                    Visit(depId);
                }
                result.Add(node);
            }
        }

        foreach (var node in snapshot.KnowledgeNodes)
        {
            Visit(node.Id);
        }

        return result;
    }

    private static string ToAnchor(string id) => id.ToLowerInvariant().Replace(" ", "-");

    public Task<IGraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        return _store.GetNodeAsync(SessionId, nodeId, cancellationToken);
    }

    public async Task<bool> RemoveNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        // Check if node exists
        if (!await _store.NodeExistsAsync(SessionId, nodeId, cancellationToken))
        {
            return false;
        }

        // Try to get knowledge node to check for S3 file (only KnowledgeNode has ResourceUri)
        var knowledgeNode = await _store.GetKnowledgeNodeAsync(SessionId, nodeId, cancellationToken);
        if (knowledgeNode != null && !string.IsNullOrWhiteSpace(knowledgeNode.ResourceUri))
        {
            var folderName = knowledgeNode.ResourceFolderPath != null
                ? Path.GetFileName(knowledgeNode.ResourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : nodeId;
            var zipFileName = folderName + ".zip";
            try
            {
                await _fileStorage.DeleteAsync(SessionId, zipFileName, cancellationToken);
            }
            catch
            {
                // Ignore S3 delete failures - node deletion should still proceed
            }
        }

        await _store.RemoveEdgesForNodeAsync(SessionId, nodeId, cancellationToken);
        return await _store.RemoveNodeAsync(SessionId, nodeId, cancellationToken);
    }

    // ========== Plan Node Operations (FR-007) ==========

    public async Task<PlanNode> CreatePlanNodeAsync(
        string nodeId,
        string coreDescription,
        string detailedDescription,
        string? methodology = null,
        int sequentialOrder = 0,
        IEnumerable<string>? promotesNodeIds = null,
        IEnumerable<string>? dependsOnNodeIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailedDescription);

        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        // Check for duplicate
        if (await _dagValidator.NodeExistsAsync(SessionId, nodeId, cancellationToken))
        {
            throw new DuplicateNodeException(nodeId);
        }

        var dependsOnList = dependsOnNodeIds?.ToList() ?? [];
        var promotesList = promotesNodeIds?.ToList() ?? [];

        // Validate all dependencies exist
        foreach (var depId in dependsOnList.Concat(promotesList))
        {
            if (!await _dagValidator.NodeExistsAsync(SessionId, depId, cancellationToken))
            {
                throw new NodeNotFoundException(depId);
            }
        }

        // Validate no cycle would be created
        if (dependsOnList.Count > 0)
        {
            var isValid = await _dagValidator.ValidateNoCycleAsync(SessionId, nodeId, dependsOnList, cancellationToken);
            if (!isValid)
            {
                throw new CycleDetectedException(nodeId, dependsOnList[0]);
            }
        }

        var node = new PlanNode
        {
            Id = nodeId,
            SessionId = SessionId,
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            Methodology = methodology,
            SequentialOrder = sequentialOrder,
            Status = PlanNodeStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DependsOn = dependsOnList
        };

        await _store.AddPlanNodeAsync(node, cancellationToken);

        // Add DependsOn edges
        foreach (var depId in dependsOnList)
        {
            await _store.AddEdgeAsync(new KnowledgeEdge
            {
                FromId = nodeId,
                ToId = depId,
                CreatedAt = DateTimeOffset.UtcNow
            }, SessionId, cancellationToken);
        }

        // Add Promotes edges (plan node promotes knowledge/goal nodes)
        foreach (var promoteId in promotesList)
        {
            await _store.AddEdgeAsync(new KnowledgeEdge
            {
                FromId = nodeId,
                ToId = promoteId,
                RelationshipType = RelationshipType.Promotes,
                CreatedAt = DateTimeOffset.UtcNow
            }, SessionId, cancellationToken);
        }

        return node;
    }

    public async Task<PlanNode> UpdatePlanNodeStatusAsync(
        string nodeId,
        PlanNodeStatus newStatus,
        string? progressText = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        var node = await _store.GetPlanNodeAsync(SessionId, nodeId, cancellationToken);
        if (node == null)
        {
            // Check if node exists as KnowledgeNode - throw InvalidStateTransition if so
            var knowledgeNode = await _store.GetKnowledgeNodeAsync(SessionId, nodeId, cancellationToken);
            if (knowledgeNode != null)
            {
                throw new GraphOperationException(
                    GraphErrorCode.InvalidStateTransition,
                    $"Node '{nodeId}' is a KnowledgeNode, not a PlanNode. Cannot update plan status.");
            }
            throw new NodeNotFoundException(nodeId);
        }

        // Validate state transition per FR-007
        var currentStatus = node.Status;
        var validTransition = (currentStatus, newStatus) switch
        {
            (PlanNodeStatus.Pending, PlanNodeStatus.Active) => true,
            (PlanNodeStatus.Active, PlanNodeStatus.Completed) => true,
            (PlanNodeStatus.Active, PlanNodeStatus.Pending) => true, // Re-planning
            (PlanNodeStatus.Completed, _) => false, // Cannot transition from Completed
            (var from, var to) when from == to => true, // Same status is OK
            _ => false
        };

        if (!validTransition)
        {
            throw GraphOperationException.InvalidStateTransition(nodeId, currentStatus, newStatus);
        }

        var updated = new PlanNode
        {
            Id = node.Id,
            SessionId = node.SessionId,
            Owner = node.Owner,
            CoreDescription = node.CoreDescription,
            DetailedDescription = node.DetailedDescription,
            CreatedAt = node.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            DependsOn = node.DependsOn,
            Status = newStatus,
            ProgressText = progressText ?? node.ProgressText,
            Methodology = node.Methodology,
            SequentialOrder = node.SequentialOrder,
            PivotStatus = node.PivotStatus,
            CancelledAt = node.CancelledAt,
            CancelledByPivotId = node.CancelledByPivotId,
            DirectionContext = node.DirectionContext
        };

        await _store.UpdatePlanNodeAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<PlanNode>> GetPlanNodesAsync(CancellationToken cancellationToken = default)
    {
        var planNodes = await _store.GetAllPlanNodesAsync(SessionId, cancellationToken);
        return planNodes
            .OrderBy(n => n.SequentialOrder)
            .ThenBy(n => n.Id)
            .ToList();
    }

    // ========== Knowledge Node Operations (FR-008) ==========

    public async Task<KnowledgeNode> CreateKnowledgeNodeAsync(
        string nodeId,
        KnowledgeNodeType nodeType,
        string coreDescription,
        string detailedDescription,
        string? derivationProcess = null,
        IEnumerable<string>? references = null,
        string? proof = null,
        string? motivatedByPlanNodeId = null,
        IEnumerable<string>? dependsOnNodeIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailedDescription);

        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        // Check for duplicate
        if (await _dagValidator.NodeExistsAsync(SessionId, nodeId, cancellationToken))
        {
            throw new DuplicateNodeException(nodeId);
        }

        var dependsOnList = dependsOnNodeIds?.ToList() ?? [];
        var referencesList = references?.ToList() ?? [];

        // Validate motivated-by plan node exists if specified
        if (!string.IsNullOrWhiteSpace(motivatedByPlanNodeId))
        {
            if (!await _dagValidator.NodeExistsAsync(SessionId, motivatedByPlanNodeId, cancellationToken))
            {
                throw new NodeNotFoundException(motivatedByPlanNodeId);
            }
        }

        // Validate all dependencies exist
        foreach (var depId in dependsOnList)
        {
            if (!await _dagValidator.NodeExistsAsync(SessionId, depId, cancellationToken))
            {
                throw new NodeNotFoundException(depId);
            }
        }

        // Validate no cycle would be created
        if (dependsOnList.Count > 0)
        {
            var isValid = await _dagValidator.ValidateNoCycleAsync(SessionId, nodeId, dependsOnList, cancellationToken);
            if (!isValid)
            {
                throw new CycleDetectedException(nodeId, dependsOnList[0]);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var node = new KnowledgeNode
        {
            Id = nodeId,
            SessionId = SessionId,
            NodeType = nodeType,
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            DerivationProcess = derivationProcess,
            References = referencesList,
            Proof = proof,
            CreatedAt = now,
            UpdatedAt = now,
            DependsOn = dependsOnList
        };

        await _store.AddKnowledgeNodeAsync(node, cancellationToken);

        // Add DependsOn edges
        foreach (var depId in dependsOnList)
        {
            await _store.AddEdgeAsync(new KnowledgeEdge
            {
                FromId = nodeId,
                ToId = depId,
                RelationshipType = RelationshipType.DependsOn,
                CreatedAt = DateTimeOffset.UtcNow
            }, SessionId, cancellationToken);
        }

        // Add MotivatedBy edge if specified
        if (!string.IsNullOrWhiteSpace(motivatedByPlanNodeId))
        {
            await _store.AddEdgeAsync(new KnowledgeEdge
            {
                FromId = nodeId,
                ToId = motivatedByPlanNodeId,
                RelationshipType = RelationshipType.MotivatedBy,
                CreatedAt = DateTimeOffset.UtcNow
            }, SessionId, cancellationToken);
        }

        return node;
    }

    public async Task<IReadOnlyList<KnowledgeNode>> GetKnowledgeNodesAsync(CancellationToken cancellationToken = default)
    {
        var knowledgeNodes = await _store.GetAllKnowledgeNodesAsync(SessionId, cancellationToken);
        return knowledgeNodes
            .OrderBy(n => n.Id)
            .ToList();
    }

    public async Task LinkKnowledgeToPlanAsync(
        string knowledgeNodeId,
        string planNodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(knowledgeNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planNodeId);

        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        // Validate knowledge node
        var knowledgeNode = await _store.GetKnowledgeNodeAsync(SessionId, knowledgeNodeId, cancellationToken);
        if (knowledgeNode == null)
        {
            // Check if it's a PlanNode instead
            var isPlanNode = await _store.GetPlanNodeAsync(SessionId, knowledgeNodeId, cancellationToken);
            if (isPlanNode != null)
            {
                throw new InvalidOperationException(
                    $"Node '{knowledgeNodeId}' is a PlanNode, but a KnowledgeNode was expected.");
            }
            throw new NodeNotFoundException(knowledgeNodeId);
        }

        // Validate plan node
        var planNode = await _store.GetPlanNodeAsync(SessionId, planNodeId, cancellationToken);
        if (planNode == null)
        {
            // Check if it's a KnowledgeNode instead
            var isKnowledgeNode = await _store.GetKnowledgeNodeAsync(SessionId, planNodeId, cancellationToken);
            if (isKnowledgeNode != null)
            {
                throw new InvalidOperationException(
                    $"Node '{planNodeId}' is a KnowledgeNode, but a PlanNode was expected.");
            }
            throw new NodeNotFoundException(planNodeId);
        }

        await _store.AddEdgeAsync(new KnowledgeEdge
        {
            FromId = knowledgeNodeId,
            ToId = planNodeId,
            RelationshipType = RelationshipType.MotivatedBy,
            CreatedAt = DateTimeOffset.UtcNow
        }, SessionId, cancellationToken);
    }

    // ========== Node Explanation (US4) ==========

    public async Task<NodeExplanation> ExplainNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var node = await _store.GetNodeAsync(SessionId, nodeId, cancellationToken);
        if (node == null)
        {
            throw new NodeNotFoundException(nodeId);
        }

        var snapshot = await GetKnowledgeSnapshotAsync(cancellationToken);
        var explanationService = new NodeExplanationService();

        return node switch
        {
            PlanNode planNode => await explanationService.ExplainPlanNodeAsync(planNode, snapshot, cancellationToken),
            KnowledgeNode knowledgeNode => await explanationService.ExplainKnowledgeNodeAsync(knowledgeNode, snapshot, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown node type for node '{nodeId}'")
        };
    }

    // ========== Pivot Operations (US6) ==========

    public async Task<bool> CanDeletePlanNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        var node = await _store.GetPlanNodeAsync(SessionId, nodeId, cancellationToken);
        if (node == null)
        {
            return false; // Node doesn't exist or is not a plan node
        }

        // Check if any knowledge nodes are motivated by this plan
        var motivatedKnowledge = await GetMotivatedKnowledgeNodeIdsAsync(nodeId, cancellationToken);
        return motivatedKnowledge.Count == 0;
    }

    public async Task<IReadOnlyList<string>> GetMotivatedKnowledgeNodeIdsAsync(string planNodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planNodeId);

        var edges = await _store.GetAllEdgesAsync(SessionId, cancellationToken);
        return edges
            .Where(e => e.ToId == planNodeId && e.RelationshipType == RelationshipType.MotivatedBy)
            .Select(e => e.FromId)
            .Distinct()
            .ToList();
    }

    // In-memory storage for pivot snapshots (per session)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<PivotSnapshot>> _pivotSnapshots = new();
    private const int MaxPivotSnapshots = 5;

    public async Task<string> CreatePivotSnapshotAsync(string pivotReason, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(SessionId, cancellationToken);

        var snapshot = await GetKnowledgeSnapshotAsync(cancellationToken);
        var snapshotId = $"pivot-{Guid.NewGuid():N}";

        var pivotSnapshot = new PivotSnapshot
        {
            Id = snapshotId,
            SessionId = SessionId,
            CreatedAt = DateTimeOffset.UtcNow,
            Reason = pivotReason ?? "No reason provided",
            Snapshot = snapshot
        };

        var snapshots = _pivotSnapshots.GetOrAdd(SessionId, _ => new List<PivotSnapshot>());
        lock (snapshots)
        {
            snapshots.Add(pivotSnapshot);
            // Keep only last MaxPivotSnapshots
            while (snapshots.Count > MaxPivotSnapshots)
            {
                snapshots.RemoveAt(0);
            }
        }

        return snapshotId;
    }

    public Task<IReadOnlyList<PivotSnapshot>> GetPivotSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        if (_pivotSnapshots.TryGetValue(SessionId, out var snapshots))
        {
            lock (snapshots)
            {
                return Task.FromResult<IReadOnlyList<PivotSnapshot>>(snapshots.ToList());
            }
        }

        return Task.FromResult<IReadOnlyList<PivotSnapshot>>(Array.Empty<PivotSnapshot>());
    }

    // ========== Summary Generation (US7) ==========

    public async Task<SessionSummary> GenerateSessionSummaryAsync(CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(cancellationToken);
        var planNodes = await GetPlanNodesAsync(cancellationToken);
        var knowledgeNodes = await GetKnowledgeNodesAsync(cancellationToken);

        var summaryService = new SummaryGenerationService();
        return await summaryService.GenerateSessionSummaryAsync(session, planNodes, knowledgeNodes, cancellationToken);
    }

    public async Task<DagSummary> GenerateFullDagSummaryAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetKnowledgeSnapshotAsync(cancellationToken);

        var summaryService = new SummaryGenerationService();
        return await summaryService.GenerateFullDagSummaryAsync(snapshot, cancellationToken);
    }

    // ========== Global Migration Operations ==========

    public Task<IReadOnlyList<KnowledgeNode>> GetAllKnowledgeNodesGlobalAsync(CancellationToken cancellationToken = default)
    {
        return _store.GetAllKnowledgeNodesGlobalAsync(cancellationToken);
    }

    public Task<IReadOnlyList<(KnowledgeEdge Edge, string SessionId)>> GetAllEdgesGlobalAsync(CancellationToken cancellationToken = default)
    {
        return _store.GetAllEdgesGlobalAsync(cancellationToken);
    }
}

/// <summary>
/// Factory for creating session-scoped knowledge graph clients.
/// Shares a single GraphOperationLock instance across all clients for thread-safe session operations.
/// </summary>
internal sealed class KnowledgeGraphClientFactory : IKnowledgeGraphClientFactory, IDisposable
{
    private readonly IKnowledgeGraphStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly GraphOperationLock _operationLock = new();

    public KnowledgeGraphClientFactory(IKnowledgeGraphStore store, IFileStorage fileStorage)
    {
        _store = store;
        _fileStorage = fileStorage;
    }

    public IKnowledgeGraphClient CreateClient(string sessionId)
    {
        return new KnowledgeGraphClient(sessionId, _store, _fileStorage, _operationLock);
    }

    public async Task<IKnowledgeGraphClient> StartSessionAsync(string? sessionId = null, CancellationToken cancellationToken = default)
    {
        var session = await _store.CreateSessionAsync(sessionId, cancellationToken);
        return new KnowledgeGraphClient(session.Id, _store, _fileStorage, _operationLock);
    }

    public void Dispose()
    {
        _operationLock.Dispose();
    }
}
