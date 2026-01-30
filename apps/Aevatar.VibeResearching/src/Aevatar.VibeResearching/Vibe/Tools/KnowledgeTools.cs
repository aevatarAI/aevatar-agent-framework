using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace VibeResearching.Vibe.Tools;

/// <summary>
/// Tool for creating knowledge nodes with provenance tracking.
/// Each knowledge node MUST have a source (motivatedByPlanNodeId or dependsOnNodeIds).
/// </summary>
internal sealed class CreateKnowledgeTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public CreateKnowledgeTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "create_knowledge";
    public override string Description =>
        "Create knowledge nodes representing research findings. IMPORTANT: Each node MUST have a source - " +
        "either motivatedByPlanNodeId (from plan step) or dependsOnNodeIds (derived from existing knowledge).";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["entries"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["entries"] = new()
            {
                Type = "array",
                Description = "Array of knowledge entries: { nodeId, nodeType (Generic|MathAxiom|MathTheorem|...), " +
                              "coreDescription, detailedDescription, derivationProcess?, references?, proof?, " +
                              "motivatedByPlanNodeId?, dependsOnNodeIds? }",
                Required = true,
                Items = new() { Type = "object", Description = "Knowledge entry" }
            }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        var sessionId = GetSessionId(context);
        var entries = ParseKnowledgeEntries(parameters.GetValueOrDefault("entries"));
        if (entries.Count == 0) throw new ArgumentException("entries is required and must be non-empty");

        // Auto-fill missing motivatedByPlanNodeId with active milestone
        var activeMilestoneId = await GetActiveMilestoneIdAsync(sessionId, logger, ct);
        
        // Enforce provenance: each node must have a source
        var fixedEntries = new List<KnowledgeInput>();
        foreach (var e in entries)
        {
            var hasMotivatedBy = !string.IsNullOrWhiteSpace(e.MotivatedByPlanNodeId);
            var hasDependsOn = e.DependsOnNodeIds is { Count: > 0 };

            if (!hasMotivatedBy && !hasDependsOn)
            {
                // Auto-fill with active milestone if available
                if (!string.IsNullOrWhiteSpace(activeMilestoneId))
                {
                    logger?.LogDebug("[CreateKnowledgeTool] Auto-filling motivatedByPlanNodeId for '{NodeId}' with active milestone '{MilestoneId}'", 
                        e.NodeId, activeMilestoneId);
                    fixedEntries.Add(e with { MotivatedByPlanNodeId = activeMilestoneId });
                }
                else
                {
                    throw new ArgumentException(
                        $"Knowledge node '{e.NodeId}' must have a source. " +
                        "Specify either 'motivatedByPlanNodeId' or 'dependsOnNodeIds'. " +
                        "No active milestone found to use as default.");
                }
            }
            else
            {
                fixedEntries.Add(e);
            }
        }

        return await _access.CreateKnowledgeAsync(sessionId, fixedEntries, ct);
    }

    private async Task<string?> GetActiveMilestoneIdAsync(string sessionId, ILogger? logger, CancellationToken ct)
    {
        try
        {
            var planNodesResult = await _access.GetPlanNodesAsync(sessionId, ct);
            if (planNodesResult.Fields.TryGetValue("nodes", out var nodesValue))
            {
                // Parse the Struct to find active milestone
                var nodesJson = JsonSerializer.Serialize(nodesValue);
                using var doc = JsonDocument.Parse(nodesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var node in doc.RootElement.EnumerateArray())
                    {
                        if (node.TryGetProperty("status", out var status) &&
                            status.GetString() == "Active")
                        {
                            if (node.TryGetProperty("nodeId", out var nodeId))
                            {
                                return nodeId.GetString();
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[CreateKnowledgeTool] Failed to query active milestone (best-effort)");
        }
        return null;
    }

    private static List<KnowledgeInput> ParseKnowledgeEntries(object? v)
    {
        var list = new List<KnowledgeInput>();
        if (v is not JsonElement je || je.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in je.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var nodeId = item.GetPropertyOrDefault("nodeId", "");
            var core = item.GetPropertyOrDefault("coreDescription", "");
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(core)) continue;

            var nodeTypeStr = item.GetPropertyOrDefault("nodeType", "Generic");
            var nodeType = Enum.TryParse<KnowledgeNodeType>(nodeTypeStr, ignoreCase: true, out var nt) ? nt : KnowledgeNodeType.Generic;

            var detail = item.GetPropertyOrDefault("detailedDescription", core);
            var derivation = item.GetPropertyOrNull("derivationProcess");
            var refs = item.GetStringArray("references");
            var proof = item.GetPropertyOrNull("proof");
            var motivatedBy = item.GetPropertyOrNull("motivatedByPlanNodeId");
            var dependsOn = item.GetStringArray("dependsOnNodeIds");

            list.Add(new KnowledgeInput(
                nodeId.Trim(), nodeType, core.Trim(), detail.Trim(),
                derivation?.Trim(), refs, proof, motivatedBy?.Trim(), dependsOn));

            if (list.Count >= 50) break;
        }
        return list;
    }
}

/// <summary>
/// Tool for retrieving all knowledge nodes.
/// </summary>
internal sealed class GetKnowledgeTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public GetKnowledgeTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "get_knowledge";
    public override string Description => "Get all knowledge nodes for the current session.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new() { Required = [], Items = new Dictionary<string, ToolParameter>() };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        return await _access.GetKnowledgeNodesAsync(GetSessionId(context), ct);
    }
}

/// <summary>
/// Tool for linking a knowledge node to a plan node.
/// </summary>
internal sealed class LinkKnowledgeToPlanTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public LinkKnowledgeToPlanTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "link_knowledge_to_plan";
    public override string Description => "Link a knowledge node to a plan node (MotivatedBy relationship).";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["knowledgeNodeId", "planNodeId"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["knowledgeNodeId"] = new() { Type = "string", Description = "Knowledge node ID", Required = true, MinLength = 1, MaxLength = 200 },
            ["planNodeId"] = new() { Type = "string", Description = "Plan node that motivated this knowledge", Required = true, MinLength = 1, MaxLength = 200 }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        var sessionId = GetSessionId(context);
        var knowledgeNodeId = GetRequiredString(parameters, "knowledgeNodeId");
        var planNodeId = GetRequiredString(parameters, "planNodeId");
        return await _access.LinkKnowledgeToPlanAsync(sessionId, knowledgeNodeId, planNodeId, ct);
    }
}
