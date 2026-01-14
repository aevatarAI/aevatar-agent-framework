using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace ScientificResearchAssistant.Vibe.Tools;

/// <summary>
/// Tool for getting the current knowledge graph snapshot.
/// Operates on KnowledgeGraph (Plan + Knowledge nodes).
/// </summary>
internal sealed class GraphGetSnapshotTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public GraphGetSnapshotTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "graph_get_snapshot";
    public override string Description => "Get current knowledge graph snapshot (Plan + Knowledge nodes). Read-only.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = [],
        Items = new Dictionary<string, ToolParameter>
        {
            ["maxNodes"] = new() { Type = "integer", Description = "Max nodes (default 250, max 2000)", Required = false, DefaultValue = 250 },
            ["maxEdges"] = new() { Type = "integer", Description = "Max edges (default 800, max 8000)", Required = false, DefaultValue = 800 }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        var maxNodes = ClampInt(parameters.GetValueOrDefault("maxNodes"), 250, 1, 2000);
        var maxEdges = ClampInt(parameters.GetValueOrDefault("maxEdges"), 800, 1, 8000);
        return await _access.GetSnapshotAsync(GetSessionId(context), maxNodes, maxEdges, ct);
    }

    private static int ClampInt(object? v, int fallback, int min, int max)
    {
        if (v == null) return fallback;
        if (v is int i) return Math.Clamp(i, min, max);
        if (v is long l) return (int)Math.Clamp(l, min, max);
        return int.TryParse(v.ToString(), out var parsed) ? Math.Clamp(parsed, min, max) : fallback;
    }
}

/// <summary>
/// Tool for explaining a node in the knowledge graph.
/// Operates on KnowledgeGraph (Plan + Knowledge nodes).
/// </summary>
internal sealed class GraphExplainNodeTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public GraphExplainNodeTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "graph_explain_node";
    public override string Description => "Explain a node in the knowledge graph (dependencies, relationships). Read-only.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["nodeId"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["nodeId"] = new() { Type = "string", Description = "Target node ID to explain", Required = true, MinLength = 1, MaxLength = 200 }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        var nodeId = GetRequiredString(parameters, "nodeId").Replace('\\', '/').Trim();
        return await _access.ExplainNodeAsync(GetSessionId(context), nodeId, ct);
    }
}

/// <summary>
/// Tool for creating a pivot snapshot before changing research direction.
/// </summary>
internal sealed class CreatePivotSnapshotTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public CreatePivotSnapshotTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "create_pivot_snapshot";
    public override string Description => "Create a snapshot before pivoting research direction. System keeps last 5 snapshots.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["reason"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["reason"] = new() { Type = "string", Description = "Why the pivot is being made", Required = true, MinLength = 1, MaxLength = 2000 }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        var reason = GetRequiredString(parameters, "reason");
        if (reason.Length > 2000) reason = reason[..2000];
        return await _access.CreatePivotSnapshotAsync(GetSessionId(context), reason, ct);
    }
}

/// <summary>
/// Tool for listing all pivot snapshots.
/// </summary>
internal sealed class GetPivotSnapshotsTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public GetPivotSnapshotsTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "get_pivot_snapshots";
    public override string Description => "Get all pivot snapshots for the current session (up to 5).";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new() { Required = [], Items = new Dictionary<string, ToolParameter>() };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        return await _access.GetPivotSnapshotsAsync(GetSessionId(context), ct);
    }
}
