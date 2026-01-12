using System.Text;
using Aevatar.Agents.Knowledge.Graph.Exceptions;
using Aevatar.Agents.Knowledge.Graph.Models;
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

    public string SessionId { get; }

    public KnowledgeGraphClient(string sessionId, IKnowledgeGraphStore store, IFileStorage fileStorage)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _store = store;
        _fileStorage = fileStorage;
        _dagValidator = new DagValidator(store);
    }

    public async Task<KnowledgeNode> AddNodeAsync(
        string nodeId,
        KnowledgeNodeType nodeType,
        string coreDescription,
        string detailedDescription,
        KnowledgeNodeKind kind = KnowledgeNodeKind.Knowledge,
        string? owner = null,
        string? proof = null,
        string? resourceFolderPath = null,
        IEnumerable<string>? dependsOn = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(coreDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailedDescription);

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

        var node = new KnowledgeNode
        {
            Id = nodeId,
            SessionId = SessionId,
            NodeType = nodeType,
            Kind = kind,
            Owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim(),
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            Proof = proof,
            ResourceFolderPath = resourceFolderPath,
            ResourceUri = resourceUri,
            Timestamp = DateTimeOffset.UtcNow,
            DependsOn = dependsOnList
        };

        await _store.AddNodeAsync(node, cancellationToken);

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
        KnowledgeNodeKind? kind = null,
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

        var coreIn = (coreDescription ?? string.Empty).Trim();
        var detailIn = (detailedDescription ?? string.Empty).Trim();
        var proofIn = string.IsNullOrWhiteSpace(proof) ? null : proof.Trim();
        var ownerIn = (owner ?? string.Empty).Trim();
        var directionIn = string.IsNullOrWhiteSpace(directionContext) ? null : directionContext.Trim();
        var cancelledByIn = string.IsNullOrWhiteSpace(cancelledByPivotId) ? null : cancelledByPivotId.Trim();

        var now = DateTimeOffset.UtcNow;
        var existing = await _store.GetNodeAsync(SessionId, nodeId, cancellationToken);

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
                Kind = kind ?? KnowledgeNodeKind.Knowledge,
                Owner = ownerIn.Length == 0 ? null : ownerIn,
                CoreDescription = core,
                DetailedDescription = detail,
                Proof = proofIn,
                ResourceFolderPath = resourceFolderPath,
                ResourceUri = resourceUri,
                Timestamp = now,
                DependsOn = depsList,
                PivotStatus = pivotStatus ?? PivotNodeStatus.Active,
                CancelledAt = cancelledAt,
                CancelledByPivotId = cancelledByIn,
                DirectionContext = directionIn
            };

            await _store.AddNodeAsync(created, cancellationToken);
            return created;
        }

        // Update: empty inputs keep existing values.
        // NodeType: treat Generic as "unspecified" (do not downgrade).
        var mergedType = nodeType != KnowledgeNodeType.Generic ? nodeType : existing.NodeType;
        var mergedKind = kind ?? existing.Kind;
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
            Kind = mergedKind,
            Owner = mergedOwner,
            CoreDescription = mergedCore,
            DetailedDescription = mergedDetail,
            Proof = mergedProof,
            ResourceFolderPath = resourceFolderPath,
            ResourceUri = mergedResourceUri,
            Timestamp = now,
            DependsOn = depsList,
            Attestations = existing.Attestations,
            PivotStatus = mergedPivotStatus,
            CancelledAt = mergedCancelledAt,
            CancelledByPivotId = mergedCancelledByPivotId,
            DirectionContext = mergedDirectionContext
        };

        await _store.AddNodeAsync(updated, cancellationToken);
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

        var node = await _store.GetNodeAsync(SessionId, nodeId, cancellationToken);
        if (node == null)
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
        var updated = new KnowledgeNode
        {
            Id = node.Id,
            SessionId = node.SessionId,
            NodeType = node.NodeType,
            CoreDescription = node.CoreDescription,
            DetailedDescription = node.DetailedDescription,
            Proof = node.Proof,
            ResourceFolderPath = node.ResourceFolderPath,
            ResourceUri = node.ResourceUri,
            Timestamp = DateTimeOffset.UtcNow,
            Kind = node.Kind,
            Owner = node.Owner,
            DependsOn = mergedDeps,
            Attestations = node.Attestations,
            PivotStatus = node.PivotStatus,
            CancelledAt = node.CancelledAt,
            CancelledByPivotId = node.CancelledByPivotId,
            DirectionContext = node.DirectionContext
        };
        await _store.AddNodeAsync(updated, cancellationToken);
    }

    public async Task<KnowledgeSnapshot> GetKnowledgeSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var nodes = await _store.GetAllNodesAsync(SessionId, cancellationToken);
        var edges = await _store.GetAllEdgesAsync(SessionId, cancellationToken);

        return new KnowledgeSnapshot
        {
            SessionId = SessionId,
            Nodes = nodes.OrderBy(n => n.NodeType).ThenBy(n => n.Id).ToList(),
            Edges = edges.OrderBy(e => e.FromId).ThenBy(e => e.ToId).ToList()
        };
    }

    private async Task<KnowledgeChain> GetKnowledgeChainAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        var targetNode = await _store.GetNodeAsync(SessionId, nodeId, cancellationToken);
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

                var node = await _store.GetNodeAsync(SessionId, id, cancellationToken);
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
        var rootNodes = snapshot.Nodes.Where(n => n.DependsOn.Count == 0).ToList();

        // Find leaf nodes (no nodes depend on them - final conclusions)
        var nodeIdsWithDependents = snapshot.Edges.Select(e => e.ToId).ToHashSet();
        var leafNodes = snapshot.Nodes.Where(n => !nodeIdsWithDependents.Contains(n.Id)).ToList();

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
        var nodesByType = snapshot.Nodes.GroupBy(n => n.NodeType).OrderBy(g => g.Key);
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
            sb.AppendLine($"**Timestamp**: {node.Timestamp:yyyy-MM-dd HH:mm:ss}");
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
                    var depNode = snapshot.Nodes.FirstOrDefault(n => n.Id == depId);
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
        foreach (var node in snapshot.Nodes.Where(n => !string.IsNullOrWhiteSpace(n.ResourceUri)))
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
        var nodeMap = snapshot.Nodes.ToDictionary(n => n.Id);

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

        foreach (var node in snapshot.Nodes)
        {
            Visit(node.Id);
        }

        return result;
    }

    private static string ToAnchor(string id) => id.ToLowerInvariant().Replace(" ", "-");

    public Task<KnowledgeNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        return _store.GetNodeAsync(SessionId, nodeId, cancellationToken);
    }

    public async Task<bool> RemoveNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        // Get node to check for S3 file
        var node = await _store.GetNodeAsync(SessionId, nodeId, cancellationToken);
        if (node == null) return false;

        // Delete S3 file if exists (stored as .zip)
        if (!string.IsNullOrWhiteSpace(node.ResourceUri))
        {
            var folderName = node.ResourceFolderPath != null
                ? Path.GetFileName(node.ResourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
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
}

/// <summary>
/// Factory for creating session-scoped knowledge graph clients.
/// </summary>
internal sealed class KnowledgeGraphClientFactory : IKnowledgeGraphClientFactory
{
    private readonly IKnowledgeGraphStore _store;
    private readonly IFileStorage _fileStorage;

    public KnowledgeGraphClientFactory(IKnowledgeGraphStore store, IFileStorage fileStorage)
    {
        _store = store;
        _fileStorage = fileStorage;
    }

    public IKnowledgeGraphClient CreateClient(string sessionId)
    {
        return new KnowledgeGraphClient(sessionId, _store, _fileStorage);
    }
}
