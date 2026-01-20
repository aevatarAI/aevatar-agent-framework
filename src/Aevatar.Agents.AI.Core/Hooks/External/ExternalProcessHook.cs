using System.Diagnostics;
using System.Text.Json;
using Aevatar.Agents.AI.Core.Hooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.AI.Core.Hooks.External;

/// <summary>
/// External hook runner (Cursor-style stdio JSON).
/// </summary>
public sealed class ExternalProcessHook : IAevatarAgentHook
{
    private readonly AevatarExternalHookOptions _options;
    private readonly ILogger<ExternalProcessHook> _logger;

    public ExternalProcessHook(IOptions<AevatarExternalHookOptions> options, ILogger<ExternalProcessHook>? logger = null)
        : this(options?.Value ?? new AevatarExternalHookOptions(), logger)
    {
    }

    public ExternalProcessHook(AevatarExternalHookOptions options, ILogger<ExternalProcessHook>? logger = null)
    {
        _options = options ?? new AevatarExternalHookOptions();
        _logger = logger ?? NullLogger<ExternalProcessHook>.Instance;
    }

    public Task OnSessionStartAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.SessionStart, context, cancellationToken);

    public Task OnSessionEndAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.SessionEnd, context, cancellationToken);

    public Task OnStopAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.Stop, context, cancellationToken);

    public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.BeforeLLMRequest, context, cancellationToken);

    public Task AfterLLMResponseAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.AfterLLMResponse, context, cancellationToken);

    public Task BeforeToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.BeforeToolExecute, context, cancellationToken);

    public Task AfterToolExecuteAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.AfterToolExecute, context, cancellationToken);

    public Task OnErrorAsync(AevatarAgentHookContext context, Exception exception, CancellationToken cancellationToken)
        => RunExternalHooksAsync(AevatarAgentHookStage.OnError, context, cancellationToken, exception);

    private async Task RunExternalHooksAsync(
        AevatarAgentHookStage stage,
        AevatarAgentHookContext context,
        CancellationToken cancellationToken,
        Exception? exception = null)
    {
        if (_options.Commands.Count == 0)
            return;

        var commands = _options.Commands
            .Where(c => c.Enabled && c.Stage == stage && !string.IsNullOrWhiteSpace(c.Command))
            .ToList();
        if (commands.Count == 0)
            return;

        var payload = BuildPayload(stage, context, exception);
        var payloadJson = JsonSerializer.Serialize(payload);

        foreach (var command in commands)
        {
            await RunCommandAsync(command, payloadJson, context, stage, cancellationToken);
        }
    }

    private async Task RunCommandAsync(
        ExternalHookCommand command,
        string payloadJson,
        AevatarAgentHookContext context,
        AevatarAgentHookStage stage,
        CancellationToken cancellationToken)
    {
        var timeoutMs = command.TimeoutMs > 0 ? command.TimeoutMs : _options.DefaultTimeoutMs;
        var cwd = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? null : command.WorkingDirectory;

        var startInfo = new ProcessStartInfo
        {
            FileName = command.Command,
            Arguments = command.Arguments ?? string.Empty,
            WorkingDirectory = cwd ?? string.Empty,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                _logger.LogWarning("External hook command failed to start. Stage={Stage} Command={Command}",
                    stage, command.Command);
                return;
            }

            await process.StandardInput.WriteAsync(payloadJson);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            var exitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(timeoutMs, cancellationToken);
            var completed = await Task.WhenAny(exitTask, timeoutTask);
            if (completed == timeoutTask)
            {
                TryKillProcess(process);
                _logger.LogWarning("External hook command timeout. Stage={Stage} Command={Command} TimeoutMs={TimeoutMs}",
                    stage, command.Command, timeoutMs);
                return;
            }

            var output = await outputTask;
            var stderr = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "External hook command returned non-zero exit code. Stage={Stage} Command={Command} ExitCode={ExitCode} Stderr={Stderr}",
                    stage, command.Command, process.ExitCode, TrimForLog(stderr));
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                ApplyHookOutput(output, context, stage);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External hook command failed. Stage={Stage} Command={Command}", stage, command.Command);
        }
    }

    private object BuildPayload(
        AevatarAgentHookStage stage,
        AevatarAgentHookContext context,
        Exception? exception)
    {
        var payload = new Dictionary<string, object?>
        {
            ["stage"] = stage.ToString(),
            ["timestamp_ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["agent_id"] = context.AgentId,
            ["agent_type"] = context.AgentType,
            ["request_id"] = context.RequestId,
            ["is_streaming"] = context.IsStreaming
        };

        if (context.ChatRequest != null)
        {
            var message = context.ChatRequest.Message ?? string.Empty;
            payload["chat_request"] = new Dictionary<string, object?>
            {
                ["message"] = _options.IncludeChatMessageContent ? TrimToMax(message, _options.MaxMessageChars) : null,
                ["message_length"] = message.Length,
                ["temperature"] = context.ChatRequest.Temperature,
                ["max_tokens"] = context.ChatRequest.MaxTokens
            };
        }

        if (context.LlmRequest != null)
        {
            payload["llm_request"] = new Dictionary<string, object?>
            {
                ["model"] = context.LlmRequest.Settings?.ModelId,
                ["messages"] = context.LlmRequest.Messages?.Count ?? 0,
                ["functions"] = context.LlmRequest.Functions?.Count ?? 0
            };
        }

        if (context.LlmResponse != null)
        {
            payload["llm_response"] = new Dictionary<string, object?>
            {
                ["model"] = context.LlmResponse.ModelName,
                ["stop_reason"] = context.LlmResponse.AevatarStopReason.ToString(),
                ["content_length"] = (context.LlmResponse.Content ?? string.Empty).Length,
                ["usage"] = context.LlmResponse.Usage == null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["prompt_tokens"] = context.LlmResponse.Usage.PromptTokens,
                        ["completion_tokens"] = context.LlmResponse.Usage.CompletionTokens,
                        ["total_tokens"] = context.LlmResponse.Usage.TotalTokens
                    }
            };
        }

        if (!string.IsNullOrWhiteSpace(context.ToolName))
        {
            payload["tool"] = new Dictionary<string, object?>
            {
                ["name"] = context.ToolName,
                ["arguments"] = context.ToolArguments
            };
        }

        if (context.ToolResult != null)
        {
            payload["tool_result"] = new Dictionary<string, object?>
            {
                ["success"] = context.ToolResult.IsSuccess,
                ["error"] = context.ToolResult.ErrorMessage,
                ["content"] = TrimToMax(context.ToolResult.Content ?? string.Empty, _options.MaxToolResultChars),
                ["duration_ms"] = context.ToolResult.Duration
            };
        }

        if (context.StopStatus.HasValue)
        {
            payload["stop"] = new Dictionary<string, object?>
            {
                ["status"] = context.StopStatus.Value.ToString(),
                ["reason"] = context.StopReason,
                ["duration_ms"] = context.Duration?.TotalMilliseconds
            };
        }

        if (exception != null)
        {
            payload["error"] = new Dictionary<string, object?>
            {
                ["type"] = exception.GetType().FullName ?? exception.GetType().Name,
                ["message"] = exception.Message
            };
        }

        return payload;
    }

    private void ApplyHookOutput(string output, AevatarAgentHookContext context, AevatarAgentHookStage stage)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            if (doc.RootElement.TryGetProperty("deny_tool", out var denyObj) &&
                denyObj.ValueKind == JsonValueKind.True)
            {
                if (!string.IsNullOrWhiteSpace(context.ToolName))
                {
                    var reason = doc.RootElement.TryGetProperty("deny_reason", out var r)
                        ? r.GetString()
                        : null;
                    context.DenyTool(reason);
                }
                else
                {
                    _logger.LogDebug(
                        "External hook requested deny_tool but no ToolName in context. Stage={Stage}",
                        stage);
                }
            }

            if (doc.RootElement.TryGetProperty("metadata", out var metaObj) &&
                metaObj.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in metaObj.EnumerateObject())
                {
                    context.Metadata[prop.Name] = ConvertJsonValue(prop.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse external hook output JSON.");
        }
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static string TrimToMax(string value, int maxChars)
    {
        if (maxChars <= 0)
            return string.Empty;
        if (value.Length <= maxChars)
            return value;
        return value.Substring(0, maxChars);
    }

    private static string TrimForLog(string? value, int maxChars = 2000)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxChars ? value : value.Substring(0, maxChars);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best-effort only
        }
    }
}
