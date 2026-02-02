using System.Text.Json;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.AGUI;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Vibe.Steps;

internal sealed class VibeTraceAppendStepModule : VibeStepModuleBase
{
    private readonly TraceStore _trace;
    private readonly WorkspaceService _workspace;
    private readonly ResearchSessionManager _sessions;
    private readonly ILogger<VibeTraceAppendStepModule> _logger;

    public VibeTraceAppendStepModule(
        TraceStore trace,
        WorkspaceService workspace,
        ResearchSessionManager sessions,
        ILogger<VibeTraceAppendStepModule> logger)
    {
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "vibe_trace_append";
    public override string StepType => "vibe_trace_append";

    public override async Task<PrimitiveResult> ExecuteAsync(
        IWorkflowCoordinatorRuntime coordinator,
        StepDefinition step,
        string? preRenderedPrompt,
        string? preRenderedSystem,
        CancellationToken ct)
    {
        var sessionId = ResolveSessionId(coordinator);
        if (string.IsNullOrWhiteSpace(sessionId))
            return PrimitiveResult.Fail("vibe_trace_append requires session_id");

        var runId = ResolveRunId(coordinator);
        var question = ResolveQuestion(coordinator);

        var outputKeys = ResolveStringListParameter(step.Parameters, "outputs");
        if (outputKeys.Count == 0)
        {
            outputKeys = ["planner", "reasoner", "librarian", "verifier", "dag_builder"];
        }

        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in outputKeys)
        {
            var value = ResolveStringVar(coordinator, key);
            if (value.Length > 0)
                outputs[key] = value;
        }

        var summaryVar = ResolveStringParameter(step.Parameters, "summary_var", "round_summary");
        var summaryMarkdown = ResolveStringVar(coordinator, summaryVar);

        var dagApplyVar = ResolveStringParameter(step.Parameters, "dag_apply_var", "dag_apply");
        var dagResult = BuildDagResult(coordinator, dagApplyVar);

        var session = _sessions.GetOrCreate(sessionId);
        await PersistTraceAsync(session, runId, question, outputs, dagResult, summaryMarkdown, ct);

        return PrimitiveResult.Ok(new Dictionary<string, object>
        {
            ["run_id"] = runId,
            ["outputs"] = outputs,
            ["summary"] = summaryMarkdown
        });
    }

    private async Task PersistTraceAsync(
        ResearchSession session,
        string runId,
        string question,
        IReadOnlyDictionary<string, string> outputs,
        DagRoundResult dagResult,
        string? summaryMarkdown,
        CancellationToken ct)
    {
        var sessionId = session.Id;
        var now = Timestamp.FromDateTime(DateTime.UtcNow);

        var prev = await _trace.LoadLatestAsync(sessionId, max: 1, ct);
        var roundIdx = prev.Count == 0 ? 0 : prev[^1].RoundIndex + 1;

        var round = new SraRoundSummary
        {
            SessionId = sessionId,
            RunId = runId,
            RoundIndex = roundIdx,
            TriggerKind = "user_message",
            TriggerRef = string.Empty,
            UpdatedAt = now
        };

        foreach (var (agent, text) in outputs)
        {
            var s = new SraRoundAgentSummary { Agent = agent };
            s.Highlights.AddRange(ExtractHighlights(text, max: 4));
            round.PerAgent.Add(s);
        }

        if (dagResult.Accepted && dagResult.AcceptedNodes.Count > 0)
        {
            foreach (var (id, type) in dagResult.AcceptedNodes)
            {
                round.DagChanges.Add(new SraRoundDagChange
                {
                    NodeId = id,
                    NodeType = type,
                    Change = "accepted"
                });
            }
        }

        round.Metrics["question_len"] = question.Length.ToString();
        round.Metrics["agents"] = outputs.Count.ToString();

        await _trace.AppendAsync(sessionId, round, summaryMarkdown, ct);

        try
        {
            var ws = _workspace.EnsureSessionWorkspace(sessionId);
            var summaryAbs = Path.Combine(ws.RunsDir, runId, "summary.md");
            var summaryRel = Path.GetRelativePath(ws.SessionRoot, summaryAbs).Replace('\\', '/').Trim('/');

            var preview = (summaryMarkdown ?? string.Empty).Replace("\r", "").Trim();

            session.Events.Publish(new CustomEvent
            {
                Timestamp = NowMs(),
                Name = "aevatar.vibe.round_summary",
                Value = new
                {
                    sessionId,
                    runId,
                    roundIndex = roundIdx,
                    summaryPath = summaryRel,
                    preview
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to publish round_summary (best-effort).");
        }
    }

    private static IReadOnlyList<string> ExtractHighlights(string text, int max)
    {
        max = Math.Clamp(max, 0, 10);
        if (max == 0) return [];

        var lines = (text ?? string.Empty)
            .Replace("\r", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return lines.Take(max).ToList();
    }

    private static DagRoundResult BuildDagResult(IWorkflowCoordinatorRuntime coordinator, string dagApplyVar)
    {
        if (!coordinator.TryGetWorkflowVariable(dagApplyVar, out var value) || value == null)
            return new DagRoundResult(false, false, [], []);

        if (value is Dictionary<string, object?> dict)
            return ParseDagApplyDict(dict);

        if (value is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in el.EnumerateObject())
                map[p.Name] = p.Value;
            return ParseDagApplyDict(map);
        }

        return new DagRoundResult(false, false, [], []);
    }

    private static DagRoundResult ParseDagApplyDict(Dictionary<string, object?> dict)
    {
        var accepted = ReadBool(dict, "accepted");
        var blocked = ReadBool(dict, "blocked");
        var redFlags = ReadStringList(dict, "red_flags");
        var acceptedNodes = ReadAcceptedNodes(dict);

        return new DagRoundResult(
            accepted,
            blocked,
            acceptedNodes,
            redFlags);
    }

    private static bool ReadBool(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value) || value == null)
            return false;

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            JsonElement el when el.ValueKind == JsonValueKind.True => true,
            JsonElement el when el.ValueKind == JsonValueKind.False => false,
            _ => false
        };
    }

    private static List<string> ReadStringList(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value) || value == null)
            return [];

        return value switch
        {
            List<string> list => list.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList(),
            IEnumerable<object> objs => objs.Select(x => x?.ToString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList(),
            JsonElement el when el.ValueKind == JsonValueKind.Array =>
                el.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .ToList(),
            string single when !string.IsNullOrWhiteSpace(single) => [single.Trim()],
            _ => []
        };
    }

    private static List<(string Id, SraDagNodeType Type)> ReadAcceptedNodes(Dictionary<string, object?> dict)
    {
        if (!dict.TryGetValue("accepted_nodes", out var value) || value == null)
            return [];

        var result = new List<(string Id, SraDagNodeType Type)>();

        void AddEntry(object? entry)
        {
            if (entry == null) return;

            if (entry is Dictionary<string, object?> map)
            {
                if (!map.TryGetValue("id", out var idObj)) return;
                var id = (idObj?.ToString() ?? string.Empty).Trim();
                if (id.Length == 0) return;
                var type = map.TryGetValue("type", out var typeObj) ? ParseNodeType(typeObj?.ToString()) : SraDagNodeType.Unknown;
                result.Add((id, type));
                return;
            }

            if (entry is JsonElement el && el.ValueKind == JsonValueKind.Object)
            {
                var id = el.TryGetProperty("id", out var idProp) ? (idProp.GetString() ?? string.Empty).Trim() : string.Empty;
                if (id.Length == 0) return;
                var type = el.TryGetProperty("type", out var typeProp)
                    ? ParseNodeType(typeProp.ValueKind == JsonValueKind.String ? typeProp.GetString() : typeProp.ToString())
                    : SraDagNodeType.Unknown;
                result.Add((id, type));
                return;
            }

            var fallbackId = entry.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(fallbackId))
                result.Add((fallbackId!, SraDagNodeType.Unknown));
        }

        switch (value)
        {
            case IEnumerable<object> objs:
                foreach (var obj in objs) AddEntry(obj);
                break;
            case JsonElement el when el.ValueKind == JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) AddEntry(item);
                break;
            default:
                AddEntry(value);
                break;
        }

        return result;
    }

    private static SraDagNodeType ParseNodeType(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) return SraDagNodeType.Unknown;
        value = value.Replace("SRA_DAG_NODE_TYPE_", "", StringComparison.OrdinalIgnoreCase);
        return System.Enum.TryParse<SraDagNodeType>(value, ignoreCase: true, out var parsed)
            ? parsed
            : SraDagNodeType.Unknown;
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed record DagRoundResult(
        bool Accepted,
        bool Blocked,
        IReadOnlyList<(string Id, SraDagNodeType Type)> AcceptedNodes,
        IReadOnlyList<string> RedFlags);
}
