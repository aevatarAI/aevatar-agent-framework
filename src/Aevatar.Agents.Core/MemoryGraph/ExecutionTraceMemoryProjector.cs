using System.Text;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.MemoryGraphs;

// ============================================================
//  ExecutionTraceMemoryProjector
//
//  PURPOSE:
//  - Convert ExecutionTrace (tree) into:
//    1) MemoryGraph (nodes/edges) for navigation/explainability
//    2) MemoryEntry (text) under scope=execution for retrieval
//
//  PRINCIPLES:
//  - Derived artifact: best-effort, reproducible, never blocks main flow.
//  - Keep text bounded: avoid dumping huge payloads.
// ============================================================
public sealed class ExecutionTraceMemoryProjector
{
    public const int MaxTextChars = 2000;
    public const int MaxNodes = 500;

    private readonly ILogger _logger;

    public ExecutionTraceMemoryProjector(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task ProjectAsync(
        ExecutionTrace trace,
        IMemoryStore memoryStore,
        IMemoryGraphStore graphStore,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(memoryStore);
        ArgumentNullException.ThrowIfNull(graphStore);

        var executionId = trace.ExecutionId?.Trim();
        if (string.IsNullOrWhiteSpace(executionId))
            return;

        try
        {
            var (graph, entries) = Build(trace);

            await graphStore.SaveAsync(graph, ct);

            foreach (var e in entries)
            {
                ct.ThrowIfCancellationRequested();
                await memoryStore.AppendAsync(e, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ExecutionTrace -> MemoryGraph projection failed (best-effort)");
        }
    }

    internal (MemoryGraph Graph, IReadOnlyList<MemoryEntry> Entries) Build(ExecutionTrace trace)
    {
        var executionId = trace.ExecutionId?.Trim() ?? string.Empty;

        var scope = new MemoryScope
        {
            Type = MemoryScopeType.Execution,
            ScopeId = executionId
        };

        var graph = new MemoryGraph
        {
            GraphId = executionId,
            Scope = scope,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        graph.Labels["kind"] = trace.Kind.ToString();
        graph.Labels["status"] = trace.Status.ToString();
        if (!string.IsNullOrWhiteSpace(trace.Name))
            graph.Labels["name"] = trace.Name.Trim();

        var entries = new List<MemoryEntry>();
        var memoryId = BuildMemoryId(scope);

        // Execution root node
        var execNodeId = $"exec:{executionId}";
        graph.Nodes.Add(new MemoryGraphNode
        {
            NodeId = execNodeId,
            Type = "execution",
            Name = string.IsNullOrWhiteSpace(trace.Name) ? executionId : trace.Name.Trim(),
            Content = Truncate($"{trace.Description}\n{trace.Error}".Trim())
        });

        // Root trace node
        if (trace.Root != null)
        {
            var visited = 0;
            VisitTraceNode(
                trace,
                trace.Root,
                parentGraphNodeId: execNodeId,
                graph,
                entries,
                memoryId,
                scope,
                ref visited);
        }

        return (graph, entries);
    }

    private void VisitTraceNode(
        ExecutionTrace trace,
        ExecutionTraceNode node,
        string parentGraphNodeId,
        MemoryGraph graph,
        List<MemoryEntry> entries,
        string memoryId,
        MemoryScope scope,
        ref int visited)
    {
        if (visited >= MaxNodes)
            return;
        visited++;

        var rawNodeId = string.IsNullOrWhiteSpace(node.NodeId) ? Guid.NewGuid().ToString("N") : node.NodeId.Trim();
        var graphNodeId = $"node:{rawNodeId}";

        graph.Nodes.Add(new MemoryGraphNode
        {
            NodeId = graphNodeId,
            Type = "trace_node",
            Name = string.IsNullOrWhiteSpace(node.Name) ? rawNodeId : node.Name.Trim(),
            Content = Truncate(BuildTraceNodeSnippet(node))
        });

        graph.Edges.Add(new MemoryGraphEdge
        {
            EdgeId = $"edge:{parentGraphNodeId}->{graphNodeId}:child",
            FromNodeId = parentGraphNodeId,
            ToNodeId = graphNodeId,
            Type = "child",
            Label = "child"
        });

        // Memory entry (node output/description/error)
        var text = BuildTraceNodeMemoryText(trace, node);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var entry = new MemoryEntry
            {
                EntryId = $"{trace.ExecutionId}:{rawNodeId}",
                MemoryId = memoryId,
                Scope = scope,
                RunId = trace.ExecutionId ?? string.Empty,
                AgentId = string.Empty,
                Role = "trace_node",
                Content = Truncate(text),
                CreatedAt = node.StartedAt ?? trace.StartedAt ?? Timestamp.FromDateTime(DateTime.UtcNow)
            };

            entry.Tags["source"] = "execution_trace";
            entry.Tags["execution_id"] = trace.ExecutionId ?? string.Empty;
            entry.Tags["node_id"] = rawNodeId;
            entry.Tags["node_type"] = node.Type ?? string.Empty;
            entry.Tags["node_status"] = node.Status.ToString();
            entries.Add(entry);
        }

        // Decisions
        foreach (var d in node.Decisions)
        {
            var decisionId = string.IsNullOrWhiteSpace(d.DecisionId) ? Guid.NewGuid().ToString("N") : d.DecisionId.Trim();
            var decisionNodeId = $"decision:{decisionId}";

            graph.Nodes.Add(new MemoryGraphNode
            {
                NodeId = decisionNodeId,
                Type = "decision",
                Name = string.IsNullOrWhiteSpace(d.Type) ? decisionId : d.Type.Trim(),
                Content = Truncate(BuildDecisionSnippet(d))
            });

            graph.Edges.Add(new MemoryGraphEdge
            {
                EdgeId = $"edge:{graphNodeId}->{decisionNodeId}:has_decision",
                FromNodeId = graphNodeId,
                ToNodeId = decisionNodeId,
                Type = "has_decision",
                Label = "has_decision"
            });

            // Candidates
            foreach (var c in d.Candidates)
            {
                var candId = string.IsNullOrWhiteSpace(c.CandidateId) ? Guid.NewGuid().ToString("N") : c.CandidateId.Trim();
                var candNodeId = $"candidate:{decisionId}:{candId}";

                graph.Nodes.Add(new MemoryGraphNode
                {
                    NodeId = candNodeId,
                    Type = "candidate",
                    Name = candId,
                    Content = Truncate(c.Content ?? string.Empty)
                });

                graph.Edges.Add(new MemoryGraphEdge
                {
                    EdgeId = $"edge:{decisionNodeId}->{candNodeId}:has_candidate",
                    FromNodeId = decisionNodeId,
                    ToNodeId = candNodeId,
                    Type = "has_candidate",
                    Label = "has_candidate"
                });

                if (!string.IsNullOrWhiteSpace(d.WinnerCandidateId) &&
                    string.Equals(d.WinnerCandidateId.Trim(), candId, StringComparison.Ordinal))
                {
                    graph.Edges.Add(new MemoryGraphEdge
                    {
                        EdgeId = $"edge:{decisionNodeId}->{candNodeId}:selected",
                        FromNodeId = decisionNodeId,
                        ToNodeId = candNodeId,
                        Type = "selected",
                        Label = "selected"
                    });
                }

                // Memory entry for candidate text (searchable)
                if (!string.IsNullOrWhiteSpace(c.Content))
                {
                    var entry = new MemoryEntry
                    {
                        EntryId = $"{trace.ExecutionId}:decision:{decisionId}:candidate:{candId}",
                        MemoryId = memoryId,
                        Scope = scope,
                        RunId = trace.ExecutionId ?? string.Empty,
                        AgentId = string.Empty,
                        Role = "decision_candidate",
                        Content = Truncate(c.Content),
                        CreatedAt = node.StartedAt ?? trace.StartedAt ?? Timestamp.FromDateTime(DateTime.UtcNow)
                    };
                    entry.Tags["source"] = "execution_trace";
                    entry.Tags["execution_id"] = trace.ExecutionId ?? string.Empty;
                    entry.Tags["decision_id"] = decisionId;
                    entry.Tags["candidate_id"] = candId;
                    entry.Tags["winner"] = string.Equals(d.WinnerCandidateId?.Trim(), candId, StringComparison.Ordinal)
                        ? "true"
                        : "false";
                    entries.Add(entry);
                }
            }
        }

        // Alerts
        foreach (var a in node.Alerts)
        {
            var alertId = string.IsNullOrWhiteSpace(a.AlertId) ? Guid.NewGuid().ToString("N") : a.AlertId.Trim();
            var alertNodeId = $"alert:{alertId}";

            graph.Nodes.Add(new MemoryGraphNode
            {
                NodeId = alertNodeId,
                Type = "alert",
                Name = string.IsNullOrWhiteSpace(a.Type) ? alertId : a.Type.Trim(),
                Content = Truncate(a.Message ?? string.Empty)
            });

            graph.Edges.Add(new MemoryGraphEdge
            {
                EdgeId = $"edge:{graphNodeId}->{alertNodeId}:has_alert",
                FromNodeId = graphNodeId,
                ToNodeId = alertNodeId,
                Type = "has_alert",
                Label = "has_alert"
            });
        }

        // Children
        foreach (var child in node.Children)
        {
            VisitTraceNode(trace, child, graphNodeId, graph, entries, memoryId, scope, ref visited);
        }
    }

    private static string BuildMemoryId(MemoryScope scope)
    {
        var type = scope.Type.ToString().ToLowerInvariant();
        var id = (scope.ScopeId ?? string.Empty).Trim();
        return $"{type}::{id}";
    }

    private static string BuildTraceNodeSnippet(ExecutionTraceNode node)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(node.Type))
            sb.Append(node.Type.Trim()).Append(' ');
        if (!string.IsNullOrWhiteSpace(node.Name))
            sb.Append(node.Name.Trim()).Append(' ');
        sb.Append('[').Append(node.Status).Append(']');

        if (!string.IsNullOrWhiteSpace(node.Output))
        {
            sb.Append(" output=");
            sb.Append(node.Output.Trim().Replace("\r", " "));
        }
        else if (!string.IsNullOrWhiteSpace(node.Description))
        {
            sb.Append(" desc=");
            sb.Append(node.Description.Trim().Replace("\r", " "));
        }
        else if (!string.IsNullOrWhiteSpace(node.Error))
        {
            sb.Append(" error=");
            sb.Append(node.Error.Trim().Replace("\r", " "));
        }

        return sb.ToString();
    }

    private static string BuildDecisionSnippet(ExecutionTraceDecisionSession d)
    {
        var sb = new StringBuilder();
        sb.Append("type=").Append(d.Type);
        sb.Append(" rounds=").Append(d.Rounds);
        if (!string.IsNullOrWhiteSpace(d.WinnerCandidateId))
            sb.Append(" winner=").Append(d.WinnerCandidateId.Trim());
        sb.Append(" candidates=").Append(d.Candidates?.Count ?? 0);
        return sb.ToString();
    }

    private static string BuildTraceNodeMemoryText(ExecutionTrace trace, ExecutionTraceNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[execution_id] {trace.ExecutionId}");
        sb.AppendLine($"[node_id] {node.NodeId}");
        sb.AppendLine($"[node_name] {node.Name}");
        sb.AppendLine($"[node_type] {node.Type}");
        sb.AppendLine($"[status] {node.Status}");

        if (!string.IsNullOrWhiteSpace(node.Description))
        {
            sb.AppendLine();
            sb.AppendLine("description:");
            sb.AppendLine(node.Description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(node.Output))
        {
            sb.AppendLine();
            sb.AppendLine("output:");
            sb.AppendLine(node.Output.Trim());
        }

        if (!string.IsNullOrWhiteSpace(node.Error))
        {
            sb.AppendLine();
            sb.AppendLine("error:");
            sb.AppendLine(node.Error.Trim());
        }

        var text = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    private static string Truncate(string? text)
    {
        var s = (text ?? string.Empty).Replace("\r", "").Trim();
        if (s.Length <= MaxTextChars)
            return s;
        return s[..MaxTextChars];
    }
}


