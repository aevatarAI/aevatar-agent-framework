using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.Knowledge.Graph.Models;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace VibeResearching.Vibe.Tools;

/// <summary>
/// Tool for creating a research plan with sequential steps.
/// </summary>
internal sealed class CreatePlanTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public CreatePlanTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "create_research_plan";
    public override string Description => "Create a research plan with sequential steps. Each step is a PlanNode with status tracking.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["steps"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["steps"] = new()
            {
                Type = "array",
                Description = "Array of plan steps: { nodeId, coreDescription, detailedDescription, methodology?, sequentialOrder, promotesNodeIds?, dependsOnNodeIds? }",
                Required = true,
                Items = new() { Type = "object", Description = "Plan step object" }
            }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        var sessionId = GetSessionId(context);
        var steps = ParsePlanSteps(parameters.GetValueOrDefault("steps"));
        if (steps.Count == 0) throw new ArgumentException("steps is required and must be non-empty");
        return await _access.CreatePlanAsync(sessionId, steps, ct);
    }

    private static List<PlanStepInput> ParsePlanSteps(object? v)
    {
        var list = new List<PlanStepInput>();
        if (v is not JsonElement je || je.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in je.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var nodeId = item.GetPropertyOrDefault("nodeId", "");
            var core = item.GetPropertyOrDefault("coreDescription", "");
            if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(core)) continue;

            var detail = item.GetPropertyOrDefault("detailedDescription", core);
            var methodology = item.GetPropertyOrNull("methodology");
            var order = item.TryGetProperty("sequentialOrder", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : list.Count + 1;
            var promotes = item.GetStringArray("promotesNodeIds");
            var dependsOn = item.GetStringArray("dependsOnNodeIds");

            list.Add(new PlanStepInput(nodeId.Trim(), core.Trim(), detail.Trim(), methodology?.Trim(), order, promotes, dependsOn));
            if (list.Count >= 20) break;
        }
        return list;
    }
}

/// <summary>
/// Tool for updating the status of a plan node.
/// </summary>
internal sealed class UpdatePlanStatusTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public UpdatePlanStatusTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "update_plan_status";
    public override string Description => "Update plan node status. Valid: Pending→Active, Active→Completed, Active→Pending.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["nodeId", "status"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["nodeId"] = new() { Type = "string", Description = "Plan node ID", Required = true, MinLength = 1, MaxLength = 200 },
            ["status"] = new() { Type = "string", Description = "New status: Pending, Active, or Completed", Required = true },
            ["progressText"] = new() { Type = "string", Description = "Optional progress description", Required = false, MaxLength = 2000 }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        var sessionId = GetSessionId(context);
        var nodeId = GetRequiredString(parameters, "nodeId");
        var statusStr = GetRequiredString(parameters, "status");

        if (!Enum.TryParse<PlanNodeStatus>(statusStr, ignoreCase: true, out var status))
            throw new ArgumentException($"Invalid status: {statusStr}");

        var progressText = parameters.GetValueOrDefault("progressText")?.ToString();
        if (progressText?.Length > 2000) progressText = progressText[..2000];

        return await _access.UpdatePlanStatusAsync(sessionId, nodeId, status, progressText, ct);
    }
}

/// <summary>
/// Tool for getting the current research plan.
/// </summary>
internal sealed class GetPlanTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public GetPlanTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "get_research_plan";
    public override string Description => "Get all plan nodes for the current session, ordered by sequence.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new() { Required = [], Items = new Dictionary<string, ToolParameter>() };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        return await _access.GetPlanNodesAsync(GetSessionId(context), ct);
    }
}

/// <summary>
/// Tool for checking if a plan node can be deleted.
/// </summary>
internal sealed class CanDeletePlanTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public CanDeletePlanTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "can_delete_plan";
    public override string Description => "Check if a plan node can be safely deleted (no linked knowledge).";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["nodeId"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["nodeId"] = new() { Type = "string", Description = "Plan node ID to check", Required = true, MinLength = 1, MaxLength = 200 }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        return await _access.CanDeletePlanAsync(GetSessionId(context), GetRequiredString(parameters, "nodeId"), ct);
    }
}

/// <summary>
/// Tool for deleting a plan node.
/// </summary>
internal sealed class DeletePlanTool : VibeToolBase
{
    private readonly IVibeGraphAccess _access;

    public DeletePlanTool(IVibeGraphAccess access) => _access = access ?? throw new ArgumentNullException(nameof(access));

    public override string Name => "delete_plan_node";
    public override string Description => "Delete a plan node. Only succeeds if no knowledge is linked. Use can_delete_plan first.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters() => new()
    {
        Required = ["nodeId"],
        Items = new Dictionary<string, ToolParameter>
        {
            ["nodeId"] = new() { Type = "string", Description = "Plan node ID to delete", Required = true, MinLength = 1, MaxLength = 200 }
        }
    };

    public override async Task<IMessage> ExecuteAsync(Dictionary<string, object> parameters, ToolContext context, ILogger? logger, CancellationToken ct = default)
    {
        return await _access.DeletePlanAsync(GetSessionId(context), GetRequiredString(parameters, "nodeId"), ct);
    }
}
