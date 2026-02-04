namespace Aevatar.VibeResearching.Agents.Mesh;

// ============================================================
//  SraMeshMappings (SSoT)
//
//  What:
//  - Central allowlist + human-readable descriptions for the Mesh DSL elements
//    that SRA "vibe" can execute (Option B).
//
//  Why:
//  - Avoid duplicated, drifting allowlists across compiler/planner/runner/API.
//  - Keep errors actionable for humans and LLM self-repair loops.
//
//  Notes:
//  - v1 is intentionally minimal: only the existing SRA worker roles.
// ============================================================

public static class SraMeshMappings
{
    // ------------------------------------------------------------
    //  Node types (NodeSpec.type)
    // ------------------------------------------------------------

    public const string NodePlanner = "planner";
    public const string NodeReasoner = "reasoner";
    public const string NodeLibrarian = "librarian";
    public const string NodeVerifier = "verifier";
    public const string NodeDagBuilder = "dag_builder";

    public static readonly IReadOnlyList<string> AllowedNodeTypes =
    [
        NodePlanner,
        NodeReasoner,
        NodeLibrarian,
        NodeVerifier,
        NodeDagBuilder
    ];

    public static bool IsSupportedNodeType(string? type)
    {
        type = (type ?? string.Empty).Trim();
        if (type.Length == 0) return false;
        return AllowedNodeTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
    }

    public static string DescribeNodeType(string? type)
    {
        type = (type ?? string.Empty).Trim().ToLowerInvariant();
        return type switch
        {
            NodePlanner => "planner: produce executable plan / unknowns",
            NodeReasoner => "reasoner: grounded reasoning (optional python_exec)",
            NodeLibrarian => "librarian: evidence + missing gaps",
            NodeVerifier => "verifier: hard checks / red flags",
            NodeDagBuilder => "dag_builder: propose DAG mutation candidate (strict JSON)",
            _ => "unknown node type"
        };
    }

    // ------------------------------------------------------------
    //  Constraint types (ConstraintSpec.type)
    //
    //  NOTE:
    //  - The underlying DSL module has its own defaults; we keep SRA explicit.
    // ------------------------------------------------------------

    public static readonly IReadOnlyList<string> AllowedConstraintTypes =
    [
        "confidence_threshold",
        "max_iterations"
    ];

    public static bool IsSupportedConstraintType(string? type)
    {
        type = (type ?? string.Empty).Trim();
        if (type.Length == 0) return false;
        return AllowedConstraintTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------
    //  Edge channels (EdgeSpec.channel)
    // ------------------------------------------------------------

    public const string ChannelQuestion = "question";
    public const string ChannelMaterials = "materials";
    public const string ChannelDagSnapshot = "dag_snapshot";
    public const string ChannelUpstreamOutput = "upstream_output";
    public const string ChannelPlannerOutput = "planner_output";

    public static readonly IReadOnlyList<string> AllowedChannels =
    [
        ChannelQuestion,
        ChannelMaterials,
        ChannelDagSnapshot,
        ChannelUpstreamOutput,
        ChannelPlannerOutput
    ];

    public static bool IsSupportedChannel(string? channel)
    {
        channel = (channel ?? string.Empty).Trim();
        if (channel.Length == 0) return false;
        return AllowedChannels.Contains(channel, StringComparer.OrdinalIgnoreCase);
    }

    public static string DescribeChannel(string? channel)
    {
        channel = (channel ?? string.Empty).Trim().ToLowerInvariant();
        return channel switch
        {
            ChannelQuestion => "Pass the user question text",
            ChannelMaterials => "Pass rendered materials context (DAG facts)",
            ChannelDagSnapshot => "Pass bounded DAG snapshot context",
            ChannelUpstreamOutput => "Pass bounded output of the upstream node",
            ChannelPlannerOutput => "Pass bounded output of node 'planner' (if present)",
            _ => "unknown channel"
        };
    }
}


