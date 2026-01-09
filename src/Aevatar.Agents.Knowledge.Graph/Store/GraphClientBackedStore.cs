using Aevatar.Agents.Knowledge.Graph.Models;
using Aevatar.Agents.Persistence.Graph.Abstractions;
using Aevatar.Agents.Persistence.Graph.Core.Semantic;

namespace Aevatar.Agents.Knowledge.Graph.Store;

/// <summary>
/// Implementation of IKnowledgeGraphStore backed by IGraphClient.
/// This enables using Neo4j, InMemory, or any other IGraphClient backend.
/// Session isolation is achieved by using composite IDs: {sessionId}:{nodeId}
/// </summary>
internal sealed class GraphClientBackedStore : IKnowledgeGraphStore
{
    private const string EdgeType = "DEPENDS_ON";
    private const string PropSessionId = "sessionId";
    private const string PropNodeId = "nodeId";
    private const string PropNodeType = "nodeType";
    private const string PropCoreDescription = "coreDescription";
    private const string PropDetailedDescription = "detailedDescription";
    private const string PropProof = "proof";
    private const string PropResourceUri = "resourceUri";
    private const string PropTimestamp = "timestamp";
    private const string PropDependsOn = "dependsOn";
    private const string PropCreatedAt = "createdAt";

    private readonly IGraphClient _graphClient;

    public GraphClientBackedStore(IGraphClient graphClient)
    {
        _graphClient = graphClient;
    }

    /// <summary>
    /// Creates a composite key for storage: {sessionId}:{nodeId}
    /// </summary>
    private static string CompositeId(string sessionId, string nodeId) => $"{sessionId}:{nodeId}";

    /// <summary>
    /// Gets the node type string to use in IGraphClient.
    /// Using the KnowledgeNodeType enum name as the graph node type.
    /// </summary>
    private static string GetNodeTypeString(KnowledgeNodeType nodeType) => nodeType.ToString();

    public async Task AddNodeAsync(KnowledgeNode node, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(node.SessionId, node.Id);
        var nodeTypeStr = GetNodeTypeString(node.NodeType);

        var properties = new Dictionary<string, Value>
        {
            ["id"] = new StringValue(compositeId),
            [PropSessionId] = new StringValue(node.SessionId),
            [PropNodeId] = new StringValue(node.Id),
            [PropNodeType] = new StringValue(nodeTypeStr),
            [PropCoreDescription] = new StringValue(node.CoreDescription),
            [PropDetailedDescription] = new StringValue(node.DetailedDescription),
            [PropTimestamp] = new StringValue(node.Timestamp.ToString("O")),
            [PropDependsOn] = new StringValue(string.Join(",", node.DependsOn))
        };

        if (node.Proof != null)
        {
            properties[PropProof] = new StringValue(node.Proof);
        }

        // Only store the HTTPS URL, not the local folder path
        if (node.ResourceUri != null)
        {
            properties[PropResourceUri] = new StringValue(node.ResourceUri);
        }

        await _graphClient.WriteAsync(nodeTypeStr, properties);
    }

    public async Task<KnowledgeNode?> GetNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var graphNode = await _graphClient.ReadAsync(new NodeId(compositeId));
        return graphNode == null ? null : ToKnowledgeNode(graphNode);
    }

    public async Task<IReadOnlyList<KnowledgeNode>> GetAllNodesAsync(string sessionId, CancellationToken cancellationToken)
    {
        // Query all node types and filter by session
        var allNodes = new List<KnowledgeNode>();

        foreach (var nodeType in Enum.GetValues<KnowledgeNodeType>())
        {
            var nodeTypeStr = GetNodeTypeString(nodeType);
            var nodes = await _graphClient.QueryAsync(new NodeQuery { Type = nodeTypeStr });

            foreach (var node in nodes)
            {
                if (node.Properties.TryGetValue(PropSessionId, out var sessionVal) &&
                    sessionVal is StringValue sv && sv.Data == sessionId)
                {
                    allNodes.Add(ToKnowledgeNode(node));
                }
            }
        }

        return allNodes;
    }

    public async Task<bool> RemoveNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var exists = await NodeExistsAsync(sessionId, nodeId, cancellationToken);
        if (!exists) return false;

        await _graphClient.DeleteAsync(new NodeId(compositeId));
        return true;
    }

    public async Task<bool> NodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var node = await _graphClient.ReadAsync(new NodeId(compositeId));
        return node != null;
    }

    private const string PropFromNodeId = "fromNodeId";
    private const string PropToNodeId = "toNodeId";

    public async Task AddEdgeAsync(KnowledgeEdge edge, string sessionId, CancellationToken cancellationToken)
    {
        var fromCompositeId = CompositeId(sessionId, edge.FromId);
        var toCompositeId = CompositeId(sessionId, edge.ToId);

        var properties = new Dictionary<string, Value>
        {
            [PropSessionId] = new StringValue(sessionId),
            [PropCreatedAt] = new StringValue(edge.CreatedAt.ToString("O")),
            // Store business node IDs directly in edge properties for reliable retrieval
            [PropFromNodeId] = new StringValue(edge.FromId),
            [PropToNodeId] = new StringValue(edge.ToId)
        };

        await _graphClient.WriteAsync(
            EdgeType,
            new NodeId(fromCompositeId),
            new NodeId(toCompositeId),
            properties);
    }

    public async Task<IReadOnlyList<KnowledgeEdge>> GetAllEdgesAsync(string sessionId, CancellationToken cancellationToken)
    {
        var edges = await _graphClient.QueryAsync(new EdgeQuery { Type = EdgeType });

        return edges
            .Where(e => e.Properties.TryGetValue(PropSessionId, out var sv) &&
                        sv is StringValue sessionVal && sessionVal.Data == sessionId)
            .Select(e =>
            {
                var createdAt = DateTimeOffset.UtcNow;
                if (e.Properties.TryGetValue(PropCreatedAt, out var createdAtVal) && createdAtVal is StringValue sv)
                {
                    createdAt = DateTimeOffset.Parse(sv.Data);
                }

                // Read node IDs directly from edge properties (stored when edge was created)
                // Fall back to parsing From/To values for backwards compatibility
                string fromId, toId;

                if (e.Properties.TryGetValue(PropFromNodeId, out var fromVal) && fromVal is StringValue fromStr)
                {
                    fromId = fromStr.Data;
                }
                else
                {
                    var fromParts = e.From.Value.Split(':', 2);
                    fromId = fromParts.Length > 1 ? fromParts[1] : e.From.Value;
                }

                if (e.Properties.TryGetValue(PropToNodeId, out var toVal) && toVal is StringValue toStr)
                {
                    toId = toStr.Data;
                }
                else
                {
                    var toParts = e.To.Value.Split(':', 2);
                    toId = toParts.Length > 1 ? toParts[1] : e.To.Value;
                }

                return new KnowledgeEdge
                {
                    FromId = fromId,
                    ToId = toId,
                    CreatedAt = createdAt
                };
            })
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetDependenciesAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var allEdges = await _graphClient.QueryAsync(new EdgeQuery { Type = EdgeType });

        // Edge direction: node -[DEPENDS_ON]-> dependency (From: node, To: dependency)
        // Read node IDs from edge properties
        return allEdges
            .Where(e => e.Properties.TryGetValue(PropSessionId, out var sv) &&
                        sv is StringValue sessionVal && sessionVal.Data == sessionId &&
                        e.Properties.TryGetValue(PropFromNodeId, out var fromVal) &&
                        fromVal is StringValue fromStr && fromStr.Data == nodeId)
            .Select(e =>
            {
                if (e.Properties.TryGetValue(PropToNodeId, out var toVal) && toVal is StringValue toStr)
                {
                    return toStr.Data;
                }
                var toParts = e.To.Value.Split(':', 2);
                return toParts.Length > 1 ? toParts[1] : e.To.Value;
            })
            .ToList();
    }

    public async Task RemoveEdgesForNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var allEdges = await _graphClient.QueryAsync(new EdgeQuery { Type = EdgeType });

        // Find edges where this node is either the source or target
        var edgesToRemove = allEdges
            .Where(e => e.Properties.TryGetValue(PropSessionId, out var sv) &&
                        sv is StringValue sessionVal && sessionVal.Data == sessionId &&
                        ((e.Properties.TryGetValue(PropFromNodeId, out var fromVal) &&
                          fromVal is StringValue fromStr && fromStr.Data == nodeId) ||
                         (e.Properties.TryGetValue(PropToNodeId, out var toVal) &&
                          toVal is StringValue toStr && toStr.Data == nodeId)))
            .ToList();

        foreach (var edge in edgesToRemove)
        {
            await _graphClient.DeleteAsync(edge.Id);
        }
    }

    private static KnowledgeNode ToKnowledgeNode(GraphNode graphNode)
    {
        var props = graphNode.Properties;

        var sessionId = props.TryGetValue(PropSessionId, out var sessionVal) && sessionVal is StringValue sessionSv
            ? sessionSv.Data
            : "";

        var nodeId = props.TryGetValue(PropNodeId, out var nodeIdVal) && nodeIdVal is StringValue nodeIdSv
            ? nodeIdSv.Data
            : graphNode.Id.Value;

        var nodeTypeStr = props.TryGetValue(PropNodeType, out var nodeTypeVal) && nodeTypeVal is StringValue nodeTypeSv
            ? nodeTypeSv.Data
            : "Generic";
        var nodeType = Enum.TryParse<KnowledgeNodeType>(nodeTypeStr, out var nt) ? nt : KnowledgeNodeType.Generic;

        var coreDescription = props.TryGetValue(PropCoreDescription, out var coreVal) && coreVal is StringValue coreSv
            ? coreSv.Data
            : "";

        var detailedDescription = props.TryGetValue(PropDetailedDescription, out var detailVal) && detailVal is StringValue detailSv
            ? detailSv.Data
            : "";

        var proof = props.TryGetValue(PropProof, out var proofVal) && proofVal is StringValue proofSv
            ? proofSv.Data
            : null;

        // ResourceFolderPath is not stored in Neo4j, only the HTTPS URL is stored
        var resourceUri = props.TryGetValue(PropResourceUri, out var uriVal) && uriVal is StringValue uriSv
            ? uriSv.Data
            : null;

        var timestamp = props.TryGetValue(PropTimestamp, out var timestampVal) && timestampVal is StringValue timestampSv
            ? DateTimeOffset.Parse(timestampSv.Data)
            : DateTimeOffset.UtcNow;

        var dependsOnStr = props.TryGetValue(PropDependsOn, out var depsVal) && depsVal is StringValue depsSv
            ? depsSv.Data
            : "";
        var dependsOn = string.IsNullOrEmpty(dependsOnStr)
            ? Array.Empty<string>()
            : dependsOnStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        return new KnowledgeNode
        {
            Id = nodeId,
            SessionId = sessionId,
            NodeType = nodeType,
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            Proof = proof,
            ResourceFolderPath = null, // Not stored in Neo4j
            ResourceUri = resourceUri,
            Timestamp = timestamp,
            DependsOn = dependsOn
        };
    }
}
