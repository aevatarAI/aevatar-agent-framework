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
    private const string SessionNodeType = "Session";
    private const string KnowledgeNodeGraphType = "KnowledgeNode";
    private const string PlanNodeGraphType = "PlanNode";

    // Common properties
    private const string PropSessionId = "sessionId";
    private const string PropNodeId = "nodeId";
    private const string PropOwner = "owner";
    private const string PropCoreDescription = "coreDescription";
    private const string PropDetailedDescription = "detailedDescription";
    private const string PropDependsOn = "dependsOn";
    private const string PropCreatedAt = "createdAt";
    private const string PropUpdatedAt = "updatedAt";

    // Session properties
    private const string PropStartedAt = "startedAt";
    private const string PropEndedAt = "endedAt";
    private const string PropStatus = "status";

    // KnowledgeNode-specific properties
    private const string PropNodeType = "nodeType";
    private const string PropProof = "proof";
    private const string PropResourceUri = "resourceUri";
    private const string PropTimestamp = "timestamp";
    private const string PropNodeAttestationsJson = "nodeAttestationsJson";
    private const string PropDerivationProcess = "derivationProcess";
    private const string PropReferencesJson = "referencesJson";
    private const string PropPivotStatus = "pivotStatus";
    private const string PropCancelledAt = "cancelledAt";
    private const string PropCancelledByPivotId = "cancelledByPivotId";
    private const string PropDirectionContext = "directionContext";

    // PlanNode-specific properties
    private const string PropPlanStatus = "planStatus";
    private const string PropProgressText = "progressText";
    private const string PropMethodology = "methodology";
    private const string PropSequentialOrder = "sequentialOrder";

    // Edge properties
    private const string PropFromNodeId = "fromNodeId";
    private const string PropToNodeId = "toNodeId";

    private readonly IGraphClient _graphClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public GraphClientBackedStore(IGraphClient graphClient)
    {
        _graphClient = graphClient;
    }

    // ========== Session Operations ==========

    public async Task<Session> CreateSessionAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var session = Session.Create(sessionId);

        var properties = new Dictionary<string, Value>
        {
            ["id"] = new StringValue(session.Id),
            [PropStartedAt] = new StringValue(session.StartedAt.ToString("O")),
            [PropStatus] = new StringValue(session.Status.ToString())
        };

        await _graphClient.WriteAsync(SessionNodeType, properties);
        return session;
    }

    public async Task<Session?> GetSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var graphNode = await _graphClient.ReadAsync(new NodeId(sessionId));
        return graphNode == null ? null : ToSession(graphNode);
    }

    public async Task UpdateSessionAsync(Session session, CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, Value>
        {
            ["id"] = new StringValue(session.Id),
            [PropStartedAt] = new StringValue(session.StartedAt.ToString("O")),
            [PropStatus] = new StringValue(session.Status.ToString())
        };

        if (session.EndedAt.HasValue)
        {
            properties[PropEndedAt] = new StringValue(session.EndedAt.Value.ToString("O"));
        }

        await _graphClient.WriteAsync(SessionNodeType, properties);
    }

    private static Session ToSession(GraphNode graphNode)
    {
        var props = graphNode.Properties;

        var startedAt = props.TryGetValue(PropStartedAt, out var startedVal) && startedVal is StringValue startedSv
            ? DateTimeOffset.Parse(startedSv.Data)
            : DateTimeOffset.UtcNow;

        var statusStr = props.TryGetValue(PropStatus, out var statusVal) && statusVal is StringValue statusSv
            ? statusSv.Data
            : SessionStatus.Active.ToString();
        var status = Enum.TryParse<SessionStatus>(statusStr, out var s) ? s : SessionStatus.Active;

        DateTimeOffset? endedAt = null;
        if (props.TryGetValue(PropEndedAt, out var endedVal) && endedVal is StringValue endedSv &&
            !string.IsNullOrWhiteSpace(endedSv.Data))
        {
            endedAt = DateTimeOffset.Parse(endedSv.Data);
        }

        return new Session
        {
            Id = graphNode.Id.Value,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Status = status
        };
    }

    // ========== Helper Methods ==========

    private static string CompositeId(string sessionId, string nodeId) => $"{sessionId}:{nodeId}";

    // ========== KnowledgeNode Operations ==========

    public async Task AddKnowledgeNodeAsync(KnowledgeNode node, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(node.SessionId, node.Id);

        var properties = new Dictionary<string, Value>
        {
            ["id"] = new StringValue(compositeId),
            [PropSessionId] = new StringValue(node.SessionId),
            [PropNodeId] = new StringValue(node.Id),
            [PropNodeType] = new StringValue(node.NodeType.ToString()),
            [PropOwner] = new StringValue((node.Owner ?? string.Empty).Trim()),
            [PropCoreDescription] = new StringValue(node.CoreDescription),
            [PropDetailedDescription] = new StringValue(node.DetailedDescription),
            [PropCreatedAt] = new StringValue(node.CreatedAt.ToString("O")),
            [PropUpdatedAt] = new StringValue(node.UpdatedAt.ToString("O")),
            [PropDependsOn] = new StringValue(string.Join(",", node.DependsOn)),
            [PropNodeAttestationsJson] = new StringValue(JsonSerializer.Serialize(node.Attestations, JsonOptions)),
            [PropPivotStatus] = new StringValue(node.PivotStatus.ToString())
        };

        if (node.Proof != null)
            properties[PropProof] = new StringValue(node.Proof);

        if (node.ResourceUri != null)
            properties[PropResourceUri] = new StringValue(node.ResourceUri);

        if (node.DerivationProcess != null)
            properties[PropDerivationProcess] = new StringValue(node.DerivationProcess);

        if (node.References.Count > 0)
            properties[PropReferencesJson] = new StringValue(JsonSerializer.Serialize(node.References, JsonOptions));

        if (node.CancelledAt.HasValue)
            properties[PropCancelledAt] = new StringValue(node.CancelledAt.Value.ToString("O"));

        if (node.CancelledByPivotId != null)
            properties[PropCancelledByPivotId] = new StringValue(node.CancelledByPivotId);

        if (node.DirectionContext != null)
            properties[PropDirectionContext] = new StringValue(node.DirectionContext);

        await _graphClient.WriteAsync(KnowledgeNodeGraphType, properties);
    }

    public async Task<KnowledgeNode?> GetKnowledgeNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var graphNode = await _graphClient.ReadAsync(new NodeId(compositeId));

        // Verify this is actually a KnowledgeNode (has nodeType property, not planStatus)
        if (graphNode == null) return null;
        if (!graphNode.Properties.ContainsKey(PropNodeType)) return null;

        return ToKnowledgeNode(graphNode);
    }

    public async Task<IReadOnlyList<KnowledgeNode>> GetAllKnowledgeNodesAsync(string sessionId, CancellationToken cancellationToken)
    {
        var nodes = await _graphClient.QueryAsync(new NodeQuery { Type = KnowledgeNodeGraphType });

        return nodes
            .Where(n => n.Properties.TryGetValue(PropSessionId, out var sv) &&
                        sv is StringValue sessionVal && sessionVal.Data == sessionId)
            .Select(ToKnowledgeNode)
            .ToList();
    }

    public async Task<bool> RemoveKnowledgeNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var exists = await KnowledgeNodeExistsAsync(sessionId, nodeId, cancellationToken);
        if (!exists) return false;

        await _graphClient.DeleteAsync(new NodeId(compositeId));
        return true;
    }

    public async Task<bool> KnowledgeNodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var node = await _graphClient.ReadAsync(new NodeId(compositeId));
        return node != null;
    }

    private static KnowledgeNode ToKnowledgeNode(GraphNode graphNode)
    {
        var props = graphNode.Properties;

        var sessionId = GetStringProp(props, PropSessionId, "");
        var nodeId = GetStringProp(props, PropNodeId, graphNode.Id.Value);
        var nodeTypeStr = GetStringProp(props, PropNodeType, "Generic");
        var nodeType = Enum.TryParse<KnowledgeNodeType>(nodeTypeStr, out var nt) ? nt : KnowledgeNodeType.Generic;

        var owner = GetStringProp(props, PropOwner, null);
        owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();

        var coreDescription = GetStringProp(props, PropCoreDescription, "");
        var detailedDescription = GetStringProp(props, PropDetailedDescription, "");
        var proof = GetStringProp(props, PropProof, null);
        var resourceUri = GetStringProp(props, PropResourceUri, null);

        // Legacy: use timestamp as fallback for createdAt if createdAt not present
        var legacyTimestamp = GetDateTimeOffsetProp(props, PropTimestamp, DateTimeOffset.MinValue);
        var createdAt = GetDateTimeOffsetProp(props, PropCreatedAt, legacyTimestamp != DateTimeOffset.MinValue ? legacyTimestamp : DateTimeOffset.UtcNow);
        var updatedAt = GetDateTimeOffsetProp(props, PropUpdatedAt, DateTimeOffset.UtcNow);

        var attestations = GetJsonListProp<KnowledgeAttestation>(props, PropNodeAttestationsJson);
        var dependsOn = GetCommaSeparatedListProp(props, PropDependsOn);
        var derivationProcess = GetStringProp(props, PropDerivationProcess, null);
        var references = GetJsonListProp<string>(props, PropReferencesJson);

        var pivotStatusStr = GetStringProp(props, PropPivotStatus, PivotNodeStatus.Active.ToString());
        var pivotStatus = Enum.TryParse<PivotNodeStatus>(pivotStatusStr, out var ps) ? ps : PivotNodeStatus.Active;

        var cancelledAt = GetNullableDateTimeOffsetProp(props, PropCancelledAt);
        var cancelledByPivotId = GetStringProp(props, PropCancelledByPivotId, null);
        var directionContext = GetStringProp(props, PropDirectionContext, null);

        return new KnowledgeNode
        {
            Id = nodeId,
            SessionId = sessionId,
            NodeType = nodeType,
            Owner = owner,
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            Proof = proof,
            ResourceUri = resourceUri,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Attestations = attestations,
            DependsOn = dependsOn,
            DerivationProcess = derivationProcess,
            References = references,
            PivotStatus = pivotStatus,
            CancelledAt = cancelledAt,
            CancelledByPivotId = cancelledByPivotId,
            DirectionContext = directionContext
        };
    }

    // ========== PlanNode Operations ==========

    public async Task AddPlanNodeAsync(PlanNode node, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(node.SessionId, node.Id);

        var properties = new Dictionary<string, Value>
        {
            ["id"] = new StringValue(compositeId),
            [PropSessionId] = new StringValue(node.SessionId),
            [PropNodeId] = new StringValue(node.Id),
            [PropOwner] = new StringValue((node.Owner ?? string.Empty).Trim()),
            [PropCoreDescription] = new StringValue(node.CoreDescription),
            [PropDetailedDescription] = new StringValue(node.DetailedDescription),
            [PropCreatedAt] = new StringValue(node.CreatedAt.ToString("O")),
            [PropUpdatedAt] = new StringValue(node.UpdatedAt.ToString("O")),
            [PropDependsOn] = new StringValue(string.Join(",", node.DependsOn)),
            [PropPlanStatus] = new StringValue(node.Status.ToString()),
            [PropSequentialOrder] = new StringValue(node.SequentialOrder.ToString()),
            [PropPivotStatus] = new StringValue(node.PivotStatus.ToString())
        };

        if (node.ProgressText != null)
            properties[PropProgressText] = new StringValue(node.ProgressText);

        if (node.Methodology != null)
            properties[PropMethodology] = new StringValue(node.Methodology);

        if (node.CancelledAt.HasValue)
            properties[PropCancelledAt] = new StringValue(node.CancelledAt.Value.ToString("O"));

        if (node.CancelledByPivotId != null)
            properties[PropCancelledByPivotId] = new StringValue(node.CancelledByPivotId);

        if (node.DirectionContext != null)
            properties[PropDirectionContext] = new StringValue(node.DirectionContext);

        await _graphClient.WriteAsync(PlanNodeGraphType, properties);
    }

    public async Task<PlanNode?> GetPlanNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var graphNode = await _graphClient.ReadAsync(new NodeId(compositeId));

        // Verify this is actually a PlanNode (has planStatus property, not nodeType)
        if (graphNode == null) return null;
        if (!graphNode.Properties.ContainsKey(PropPlanStatus)) return null;

        return ToPlanNode(graphNode);
    }

    public async Task<IReadOnlyList<PlanNode>> GetAllPlanNodesAsync(string sessionId, CancellationToken cancellationToken)
    {
        var nodes = await _graphClient.QueryAsync(new NodeQuery { Type = PlanNodeGraphType });

        return nodes
            .Where(n => n.Properties.TryGetValue(PropSessionId, out var sv) &&
                        sv is StringValue sessionVal && sessionVal.Data == sessionId)
            .Select(ToPlanNode)
            .ToList();
    }

    public async Task<bool> RemovePlanNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var exists = await PlanNodeExistsAsync(sessionId, nodeId, cancellationToken);
        if (!exists) return false;

        await _graphClient.DeleteAsync(new NodeId(compositeId));
        return true;
    }

    public async Task<bool> PlanNodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var compositeId = CompositeId(sessionId, nodeId);
        var node = await _graphClient.ReadAsync(new NodeId(compositeId));
        return node != null;
    }

    public async Task UpdatePlanNodeAsync(PlanNode node, CancellationToken cancellationToken)
    {
        // Same as Add - WriteAsync uses MERGE semantics
        await AddPlanNodeAsync(node, cancellationToken);
    }

    private static PlanNode ToPlanNode(GraphNode graphNode)
    {
        var props = graphNode.Properties;

        var sessionId = GetStringProp(props, PropSessionId, "");
        var nodeId = GetStringProp(props, PropNodeId, graphNode.Id.Value);

        var owner = GetStringProp(props, PropOwner, null);
        owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();

        var coreDescription = GetStringProp(props, PropCoreDescription, "");
        var detailedDescription = GetStringProp(props, PropDetailedDescription, "");

        var createdAt = GetDateTimeOffsetProp(props, PropCreatedAt, DateTimeOffset.UtcNow);
        var updatedAt = GetDateTimeOffsetProp(props, PropUpdatedAt, DateTimeOffset.UtcNow);

        var dependsOn = GetCommaSeparatedListProp(props, PropDependsOn);

        var statusStr = GetStringProp(props, PropPlanStatus, PlanNodeStatus.Pending.ToString());
        var status = Enum.TryParse<PlanNodeStatus>(statusStr, out var st) ? st : PlanNodeStatus.Pending;

        var progressText = GetStringProp(props, PropProgressText, null);
        var methodology = GetStringProp(props, PropMethodology, null);

        var sequentialOrderStr = GetStringProp(props, PropSequentialOrder, "0");
        var sequentialOrder = int.TryParse(sequentialOrderStr, out var so) ? so : 0;

        var pivotStatusStr = GetStringProp(props, PropPivotStatus, PivotNodeStatus.Active.ToString());
        var pivotStatus = Enum.TryParse<PivotNodeStatus>(pivotStatusStr, out var ps) ? ps : PivotNodeStatus.Active;

        var cancelledAt = GetNullableDateTimeOffsetProp(props, PropCancelledAt);
        var cancelledByPivotId = GetStringProp(props, PropCancelledByPivotId, null);
        var directionContext = GetStringProp(props, PropDirectionContext, null);

        return new PlanNode
        {
            Id = nodeId,
            SessionId = sessionId,
            Owner = owner,
            CoreDescription = coreDescription,
            DetailedDescription = detailedDescription,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            DependsOn = dependsOn,
            Status = status,
            ProgressText = progressText,
            Methodology = methodology,
            SequentialOrder = sequentialOrder,
            PivotStatus = pivotStatus,
            CancelledAt = cancelledAt,
            CancelledByPivotId = cancelledByPivotId,
            DirectionContext = directionContext
        };
    }

    // ========== Generic Node Operations ==========

    public async Task<bool> NodeExistsAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        // Check both PlanNode and KnowledgeNode stores
        return await PlanNodeExistsAsync(sessionId, nodeId, cancellationToken) ||
               await KnowledgeNodeExistsAsync(sessionId, nodeId, cancellationToken);
    }

    public async Task<IGraphNode?> GetNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        // Try to get as PlanNode first, then KnowledgeNode
        var planNode = await GetPlanNodeAsync(sessionId, nodeId, cancellationToken);
        if (planNode != null) return planNode;

        return await GetKnowledgeNodeAsync(sessionId, nodeId, cancellationToken);
    }

    public async Task<bool> RemoveNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        // Try to remove from both stores
        var removedPlan = await RemovePlanNodeAsync(sessionId, nodeId, cancellationToken);
        var removedKnowledge = await RemoveKnowledgeNodeAsync(sessionId, nodeId, cancellationToken);
        return removedPlan || removedKnowledge;
    }

    public async Task<IReadOnlyList<IGraphNode>> GetAllNodesAsync(string sessionId, CancellationToken cancellationToken)
    {
        var planNodes = await GetAllPlanNodesAsync(sessionId, cancellationToken);
        var knowledgeNodes = await GetAllKnowledgeNodesAsync(sessionId, cancellationToken);

        var allNodes = new List<IGraphNode>();
        allNodes.AddRange(planNodes);
        allNodes.AddRange(knowledgeNodes);
        return allNodes;
    }

    // ========== Edge Operations ==========

    public async Task AddEdgeAsync(KnowledgeEdge edge, string sessionId, CancellationToken cancellationToken)
    {
        var fromCompositeId = CompositeId(sessionId, edge.FromId);
        var toCompositeId = CompositeId(sessionId, edge.ToId);

        var edgeId = $"{sessionId}:{edge.FromId}->{edge.ToId}:{EdgeType}";

        var properties = new Dictionary<string, Value>
        {
            ["id"] = new StringValue(edgeId),
            [PropSessionId] = new StringValue(sessionId),
            [PropCreatedAt] = new StringValue(edge.CreatedAt.ToString("O")),
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
                var createdAt = GetDateTimeOffsetProp(e.Properties, PropCreatedAt, DateTimeOffset.UtcNow);

                string fromId = GetStringProp(e.Properties, PropFromNodeId, null)
                                ?? ExtractNodeIdFromComposite(e.From.Value);
                string toId = GetStringProp(e.Properties, PropToNodeId, null)
                              ?? ExtractNodeIdFromComposite(e.To.Value);

                return new KnowledgeEdge
                {
                    FromId = fromId,
                    ToId = toId,
                    CreatedAt = createdAt
                };
            })
            .ToList();

        // Dedupe by (fromId, toId) pair
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

        return allEdges
            .Where(e => e.Properties.TryGetValue(PropSessionId, out var sv) &&
                        sv is StringValue sessionVal && sessionVal.Data == sessionId &&
                        e.Properties.TryGetValue(PropFromNodeId, out var fromVal) &&
                        fromVal is StringValue fromStr && fromStr.Data == nodeId)
            .Select(e => GetStringProp(e.Properties, PropToNodeId, null)
                         ?? ExtractNodeIdFromComposite(e.To.Value))
            .ToList();
    }

    public async Task RemoveEdgesForNodeAsync(string sessionId, string nodeId, CancellationToken cancellationToken)
    {
        var allEdges = await _graphClient.QueryAsync(new EdgeQuery { Type = EdgeType });

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

    // ========== Property Helpers ==========

    private static string? GetStringProp(IReadOnlyDictionary<string, Value> props, string key, string? defaultValue)
    {
        if (props.TryGetValue(key, out var val) && val is StringValue sv)
            return sv.Data;
        return defaultValue;
    }

    private static DateTimeOffset GetDateTimeOffsetProp(IReadOnlyDictionary<string, Value> props, string key, DateTimeOffset defaultValue)
    {
        if (props.TryGetValue(key, out var val) && val is StringValue sv && !string.IsNullOrWhiteSpace(sv.Data))
            return DateTimeOffset.Parse(sv.Data);
        return defaultValue;
    }

    private static DateTimeOffset? GetNullableDateTimeOffsetProp(IReadOnlyDictionary<string, Value> props, string key)
    {
        if (props.TryGetValue(key, out var val) && val is StringValue sv && !string.IsNullOrWhiteSpace(sv.Data))
            return DateTimeOffset.Parse(sv.Data);
        return null;
    }

    private static IReadOnlyList<string> GetCommaSeparatedListProp(IReadOnlyDictionary<string, Value> props, string key)
    {
        if (props.TryGetValue(key, out var val) && val is StringValue sv && !string.IsNullOrWhiteSpace(sv.Data))
            return sv.Data.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return Array.Empty<string>();
    }

    private static IReadOnlyList<T> GetJsonListProp<T>(IReadOnlyDictionary<string, Value> props, string key)
    {
        if (props.TryGetValue(key, out var val) && val is StringValue sv && !string.IsNullOrWhiteSpace(sv.Data))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<T>>(sv.Data, JsonOptions);
                return (IReadOnlyList<T>?)list ?? Array.Empty<T>();
            }
            catch
            {
                return Array.Empty<T>();
            }
        }
        return Array.Empty<T>();
    }

    private static string ExtractNodeIdFromComposite(string compositeId)
    {
        var parts = compositeId.Split(':', 2);
        return parts.Length > 1 ? parts[1] : compositeId;
    }

    // ========== Cross-Session Operations ==========

    public async Task<IReadOnlyList<KnowledgeNode>> SearchKnowledgeNodesGlobalAsync(
        string query,
        int maxResults = 10,
        string? excludeSessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<KnowledgeNode>();

        var queryLower = query.ToLowerInvariant();
        var allNodes = await _graphClient.QueryAsync(new NodeQuery { Type = KnowledgeNodeGraphType });

        var results = allNodes
            .Where(n =>
            {
                // Exclude specified session
                if (!string.IsNullOrEmpty(excludeSessionId) &&
                    n.Properties.TryGetValue(PropSessionId, out var sv) &&
                    sv is StringValue sessionVal && sessionVal.Data == excludeSessionId)
                {
                    return false;
                }

                // Verify it has required properties
                if (!n.Properties.ContainsKey(PropNodeType))
                    return false;

                // Match against CoreDescription and DetailedDescription
                var coreDesc = GetStringProp(n.Properties, PropCoreDescription, "") ?? "";
                var detailedDesc = GetStringProp(n.Properties, PropDetailedDescription, "") ?? "";

                return coreDesc.ToLowerInvariant().Contains(queryLower) ||
                       detailedDesc.ToLowerInvariant().Contains(queryLower);
            })
            .Take(maxResults)
            .Select(ToKnowledgeNode)
            .ToList();

        return results;
    }

    public async Task<KnowledgeNode?> GetKnowledgeNodeGlobalAsync(
        string sessionId,
        string nodeId,
        CancellationToken cancellationToken)
    {
        // Same as GetKnowledgeNodeAsync - access is allowed for cross-session reference
        return await GetKnowledgeNodeAsync(sessionId, nodeId, cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeNode>> GetAllKnowledgeNodesGlobalAsync(CancellationToken cancellationToken)
    {
        var nodes = await _graphClient.QueryAsync(new NodeQuery { Type = KnowledgeNodeGraphType });
        return nodes.Select(ToKnowledgeNode).ToList();
    }

    public async Task<IReadOnlyList<(KnowledgeEdge Edge, string SessionId)>> GetAllEdgesGlobalAsync(CancellationToken cancellationToken)
    {
        var edges = await _graphClient.QueryAsync(new EdgeQuery { Type = EdgeType });

        return edges
            .Select(e =>
            {
                var sessionId = GetStringProp(e.Properties, PropSessionId, "");
                var createdAt = GetDateTimeOffsetProp(e.Properties, PropCreatedAt, DateTimeOffset.UtcNow);

                string fromId = GetStringProp(e.Properties, PropFromNodeId, null)
                                ?? ExtractNodeIdFromComposite(e.From.Value);
                string toId = GetStringProp(e.Properties, PropToNodeId, null)
                              ?? ExtractNodeIdFromComposite(e.To.Value);

                var edge = new KnowledgeEdge
                {
                    FromId = fromId,
                    ToId = toId,
                    CreatedAt = createdAt
                };

                return (Edge: edge, SessionId: sessionId);
            })
            .ToList();
    }
}
