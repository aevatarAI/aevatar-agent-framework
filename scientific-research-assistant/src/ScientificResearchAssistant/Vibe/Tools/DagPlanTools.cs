using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace ScientificResearchAssistant.Vibe.Tools;

// ============================================================
//  DAG Plan tools (write)
//
//  Purpose:
//  - Allow the "research assistant" agent to update DAG plan nodes via tool-calling
//    based on natural language user messages.
//
//  Safety:
//  - Writes are scoped to "milestone plan nodes" (planKind=milestone) owned by the session.
// ============================================================

public sealed record DagPlanMilestone(int RoundIndex, string ExpectedOutput);

public interface IVibeDagPlanAccess
{
    Task<Struct> SetMilestonesAsync(
        string sessionId,
        IReadOnlyList<DagPlanMilestone> milestones,
        string? note,
        bool deleteOthers,
        CancellationToken ct);
}

internal sealed class DagPlanSetMilestonesTool : AevatarToolBase
{
    private readonly IVibeDagPlanAccess _plans;

    public DagPlanSetMilestonesTool(IVibeDagPlanAccess plans)
    {
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
    }

    public override string Name => "dag_plan_set_milestones";
    public override string Description =>
        "Replace/update the DAG milestone plan nodes for this session. Use when the user asks to change/adjust the research plan.";
    public override ToolCategory Category => ToolCategory.Utility;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = ["milestones"],
            Items = new Dictionary<string, ToolParameter>
            {
                ["milestones"] = new ToolParameter
                {
                    Type = "array",
                    Description = "Milestones array. Each item: { roundIndex: int (0 allowed), expectedOutput: string }. Keep 2-8 items.",
                    Required = true
                },
                ["note"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Optional short note describing the change.",
                    Required = false,
                    MaxLength = 800
                },
                ["deleteOthers"] = new ToolParameter
                {
                    Type = "boolean",
                    Description = "If true, mark any existing milestone plan nodes not present in this update as deleted (default true).",
                    Required = false,
                    DefaultValue = true
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

        var milestones = ParseMilestones(parameters.GetValueOrDefault("milestones"));
        if (milestones.Count == 0)
            throw new ArgumentException("milestones is required and must be non-empty");

        var note = (parameters.GetValueOrDefault("note")?.ToString() ?? string.Empty).Replace("\r", "").Trim();
        if (note.Length > 800) note = note[..800];

        var deleteOthers = ParseBool(parameters.GetValueOrDefault("deleteOthers"), fallback: true);

        return await _plans.SetMilestonesAsync(sid, milestones, note.Length == 0 ? null : note, deleteOthers, cancellationToken);
    }

    private static bool ParseBool(object? v, bool fallback)
    {
        if (v == null) return fallback;
        if (v is bool b) return b;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var bb)) return bb;
        }
        return bool.TryParse(v.ToString(), out var x) ? x : fallback;
    }

    private static List<DagPlanMilestone> ParseMilestones(object? v)
    {
        var list = new List<DagPlanMilestone>();

        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var round = item.TryGetProperty("roundIndex", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
                var expected = item.TryGetProperty("expectedOutput", out var e) ? (e.GetString() ?? "") : "";
                expected = expected.Replace("\r", "").Trim();
                if (expected.Length == 0) continue;
                list.Add(new DagPlanMilestone(Math.Clamp(round, 0, 200), expected));
                if (list.Count >= 12) break;
            }
            return list;
        }

        // Fallback: accept JSON string.
        if (v is string s && s.TrimStart().StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(s);
                return ParseMilestones(doc.RootElement);
            }
            catch
            {
                return list;
            }
        }

        // Fallback: IEnumerable<object>
        if (v is IEnumerable<object> arr)
        {
            foreach (var o in arr)
            {
                if (o is JsonElement e2)
                {
                    var inner = ParseMilestones(e2);
                    list.AddRange(inner);
                    break;
                }
            }
        }

        if (list.Count > 12) list = list.Take(12).ToList();
        return list;
    }
}


