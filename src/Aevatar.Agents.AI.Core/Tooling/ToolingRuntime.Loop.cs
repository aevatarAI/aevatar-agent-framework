using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class ToolingRuntime
{
    internal async Task<(AevatarLLMResponse FinalResponse, ToolCallInfo? ToolCall)> ExecuteToolCallLoopAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        AevatarLLMResponse initialResponse,
        CancellationToken cancellationToken)
    {
        const int MaxRounds = 8;
        const int MaxSameCallAttempts = 2;

        var current = initialResponse;
        ToolCallInfo? lastToolCall = null;
        var toolCallAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var round = 0; round < MaxRounds && current.AevatarFunctionCall != null; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var functionCall = current.AevatarFunctionCall;
            if (functionCall == null)
            {
                break;
            }

            await InitializeToolsAsync(cancellationToken);

            var args = ParseToolArguments(functionCall.Arguments);
            var callId = EnsureToolCallId(functionCall);
            var signature = BuildToolCallSignature(functionCall.Name, args);
            toolCallAttempts.TryGetValue(signature, out var attempts);
            attempts++;
            toolCallAttempts[signature] = attempts;

            // If the model keeps requesting the exact same tool call, force it to answer without tools.
            if (attempts > MaxSameCallAttempts)
            {
                _loopHost.Logger.LogWarning(
                    "Detected repeated tool call (attempt {Attempt}/{MaxAttempt}) for {ToolName}. Forcing final answer. RequestId={RequestId}",
                    attempts, MaxSameCallAttempts, functionCall.Name, request.RequestId);

                current = await ForceModelToAnswerWithoutToolsAsync(
                    llmRequest,
                    reason: $"Repeated tool call '{functionCall.Name}' with identical arguments.",
                    cancellationToken);
                break;
            }

            var executionContext = _loopHost.BuildToolExecutionContext(request.RequestId, cancellationToken);

            // Hard guard: if a tool allowlist is active, deny executing tools outside it.
            // This is used by Agent Skills `allowed-tools` to constrain what the model can do.
            var toolResult = await _loopHost.ExecuteAllowedToolWithHooksAsync(
                functionCall.Name,
                args,
                executionContext,
                llmRequest,
                cancellationToken);

            lastToolCall = new ToolCallInfo
            {
                ToolName = functionCall.Name,
                Result = toolResult.Content ?? string.Empty
            };

            string? reasoningContent = null;
            if (round == 0 && initialResponse.Metadata != null &&
                initialResponse.Metadata.TryGetValue("reasoning_content", out var rObj) &&
                rObj is string rStr)
            {
                reasoningContent = rStr;
            }

            foreach (var (k, v) in args)
            {
                lastToolCall.Arguments[k] = v?.ToString() ?? string.Empty;
            }

            var toolCallMsg = CreateToolCallMessage(functionCall, reasoningContent);
            var toolResultMsg = CreateToolResultMessage(functionCall.Name, callId, toolResult);

            llmRequest.Messages.Add(toolCallMsg);
            llmRequest.Messages.Add(toolResultMsg);

            // Optional: persist tool transcript into State.History (only when history switch is on).
            if (_loopHost.EnableChatHistoryInState)
            {
                _loopHost.AddMessageToHistory(toolCallMsg);
                _loopHost.AddMessageToHistory(toolResultMsg);
            }

            // Publish tool execution response (useful for UI/telemetry)
            await _loopHost.PublishAsync(new ToolExecutionResponseEvent
            {
                RequestId = request.RequestId,
                ToolName = functionCall.Name,
                Success = toolResult.IsSuccess,
                Result = toolResult.Content ?? string.Empty,
                Error = toolResult.ErrorMessage ?? string.Empty,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, EventDirection.Down, cancellationToken);

            // If skills_load was executed, it may update the tool allowlist dynamically.
            _loopHost.TryApplyToolAllowlistFromSkillsLoadResult(llmRequest, functionCall.Name, toolResult.Content);

            // Tool set might have changed (e.g., skills_load imported new tools) - refresh function defs.
            await RefreshToolCachesAsync(cancellationToken);
            _loopHost.AttachToolsToRequest(llmRequest);

            // Call LLM again with tool result appended
            current = await _loopHost.GenerateLLMWithHooksAsync(request, llmRequest, cancellationToken);
        }

        if (current.AevatarFunctionCall != null)
        {
            _loopHost.Logger.LogWarning(
                "Tool call loop exceeded max rounds ({MaxRounds}). Forcing final answer without tools. RequestId={RequestId}",
                MaxRounds, request.RequestId);

            current = await ForceModelToAnswerWithoutToolsAsync(
                llmRequest,
                reason: $"Exceeded max tool call rounds ({MaxRounds}).",
                cancellationToken);

            // If provider still returns a function call even when no tools are attached, fall back to a safe text reply.
            if (current.AevatarFunctionCall != null)
            {
                _loopHost.Logger.LogWarning(
                    "Provider still returned a tool call after tools were detached. Returning fallback content. RequestId={RequestId}",
                    request.RequestId);

                current = new AevatarLLMResponse
                {
                    Content =
                        "工具调用陷入循环，已强制停止工具调用并返回兜底回复。请尝试换个问法、减少需要工具的操作，或检查工具/权限配置。",
                    ModelName = initialResponse.ModelName,
                    AevatarStopReason = AevatarStopReason.Complete,
                    Usage = initialResponse.Usage
                };
            }
        }

        return (current, lastToolCall);
    }

    internal async Task<ToolExecutionResult> ExecuteAllowedToolAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
    {
        // Enforce policy again at execution time (defense in depth).
        var toolDef =
            _registeredToolsCache.FirstOrDefault(t =>
                string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (toolDef != null && !_loopHost.IsToolAllowedByPolicy(toolDef))
        {
            var content = JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool execution denied by agent policy.",
                tool = toolName,
                deniedReason = _loopHost.BuildToolPolicyDenyReason(toolDef)
            });

            return new ToolExecutionResult
            {
                ToolName = toolName,
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is denied by agent policy.",
                Content = content,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        if (AIGAgentKeys.TryGetToolAllowlist(llmRequest, out var allowlist) &&
            allowlist.Count > 0 &&
            !allowlist.Contains(toolName))
        {
            // Deny execution (return a tool result the model can read)
            var content = JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool is not allowed by current allowlist.",
                tool = toolName,
                allowedTools = allowlist.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
            });

            return new ToolExecutionResult
            {
                ToolName = toolName,
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is not allowed by current allowlist.",
                Content = content,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        return await _loopHost.ExecuteToolAsync(toolName, args, executionContext, cancellationToken);
    }

    internal Dictionary<string, object> ParseToolArguments(string argumentsJson)
    {
        return ToolArgumentsJson.Parse(argumentsJson);
    }

    private async Task<AevatarLLMResponse> ForceModelToAnswerWithoutToolsAsync(
        AevatarLLMRequest llmRequest,
        string reason,
        CancellationToken cancellationToken)
    {
        llmRequest.Messages.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.System,
            Content =
                "Tool loop guard triggered: " + reason + "\n" +
                "You MUST stop calling tools now. Use the existing tool results already provided in the conversation and answer the user directly.\n" +
                "If you cannot answer, explain what information is missing and ask a single clarifying question.",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        llmRequest.Functions = new List<AevatarFunctionDefinition>();
        return await _loopHost.LLMProvider.GenerateAsync(llmRequest, cancellationToken);
    }

    private static string EnsureToolCallId(AevatarFunctionCall functionCall)
    {
        if (!string.IsNullOrWhiteSpace(functionCall.CallId))
            return functionCall.CallId;

        functionCall.CallId = Guid.NewGuid().ToString("N");
        return functionCall.CallId;
    }

    private static string BuildToolCallSignature(string toolName, Dictionary<string, object> args)
    {
        var sb = new StringBuilder(toolName.Trim().ToLowerInvariant());
        foreach (var (k, v) in args.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|')
                .Append(k.Trim().ToLowerInvariant())
                .Append('=')
                .Append(v?.ToString() ?? "null");
        }

        return sb.ToString();
    }

    private static AevatarChatMessage CreateToolCallMessage(AevatarFunctionCall functionCall,
        string? reasoningContent = null)
    {
        var callId = string.IsNullOrWhiteSpace(functionCall.CallId)
            ? Guid.NewGuid().ToString("N")
            : functionCall.CallId;

        var msg = new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = $"Calling tool {functionCall.Name} with arguments: {functionCall.Arguments}",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolCalls =
            {
                new ToolCall
                {
                    Id = callId,
                    ToolName = functionCall.Name,
                    Arguments = functionCall.Arguments
                }
            }
        };

        if (!string.IsNullOrEmpty(reasoningContent))
        {
            msg.Metadata.Add("reasoning_content", reasoningContent);
        }

        return msg;
    }

    private static AevatarChatMessage CreateToolResultMessage(string toolName, string toolCallId,
        ToolExecutionResult result)
    {
        var callId = !string.IsNullOrWhiteSpace(toolCallId)
            ? toolCallId
            : result.ToolCallId;

        return new AevatarChatMessage
        {
            Role = AevatarChatRole.Tool,
            Content = result.Content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolResult = new ToolExecutionResult
            {
                ToolCallId = callId,
                ToolName = toolName,
                Content = result.Content,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                Timestamp = result.Timestamp,
                Duration = result.Duration
            }
        };
    }
}