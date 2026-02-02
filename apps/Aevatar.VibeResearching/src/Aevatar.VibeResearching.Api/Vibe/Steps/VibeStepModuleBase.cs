using System.Text.Json;
using Aevatar.Agents.Cognitive.Execution;
using VibeResearching.Api.Vibe;

namespace VibeResearching.Api.Vibe.Steps;

internal abstract class VibeStepModuleBase : CognitiveStepModuleBase
{
    protected static string ResolveSessionId(IWorkflowCoordinatorRuntime coordinator)
        => ResolveStringVar(coordinator, "session_id");

    protected static string ResolveRunId(IWorkflowCoordinatorRuntime coordinator)
    {
        var runId = ResolveStringVar(coordinator, "run_id");
        if (runId.Length > 0) return runId;
        runId = ResolveStringVar(coordinator, "request_id");
        return runId;
    }

    protected static string ResolveQuestion(IWorkflowCoordinatorRuntime coordinator)
    {
        var question = ResolveStringVar(coordinator, "question");
        if (question.Length > 0) return question;
        question = ResolveStringVar(coordinator, "task");
        if (question.Length > 0) return question;
        return ResolveStringVar(coordinator, "message");
    }

    protected static string ResolveStringVar(IWorkflowCoordinatorRuntime coordinator, string key, string fallback = "")
    {
        if (coordinator.TryGetWorkflowVariable(key, out var value) && value != null)
            return value.ToString() ?? fallback;
        return fallback;
    }

    protected static int ResolveIntVar(IWorkflowCoordinatorRuntime coordinator, string key, int fallback)
    {
        if (!coordinator.TryGetWorkflowVariable(key, out var value) || value == null)
            return fallback;

        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            JsonElement el when el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var j) => j,
            JsonElement el when el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var j) => j,
            _ => fallback
        };
    }

    protected static bool ResolveBoolVar(IWorkflowCoordinatorRuntime coordinator, string key, bool fallback)
    {
        if (!coordinator.TryGetWorkflowVariable(key, out var value) || value == null)
            return fallback;

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            JsonElement el when el.ValueKind == JsonValueKind.True => true,
            JsonElement el when el.ValueKind == JsonValueKind.False => false,
            _ => fallback
        };
    }

    protected static List<string> ResolveStringListVar(IWorkflowCoordinatorRuntime coordinator, string key)
    {
        if (!coordinator.TryGetWorkflowVariable(key, out var value) || value == null)
            return [];

        return NormalizeStringList(value);
    }

    protected static string ResolveStringParameter(Dictionary<string, object?> parameters, string key, string fallback)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return fallback;
        var text = value.ToString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    protected static List<string> ResolveStringListParameter(Dictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return [];
        return NormalizeStringList(value);
    }

    protected static bool TryExtractJson(string raw, out string? json)
        => VibeWorkflowParsing.TryExtractJson(raw, out json);

    private static List<string> NormalizeStringList(object value)
    {
        switch (value)
        {
            case List<string> strings:
                return strings.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            case IEnumerable<object> objects:
                return objects
                    .Select(x => x?.ToString() ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToList();
            case JsonElement el when el.ValueKind == JsonValueKind.Array:
            {
                var list = new List<string>();
                foreach (var item in el.EnumerateArray())
                {
                    var text = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        list.Add(text!.Trim());
                }
                return list;
            }
            case string single when !string.IsNullOrWhiteSpace(single):
                return [single.Trim()];
            default:
                return [];
        }
    }
}
