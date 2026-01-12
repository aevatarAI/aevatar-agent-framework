using System.Text.Json;
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
    private const string PropOwner = "owner";
    private const string PropCoreDescription = "coreDescription";
    private const string PropDetailedDescription = "detailedDescription";
    private const string PropProof = "proof";
    private const string PropResourceUri = "resourceUri";
    private const string PropTimestamp = "timestamp";
    private const string PropDependsOn = "dependsOn";
    private const string PropNodeKind = "nodeKind";
    private const string PropNodeAttestationsJson = "nodeAttestationsJson";
    private const string PropCreatedAt = "createdAt";

    private readonly IGraphClient _graphClient;
    private static readonly JsonSerializerOptions AttestationsJsonOptions = new(JsonSerializerDefaults.Web);

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
            [PropNodeKind] = new StringValue(node.Kind.ToString()),
            [PropOwner] = new StringValue((node.Owner ?? string.Empty).Trim()),
            [PropCoreDescription] = new StringValue(node.CoreDescription),
            [PropDetailedDescription] = new StringValue(node.DetailedDescription),
            [PropTimestamp] = new StringValue(node.Timestamp.ToString("O")),
            [PropDependsOn] = new StringValue(string.Join(",", node.DependsOn)),
            [PropNodeAttestationsJson] = new StringValue(JsonSerializer.Serialize(node.Attestations, AttestationsJsonOptions))
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
        // Query all node types and filter by session.
        //
        // Notes:
        // - Neo4j writes use MERGE + SET n:`label`, which may accumulate multiple labels on a node over time.
        // - We dedupe by business node id to avoid returning duplicates across labels.
        var byId = new Dictionary<string, KnowledgeNode>(StringComparer.Ordinal);

        foreach (var nodeType in Enum.GetValues<KnowledgeNodeType>())
        {
            var nodeTypeStr = GetNodeTypeString(nodeType);
            var nodes = await _graphClient.QueryAsync(new NodeQuery { Type = nodeTypeStr });

            foreach (var node in nodes)
            {
                if (node.Properties.TryGetValue(PropSessionId, out var sessionVal) &&
                    sessionVal is StringValue sv && sv.Data == sessionId)
                {
                    var kn = ToKnowledgeNode(node);
                    byId[kn.Id] = kn;
                }
            }
        }

        return byId.Values.ToList();
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

        // Deterministic relationship id:
        // - Avoid duplicates when the same dependency edge is added multiple times.
        // - Works with both InMemory (UpsertEdge) and Neo4j (MERGE ... { id: $id }).
        var edgeId = $"{sessionId}:{edge.FromId}->{edge.ToId}:{EdgeType}";

        var properties = new Dictionary<string, Value>
        {
            ["id"] = new StringValue(edgeId),
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

        var list = edges
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

        // Dedupe by (fromId, toId) pair - callers treat these as semantic unique edges.
        var byPair = new Dictionary<string, KnowledgeEdge>(StringComparer.Ordinal);
        foreach (var e in list)
        {
            var key = $"{e.FromId}->{e.ToId}";
            if (!byPair.TryGetValue(key, out var existing) || e.CreatedAt < existing.CreatedAt)
            {
                byPair[key] = e;
            }
        }

        return byPair.Values.ToList();
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

        var kindStr = props.TryGetValue(PropNodeKind, out var kindVal) && kindVal is StringValue kindSv
            ? kindSv.Data
            : KnowledgeNodeKind.Knowledge.ToString();
        var kind = Enum.TryParse<KnowledgeNodeKind>(kindStr, out var nk) ? nk : KnowledgeNodeKind.Knowledge;

        var owner = props.TryGetValue(PropOwner, out var ownerVal) && ownerVal is StringValue ownerSv
            ? ownerSv.Data
            : null;
        owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();

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

        IReadOnlyList<KnowledgeAttestation> attestations = Array.Empty<KnowledgeAttestation>();
        if (props.TryGetValue(PropNodeAttestationsJson, out var attVal) && attVal is StringValue attSv &&
            !string.IsNullOrWhiteSpace(attSv.Data))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<KnowledgeAttestation>>(attSv.Data, AttestationsJsonOptions);
                attestations = (IReadOnlyList<KnowledgeAttestation>?)list ?? Array.Empty<KnowledgeAttestation>();
            }
            catch
            {
                // Best-effort: tolerate malformed JSON from legacy/backends.
                attestations = Array.Empty<KnowledgeAttestation>();
            }
        }

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
            Kind = kind,
            Owner = owner,
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            Proof = proof,
            ResourceFolderPath = null, // Not stored in Neo4j
            ResourceUri = resourceUri,
            Timestamp = timestamp,
            Attestations = attestations,
            DependsOn = dependsOn
        };
    }
}
