using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Utilities;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Cognitive.Execution;

public sealed class CognitiveStepExecutionHandler : IStepExecutionHandler
{
    private readonly TemplateEngine _templateEngine = new();
    private readonly OutputParserFactory _parserFactory = new();

    public bool CanHandle(EventEnvelope envelope)
        => envelope.Payload?.Is(ExecuteStepRequestEvent.Descriptor) ?? false;

    public async Task<StepExecutionResult?> HandleAsync(
        EventEnvelope envelope,
        AIGAgentBase agent,
        CancellationToken ct)
    {
        if (envelope.Payload == null)
            return null;

        ExecuteStepRequestEvent request;
        try
        {
            request = envelope.Payload.Unpack<ExecuteStepRequestEvent>();
        }
        catch
        {
            return null;
        }

        // fan_out uses Down broadcast; enforce "exactly one worker handles a subtask"
        if (request.Variables.TryGetValue("__target_worker", out var targetValue))
        {
            var target = targetValue?.StringValue;
            if (!string.IsNullOrWhiteSpace(target) &&
                !string.Equals(target, agent.Id, StringComparison.OrdinalIgnoreCase))
            {
                return null; // ignore tasks not assigned to me
            }
        }

        if (!string.Equals(request.StepType, "llm_call", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.StepType, "tool_call", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.StepType, "tool_validate", StringComparison.OrdinalIgnoreCase))
        {
            var unsupported = new StepCompletedEventProto
            {
                RequestId = request.RequestId,
                StepId = request.StepId,
                WorkerId = agent.Id,
                Success = false,
                Error = $"Worker does not support step type: {request.StepType}",
                DurationMs = 0
            };
            return new StepExecutionResult(unsupported, EventDirection.Up);
        }

        var startTime = DateTime.UtcNow;
        PrimitiveResult result;
        try
        {
            if (string.Equals(request.StepType, "llm_call", StringComparison.OrdinalIgnoreCase))
            {
                result = await ExecuteLlmCallAsync(agent, request, ct);
            }
            else
            {
                result = await ExecuteToolCallAsync(agent, request, ct);
            }
        }
        catch (Exception ex)
        {
            result = new PrimitiveResult { Success = false, Error = ex.Message };
        }

        var completed = new StepCompletedEventProto
        {
            RequestId = request.RequestId,
            StepId = request.StepId,
            WorkerId = agent.Id,
            Success = result.Success,
            Result = result.AssistantResponse ?? result.Value?.ToString() ?? string.Empty,
            Error = result.Error ?? string.Empty,
            TokensUsed = result.TokensUsed,
            LlmCalls = result.LlmCalls,
            DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
        };

        return new StepExecutionResult(completed, EventDirection.Up);
    }

    private async Task<PrimitiveResult> ExecuteLlmCallAsync(
        AIGAgentBase agent,
        ExecuteStepRequestEvent request,
        CancellationToken ct)
    {
        var parameters = ConvertFromProtoMap(request.Parameters);
        var variables = ConvertFromProtoMap(request.Variables);

        var prompt = parameters.GetValueOrDefault("prompt")?.ToString() ?? string.Empty;
        var systemPrompt = parameters.GetValueOrDefault("system")?.ToString();
        var outputType = parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        var strictParse = ResolveBool(parameters.GetValueOrDefault("strict_parse"), true);

        var maxLength = ResolveInt(parameters.GetValueOrDefault("max_length"), 102400);
        maxLength = Math.Clamp(maxLength, 1024, 1024 * 1024);

        var timeoutSeconds = ResolveInt(parameters.GetValueOrDefault("timeout_seconds"), 180);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600);

        var idleTimeoutSeconds = ResolveInt(parameters.GetValueOrDefault("idle_timeout_seconds"), 30);
        idleTimeoutSeconds = Math.Clamp(idleTimeoutSeconds, 1, timeoutSeconds);

        prompt = _templateEngine.Render(prompt, variables);
        if (systemPrompt != null)
        {
            systemPrompt = _templateEngine.Render(systemPrompt, variables);
        }

        var agentOverride = ResolveAgentOverride(parameters, variables);
        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? agentOverride?.SystemPrompt
            : systemPrompt;

        var chat = ChatRequest.Create(prompt);
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            chat.RequestId = request.RequestId;
        }

        if (!TryApplySessionId(chat, variables))
        {
            // no session id in variables
        }

        chat.StageHint = request.StepId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(effectiveSystemPrompt))
        {
            chat.AddContext("system_prompt", effectiveSystemPrompt!);
        }

        if (agentOverride?.Temperature is not null)
            chat.SetTemperatureIfNotSet(agentOverride.Temperature.Value);
        if (agentOverride?.MaxTokens is not null)
            chat.SetMaxTokensIfNotSet(agentOverride.MaxTokens.Value);

        var callTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        var idleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);
        using var timeoutCts = new CancellationTokenSource(callTimeout);
        var localCt = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token).Token;

        var finalContent = string.Empty;
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;

        try
        {
            var supportsStreaming = await agent.SupportsStreamingAsync(localCt);
            if (supportsStreaming)
            {
                var sb = new StringBuilder();
                var chunkIndex = 0;
                var startAt = DateTimeOffset.UtcNow;

                var stream = agent.ChatStreamAsync(chat, localCt);
                var enumerator = stream.GetAsyncEnumerator(localCt);
                try
                {
                    while (true)
                    {
                        var elapsed = DateTimeOffset.UtcNow - startAt;
                        var remaining = callTimeout - elapsed;
                        if (remaining <= TimeSpan.Zero)
                        {
                            return new PrimitiveResult { Success = false, Error = $"llm-timeout>{timeoutSeconds}s" };
                        }

                        var moveNextTask = enumerator.MoveNextAsync().AsTask();
                        var waitTimeout = remaining < idleTimeout ? remaining : idleTimeout;
                        var completed = await Task.WhenAny(moveNextTask, Task.Delay(waitTimeout, localCt));
                        if (completed != moveNextTask)
                        {
                            return new PrimitiveResult
                            {
                                Success = false,
                                Error = waitTimeout == idleTimeout
                                    ? $"llm-idle-timeout>{idleTimeoutSeconds}s"
                                    : $"llm-timeout>{timeoutSeconds}s"
                            };
                        }

                        if (!await moveNextTask)
                        {
                            break;
                        }

                        var delta = enumerator.Current ?? string.Empty;
                        if (string.IsNullOrEmpty(delta))
                            continue;

                        sb.Append(delta);
                        finalContent = sb.ToString();

                        if (finalContent.Length > maxLength)
                        {
                            return new PrimitiveResult { Success = false, Error = $"redflag-length>{maxLength}" };
                        }

                        chunkIndex++;
                        if (chunkIndex > 20000)
                        {
                            return new PrimitiveResult { Success = false, Error = "llm-stream-too-long>20000" };
                        }
                    }
                }
                finally
                {
                    try
                    {
                        var disposeTask = enumerator.DisposeAsync().AsTask();
                        await Task.WhenAny(disposeTask, Task.Delay(1000, localCt));
                    }
                    catch
                    {
                        // ignored
                    }
                }

                totalPromptTokens = Math.Max(1, prompt.Length / 4);
                totalCompletionTokens = Math.Max(1, finalContent.Length / 4);
            }
            else
            {
                var response = await agent.ChatAsync(chat, localCt).WaitAsync(callTimeout);
                finalContent = response.Content ?? string.Empty;
                totalPromptTokens = response.Usage?.PromptTokens ?? Math.Max(1, prompt.Length / 4);
                totalCompletionTokens = response.Usage?.CompletionTokens ?? Math.Max(1, finalContent.Length / 4);
            }
        }
        catch (OperationCanceledException)
        {
            return new PrimitiveResult { Success = false, Error = $"llm-timeout>{timeoutSeconds}s" };
        }
        catch (Exception ex)
        {
            return new PrimitiveResult { Success = false, Error = $"llm-error:{ex.Message}" };
        }

        if (finalContent.Length > maxLength)
        {
            return new PrimitiveResult { Success = false, Error = $"redflag-length>{maxLength}" };
        }

        object? parsedValue;
        if (string.Equals(outputType, "text", StringComparison.OrdinalIgnoreCase))
        {
            parsedValue = finalContent;
        }
        else
        {
            var parser = _parserFactory.Create(outputType);
            parsedValue = parser.Parse(finalContent);
            if (parsedValue == null)
            {
                if (!strictParse)
                {
                    parsedValue = finalContent;
                }
                else
                {
                    return new PrimitiveResult { Success = false, Error = "redflag-parse-null" };
                }
            }
        }

        return new PrimitiveResult
        {
            Success = true,
            Value = parsedValue,
            TokensUsed = totalPromptTokens + totalCompletionTokens,
            PromptTokens = totalPromptTokens,
            CompletionTokens = totalCompletionTokens,
            LlmCalls = 1,
            SystemPrompt = effectiveSystemPrompt,
            UserPrompt = prompt,
            AssistantResponse = finalContent
        };
    }

    private async Task<PrimitiveResult> ExecuteToolCallAsync(
        AIGAgentBase agent,
        ExecuteStepRequestEvent request,
        CancellationToken ct)
    {
        if (!agent.ToolEvolutionOptions.EnableToolCalls)
            return new PrimitiveResult { Success = false, Error = "tool_call is disabled by ToolEvolutionOptions" };

        var parameters = ConvertFromProtoMap(request.Parameters);
        var variables = ConvertFromProtoMap(request.Variables);

        var toolName = parameters.GetValueOrDefault("tool")?.ToString()
                       ?? parameters.GetValueOrDefault("tool_name")?.ToString()
                       ?? string.Empty;
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new PrimitiveResult { Success = false, Error = "tool_call requires 'tool' parameter" };
        }

        var argsObj = parameters.GetValueOrDefault("args") ?? parameters.GetValueOrDefault("validation_args");
        var args = ResolveToolArguments(argsObj, variables);

        var sessionId = TryGetSessionId(variables);
        var result = await agent.ExecuteToolForWorkflowAsync(toolName, args, sessionId, ct);
        if (!result.IsSuccess)
        {
            return new PrimitiveResult { Success = false, Error = result.ErrorMessage ?? "tool_call failed" };
        }

        return new PrimitiveResult
        {
            Success = true,
            Value = result.Content ?? string.Empty,
            AssistantResponse = result.Content ?? string.Empty,
            TokensUsed = 0,
            LlmCalls = 0
        };
    }

    private static string? TryGetSessionId(Dictionary<string, object> variables)
    {
        if (variables.TryGetValue(ChatRequest.SessionIdKey, out var raw) && raw != null)
            return raw.ToString();
        if (variables.TryGetValue(ChatRequest.SessionIdKeyCamel, out var camel) && camel != null)
            return camel.ToString();
        return null;
    }

    private Dictionary<string, object> ResolveToolArguments(object? argsObj, Dictionary<string, object> variables)
    {
        if (argsObj == null)
            return new Dictionary<string, object>();

        if (argsObj is Dictionary<string, object> dict)
            return RenderArgs(dict, variables);

        if (argsObj is Dictionary<string, object?> dictNullable)
        {
            var mapped = dictNullable.ToDictionary(k => k.Key, v => v.Value);
            return RenderArgs(mapped, variables);
        }

        if (argsObj is JsonElement element)
        {
            var raw = element.GetRawText();
            return ParseArgsJson(raw);
        }

        if (argsObj is string text)
        {
            var rendered = _templateEngine.Render(text, variables);
            return ParseArgsJson(rendered);
        }

        return new Dictionary<string, object> { ["input"] = argsObj };
    }

    private Dictionary<string, object> RenderArgs(
        Dictionary<string, object?> args,
        Dictionary<string, object> variables)
    {
        var result = new Dictionary<string, object>(args.Count, StringComparer.Ordinal);
        foreach (var (key, value) in args)
        {
            result[key] = RenderArgValue(value, variables);
        }
        return result;
    }

    private object RenderArgValue(object? value, Dictionary<string, object> variables)
    {
        if (value == null)
            return string.Empty;

        if (value is string s)
            return _templateEngine.Render(s, variables);

        if (value is Dictionary<string, object?> dict)
            return RenderArgs(dict, variables);

        if (value is IEnumerable<object?> list)
            return list.Select(v => RenderArgValue(v, variables)).ToList();

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

        return new Dictionary<string, object> { ["input"] = text };
    }

    private sealed record AgentOverride(
        string Role,
        string? SystemPrompt,
        double? Temperature,
        int? MaxTokens);

    private AgentOverride? ResolveAgentOverride(
        Dictionary<string, object> parameters,
        Dictionary<string, object> variables)
    {
        if (!parameters.TryGetValue("agent", out var agentObj) || agentObj == null)
            return null;

        var raw = agentObj.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var role = _templateEngine.Render(raw, variables).Trim();
        if (role.Length == 0)
            return null;

        string? workingDirectory = null;
        if (WorkspacePathGuard.TryGetWorkspaceRoot(out var workspaceRoot, out _))
            workingDirectory = workspaceRoot;

        var yaml = AgentYamlResolver.TryLoad(role, workingDirectory);
        if (yaml == null)
            return null;

        var systemPrompt = (yaml.SystemPrompt ?? string.Empty).Trim();
        if (systemPrompt.Length == 0 && yaml.Persona?.Role is { Length: > 0 } personaRole)
        {
            systemPrompt = $"You are {personaRole}.";
        }

        return new AgentOverride(
            Role: role,
            SystemPrompt: systemPrompt.Length == 0 ? null : systemPrompt,
            Temperature: yaml.Temperature,
            MaxTokens: yaml.MaxTokens);
    }

    private static int ResolveInt(object? value, int defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            decimal m => (int)m,
            string s when int.TryParse(s, out var p) => p,
            _ => defaultValue
        };
    }

    private static bool ResolveBool(object? value, bool defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => Math.Abs(d) > double.Epsilon,
            float f => Math.Abs(f) > float.Epsilon,
            decimal m => m != 0,
            string s when bool.TryParse(s, out var p) => p,
            _ => defaultValue
        };
    }

    private static Dictionary<string, object> ConvertFromProtoMap(
        Google.Protobuf.Collections.MapField<string, Value> protoMap)
    {
        var result = new Dictionary<string, object>();
        foreach (var (key, value) in protoMap)
        {
            result[key] = ProtoValueConverter.FromProto(value);
        }

        return result;
    }

    private static bool TryApplySessionId(ChatRequest chat, Dictionary<string, object> variables)
    {
        if (variables.TryGetValue(ChatRequest.SessionIdKey, out var sessionObj))
        {
            var sessionId = sessionObj?.ToString();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                chat.SetSessionId(sessionId);
                return true;
            }
        }

        if (variables.TryGetValue(ChatRequest.SessionIdKeyCamel, out var sessionCamel))
        {
            var sessionId = sessionCamel?.ToString();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                chat.SetSessionId(sessionId);
                return true;
            }
        }

        return false;
    }
}
