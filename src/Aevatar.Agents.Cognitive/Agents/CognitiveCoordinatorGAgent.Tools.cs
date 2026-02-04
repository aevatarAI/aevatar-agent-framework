using System.Text.Json;
using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CoordinatorAgent - Tool steps (tool_call/tool_evolve/tool_validate)
//
//  中文 + ASCII:
//  - Coordinator 侧负责工具演化与注册。
//  - Worker 侧只执行 tool_call/tool_validate。
// ============================================================
public partial class CoordinatorAgent
{
    internal async Task<PrimitiveResult> ExecuteToolCallAsync(StepDefinition step)
    {
        if (!ToolEvolutionOptions.EnableToolCalls)
            return PrimitiveResult.Fail("tool_call is disabled by ToolEvolutionOptions");

        var toolName = ResolveToolName(step);
        if (string.IsNullOrWhiteSpace(toolName))
            return PrimitiveResult.Fail("tool_call requires 'tool' parameter");

        var args = ResolveToolArguments(step.Parameters.GetValueOrDefault("args"));
        var sessionId = SessionId ?? CustomState.ExecutionId ?? Id.ToString();

        var result = await ExecuteToolForWorkflowAsync(toolName, args, sessionId, CancellationToken.None);
        if (!result.IsSuccess)
        {
            if (ToolEvolutionOptions.AutoTriggerOnToolFailure)
            {
                SetToolEvolutionTrigger("tool-failed", step, toolName);
            }

            return PrimitiveResult.Fail(result.ErrorMessage ?? "tool_call failed");
        }

        var outputType = step.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        var strictParse = ResolveBoolParameter(step.Parameters, "strict_parse", false);
        var content = result.Content ?? string.Empty;
        var parsed = outputType.Equals("text", StringComparison.OrdinalIgnoreCase)
            ? content
            : ParseOutput(content, outputType);

        if (parsed == null && strictParse)
        {
            if (ToolEvolutionOptions.AutoTriggerOnParseFailure)
            {
                SetToolEvolutionTrigger("tool-parse-failed", step, toolName);
            }

            return PrimitiveResult.Fail("tool-parse-null");
        }

        return PrimitiveResult.Ok(parsed ?? content);
    }

    internal async Task<PrimitiveResult> ExecuteToolValidateAsync(StepDefinition step)
    {
        if (!ToolEvolutionOptions.EnableToolCalls)
            return PrimitiveResult.Fail("tool_validate is disabled by ToolEvolutionOptions");

        var toolName = ResolveToolName(step);
        if (string.IsNullOrWhiteSpace(toolName))
            return PrimitiveResult.Fail("tool_validate requires 'tool' parameter");

        var args = ResolveToolArguments(step.Parameters.GetValueOrDefault("args")
            ?? step.Parameters.GetValueOrDefault("validation_args"));
        var sessionId = SessionId ?? CustomState.ExecutionId ?? Id.ToString();

        var result = await ExecuteToolForWorkflowAsync(toolName, args, sessionId, CancellationToken.None);
        return result.IsSuccess
            ? PrimitiveResult.Ok(result.Content ?? string.Empty)
            : PrimitiveResult.Fail(result.ErrorMessage ?? "tool_validate failed");
    }

    internal async Task<PrimitiveResult> ExecuteToolEvolveAsync(StepDefinition step)
    {
        if (!ToolEvolutionOptions.Enabled || !ToolEvolutionOptions.EnableToolEvolutionSteps)
            return PrimitiveResult.Fail("tool_evolve is disabled by ToolEvolutionOptions");

        var candidates = await ResolveToolCandidatesAsync(step);
        if (candidates.Count == 0)
            return PrimitiveResult.Fail("tool_evolve has no candidates");

        var policy = ResolveEvolutionPolicy(step.Parameters);
        var maxCandidates = ResolveIntParameter(step.Parameters, "max_candidates", 3);
        var validationArgs = ResolveToolArguments(step.Parameters.GetValueOrDefault("validation_args"));

        var result = new ToolEvolutionResult
        {
            EvolutionId = Guid.NewGuid().ToString("N"),
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };

        var options = BuildToolEvolutionOptionsOverride(step.Parameters);

        foreach (var candidate in candidates.Take(Math.Max(1, maxCandidates)))
        {
            try
            {
                var tool = await ToolCandidateMaterializer.MaterializeAsync(
                    candidate,
                    options,
                    Logger,
                    CancellationToken.None);

                var toolDef = await RegisterToolDefinitionAsync(tool, Logger, CancellationToken.None);
                if (ToolEvolutionRegistry != null)
                {
                    await ToolEvolutionRegistry.RegisterEvolvedToolAsync(
                        toolDef,
                        policy,
                        CancellationToken.None);
                }

                if (validationArgs.Count > 0)
                {
                    var validation = await ExecuteToolForWorkflowAsync(
                        tool.Name,
                        validationArgs,
                        SessionId ?? CustomState.ExecutionId ?? Id.ToString(),
                        CancellationToken.None);

                    if (!validation.IsSuccess)
                    {
                        candidate.Metadata["validation_error"] =
                            validation.ErrorMessage ?? "tool validation failed";
                        result.RejectedTools.Add(candidate);
                        continue;
                    }
                }

                result.AcceptedTools.Add(candidate);
            }
            catch (Exception ex)
            {
                candidate.Metadata["exception"] = ex.Message;
                result.RejectedTools.Add(candidate);
            }
        }

        if (ToolEvolutionOptions.EnableMetrics)
        {
            result.MetricsSnapshot = ToolMetricsStore.BuildSnapshot();
        }

        result.Success = result.AcceptedTools.Count > 0;
        result.ErrorMessage = result.Success ? string.Empty : "No tool candidate accepted.";

        return new PrimitiveResult
        {
            Success = result.Success,
            Value = result,
            AssistantResponse = result.Success
                ? $"tool_evolve accepted {result.AcceptedTools.Count} tools"
                : result.ErrorMessage
        };
    }

    private string ResolveToolName(StepDefinition step)
    {
        if (step.Parameters.TryGetValue("tool", out var toolObj) && toolObj != null)
            return _templateEngine.Render(toolObj.ToString() ?? string.Empty, _workflowVariables).Trim();

        if (step.Parameters.TryGetValue("tool_name", out var toolName) && toolName != null)
            return _templateEngine.Render(toolName.ToString() ?? string.Empty, _workflowVariables).Trim();

        return string.Empty;
    }

    private Dictionary<string, object> ResolveToolArguments(object? argsObj)
    {
        if (argsObj == null)
            return new Dictionary<string, object>();

        if (argsObj is Dictionary<string, object> dict)
            return RenderArgs(dict);

        if (argsObj is Dictionary<string, object?> dictNullable)
        {
            var mapped = dictNullable.ToDictionary(k => k.Key, v => v.Value);
            return RenderArgs(mapped);
        }

        if (argsObj is JsonElement element)
        {
            var raw = element.GetRawText();
            return ParseArgsJson(raw);
        }

        if (argsObj is string text)
        {
            var rendered = _templateEngine.Render(text, _workflowVariables);
            return ParseArgsJson(rendered);
        }

        return new Dictionary<string, object>
        {
            ["input"] = argsObj
        };
    }

    private Dictionary<string, object> RenderArgs(Dictionary<string, object?> args)
    {
        var result = new Dictionary<string, object>(args.Count, StringComparer.Ordinal);
        foreach (var (key, value) in args)
        {
            result[key] = RenderArgValue(value);
        }

        return result;
    }

    private object RenderArgValue(object? value)
    {
        if (value == null)
            return string.Empty;

        if (value is string s)
            return _templateEngine.Render(s, _workflowVariables);

        if (value is Dictionary<string, object?> dict)
            return RenderArgs(dict);

        if (value is IEnumerable<object?> list)
            return list.Select(RenderArgValue).ToList();

        return value;
    }

    private static Dictionary<string, object> ParseArgsJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new Dictionary<string, object>();

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(text);
            if (parsed != null)
                return parsed;
        }
        catch
        {
            // best-effort
        }

        return new Dictionary<string, object>
        {
            ["input"] = text
        };
    }

    private async Task<List<ToolCandidate>> ResolveToolCandidatesAsync(StepDefinition step)
    {
        if (step.Parameters.TryGetValue("candidates", out var candidatesObj) && candidatesObj != null)
        {
            if (candidatesObj is string key)
            {
                var rendered = _templateEngine.Render(key, _workflowVariables).Trim();
                if (_workflowVariables.TryGetValue(rendered, out var fromVars))
                    return ParseCandidates(fromVars);

                return ParseCandidates(rendered);
            }

            return ParseCandidates(candidatesObj);
        }

        if (step.Generator == null)
            return new List<ToolCandidate>();

        var useVote = ResolveBoolParameter(step.Parameters, "use_vote", true);
        PrimitiveResult result;
        if (useVote)
        {
            var voteStep = new StepDefinition
            {
                Id = $"{step.Id}.vote",
                Type = "vote",
                Generator = step.Generator,
                Parameters = new Dictionary<string, object?>(step.Parameters)
            };
            result = await ExecuteVoteAsync(voteStep);
        }
        else
        {
            result = await ExecuteLlmCallDirectAsync(step.Generator);
        }

        if (!result.Success)
            return new List<ToolCandidate>();

        var source = result.Value ?? result.AssistantResponse;
        return ParseCandidates(source);
    }

    private List<ToolCandidate> ParseCandidates(object? source)
    {
        var list = new List<ToolCandidate>();
        if (source == null)
            return list;

        if (source is string rawText)
        {
            var trimmed = rawText.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            if (TryParseCandidate(el.GetRawText(), out var item))
                                list.Add(item);
                        }

                        return list;
                    }
                }
                catch
                {
                    // fall through
                }
            }
        }

        if (source is IEnumerable<object> items)
        {
            foreach (var item in items)
            {
                if (TryParseCandidate(item, out var candidate))
                    list.Add(candidate);
            }
            return list;
        }

        if (TryParseCandidate(source, out var single))
            list.Add(single);

        return list;
    }

    private static bool TryParseCandidate(object source, out ToolCandidate candidate)
    {
        candidate = new ToolCandidate();

        if (source is ToolCandidate proto)
        {
            candidate = proto;
            return true;
        }

        if (source is string text)
        {
            return TryParseCandidateJson(text, out candidate);
        }

        try
        {
            var json = JsonSerializer.Serialize(source);
            return TryParseCandidateJson(json, out candidate);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseCandidateJson(string json, out ToolCandidate candidate)
    {
        candidate = new ToolCandidate();
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            candidate = JsonParser.Default.Parse<ToolCandidate>(json);
            return !string.IsNullOrWhiteSpace(candidate.ToolName);
        }
        catch
        {
            return false;
        }
    }

    private ToolEvolutionPolicy ResolveEvolutionPolicy(Dictionary<string, object?> parameters)
    {
        var basePolicy = ToolEvolutionOptions.DefaultPolicy ?? new ToolEvolutionPolicy();
        var policy = new ToolEvolutionPolicy
        {
            Strategy = basePolicy.Strategy,
            CanaryPercentage = basePolicy.CanaryPercentage,
            RollbackFailureRate = basePolicy.RollbackFailureRate,
            MinCallsBeforeRollback = basePolicy.MinCallsBeforeRollback
        };

        if (parameters.TryGetValue("policy", out var policyObj) &&
            policyObj is Dictionary<string, object?> dict)
        {
            if (dict.TryGetValue("strategy", out var strategyObj) && strategyObj != null)
            {
                var raw = strategyObj.ToString()?.Trim().ToLowerInvariant();
                policy.Strategy = raw == "canary" ? ToolEvolutionStrategy.Canary : ToolEvolutionStrategy.Replace;
            }

            if (dict.TryGetValue("canary_percentage", out var pctObj) && pctObj != null)
            {
                if (double.TryParse(pctObj.ToString(), out var pct))
                    policy.CanaryPercentage = Math.Clamp(pct, 0, 1);
            }

            if (dict.TryGetValue("rollback_failure_rate", out var rateObj) && rateObj != null)
            {
                if (double.TryParse(rateObj.ToString(), out var rate))
                    policy.RollbackFailureRate = Math.Clamp(rate, 0, 1);
            }

            if (dict.TryGetValue("min_calls_before_rollback", out var minObj) && minObj != null)
            {
                if (int.TryParse(minObj.ToString(), out var minCalls))
                    policy.MinCallsBeforeRollback = Math.Max(1, minCalls);
            }
        }

        return policy;
    }

    private ToolEvolutionOptions BuildToolEvolutionOptionsOverride(Dictionary<string, object?> parameters)
    {
        var options = ToolEvolutionOptions;
        if (parameters.TryGetValue("tool_storage_dir", out var dirObj) && dirObj != null)
        {
            var overrideDir = dirObj.ToString();
            if (!string.IsNullOrWhiteSpace(overrideDir))
            {
                return new ToolEvolutionOptions
                {
                    Enabled = options.Enabled,
                    EnableFeedbackHooks = options.EnableFeedbackHooks,
                    EnableMetrics = options.EnableMetrics,
                    EnableFeedbackEvents = options.EnableFeedbackEvents,
                    EnableMemoryStoreAppend = options.EnableMemoryStoreAppend,
                    MetricsSnapshotEveryNCalls = options.MetricsSnapshotEveryNCalls,
                    DefaultPolicy = options.DefaultPolicy,
                    ToolStorageDirectory = overrideDir.Trim(),
                    EnableToolCalls = options.EnableToolCalls,
                    EnableToolEvolutionSteps = options.EnableToolEvolutionSteps,
                    AutoTriggerOnParseFailure = options.AutoTriggerOnParseFailure,
                    AutoTriggerOnToolFailure = options.AutoTriggerOnToolFailure
                };
            }
        }

        return options;
    }

    private void SetToolEvolutionTrigger(string reason, StepDefinition step, string? toolName)
    {
        var payload = new Dictionary<string, object>
        {
            ["reason"] = reason,
            ["step_id"] = step.Id ?? string.Empty,
            ["step_type"] = step.Type ?? string.Empty,
            ["tool_name"] = toolName ?? string.Empty,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };

        _workflowVariables["_tool_evolve_trigger"] = payload;
    }
}
