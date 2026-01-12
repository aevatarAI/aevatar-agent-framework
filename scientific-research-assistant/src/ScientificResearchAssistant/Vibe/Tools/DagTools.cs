using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace ScientificResearchAssistant.Vibe.Tools;

// ============================================================
//  DAG tools (read-only)
//
//  Why:
//  - dag_builder needs stable ids and must reuse existing nodes when possible.
//  - A read-only snapshot/explain tool improves consistency without bypassing
//    DAG consensus (DagConsensusRunner).
//
//  Safety:
//  - Read-only: never writes, never stages, never applies.
// ============================================================

public interface IVibeDagAccess
{
    Task<Struct> GetSnapshotAsync(string sessionId, int maxNodes, int maxEdges, CancellationToken ct);
    Task<Struct> ExplainAsync(string sessionId, string nodeId, CancellationToken ct);
}

internal sealed class DagGetSnapshotTool : AevatarToolBase
{
    private readonly IVibeDagAccess _dag;

    public DagGetSnapshotTool(IVibeDagAccess dag)
    {
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
    }

    public override string Name => "dag_get_snapshot";
    public override string Description => "Get current DAG snapshot for this session (bounded). Read-only.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = [],
            Items = new Dictionary<string, ToolParameter>
            {
                ["maxNodes"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max nodes to include in the snapshot (default 250, capped to 2000).",
                    Required = false,
                    DefaultValue = 250
                },
                ["maxEdges"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Max edges to include in the snapshot (default 800, capped to 8000).",
                    Required = false,
                    DefaultValue = 800
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var sid = (context?.GetSessionIdCallback?.Invoke() ?? string.Empty).Trim();
        if (sid.Length == 0)
            throw new InvalidOperationException("missing sessionId in tool context");

        var maxNodes = ClampInt(parameters.GetValueOrDefault("maxNodes"), 250, 1, 2000);
        var maxEdges = ClampInt(parameters.GetValueOrDefault("maxEdges"), 800, 1, 8000);

        return await _dag.GetSnapshotAsync(sid, maxNodes, maxEdges, cancellationToken);
    }

    private static int ClampInt(object? v, int fallback, int min, int max)
    {
        if (!TryParseInt(v, out var i))
            i = fallback;
        return Math.Clamp(i, min, max);
    }

    private static bool TryParseInt(object? v, out int i)
    {
        i = 0;
        if (v == null) return false;
        if (v is int ii) { i = ii; return true; }
        if (v is long ll) { i = (int)Math.Clamp(ll, int.MinValue, int.MaxValue); return true; }
        return int.TryParse(v.ToString(), out i);
    }
}

internal sealed class DagExplainTool : AevatarToolBase
{
    private readonly IVibeDagAccess _dag;

    public DagExplainTool(IVibeDagAccess dag)
    {
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
    }

    public override string Name => "dag_explain";
    public override string Description => "Explain a node in the current DAG (deps, topo order, missing deps). Read-only.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = ["nodeId"],
            Items = new Dictionary<string, ToolParameter>
            {
                ["nodeId"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Target node id to explain (e.g. thm_pythagoras_v1).",
                    Required = true,
                    MinLength = 1,
                    MaxLength = 200
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var sid = (context?.GetSessionIdCallback?.Invoke() ?? string.Empty).Trim();
        if (sid.Length == 0)
            throw new InvalidOperationException("missing sessionId in tool context");

        var nodeId = (parameters.GetValueOrDefault("nodeId")?.ToString() ?? string.Empty).Trim();
        if (nodeId.Length == 0)
            throw new ArgumentException("nodeId is required");

        // Normalize to a stable token-ish id (best-effort; no hard enforcement).
        nodeId = nodeId.Replace('\\', '/').Trim();

        return await _dag.ExplainAsync(sid, nodeId, cancellationToken);
    }
}


