using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Utilities;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  Cognitive Worker Agent
//  Actor that actually executes LLM calls
//  Each Worker runs independently on Actor Runtime
// ============================================================

/// <summary>
/// Cognitive Worker Agent - Actually executes LLM calls
/// 
/// Architecture:
/// - Coordinator distributes tasks via Protobuf events
/// - Worker executes independently, scheduled by Actor Runtime
/// - Sends Protobuf events back to Coordinator after completion
/// 
/// This is true Actor parallelism, not in-process pseudo-concurrency
/// </summary>
public class CognitiveWorkerGAgent : CognitiveAIGAgentBase<CognitiveWorkerState>
{
    private readonly TemplateEngine _templateEngine = new();
    private readonly OutputParserFactory _parserFactory = new();

    public CognitiveWorkerGAgent()
    {
    }

    protected override string AgentKind => "cognitive_worker";

    protected override void AppendAgentHistoryMetadata(Dictionary<string, string> metadata)
    {
        metadata["worker_id"] = CustomState.WorkerId ?? string.Empty;
    }

    // ============================================================
    //  Lifecycle
    // ============================================================

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Initialize Worker state
        var rawId = AgentId.ExtractRawId(Id);
        var compact = rawId.Replace("-", "", StringComparison.Ordinal);
        CustomState.WorkerId = compact.Length > 8 ? compact[..8] : compact;
        CustomState.Status = WorkerStatus.WsIdle;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"CognitiveWorker [{CustomState.WorkerId}] - Status: {CustomState.Status}");
    }

    // ============================================================
    //  Event Handling
    // ============================================================

    /// <summary>
    /// Handle execute step request (Protobuf event)
    /// </summary>
    [EventHandler]
    public async Task HandleExecuteStepRequest(ExecuteStepRequestEvent request)
    {
        // fan_out uses Down broadcast; enforce "exactly one worker handles a subtask"
        // by honoring the reserved variable `__target_worker` (Guid string in "N" format).
        if (request.Variables.TryGetValue("__target_worker", out var targetValue))
        {
            var target = targetValue?.StringValue;
            var self = Id;
            if (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, self, StringComparison.OrdinalIgnoreCase))
            {
                return; // ignore tasks not assigned to me
            }
        }

        Logger.LogDebug("Worker {WorkerId} received step request: {StepId}",
            CustomState.WorkerId, request.StepId);

        CustomState.Status = WorkerStatus.WsExecuting;
        CustomState.CurrentStepId = request.StepId;

        var startTime = DateTime.UtcNow;

        try
        {
            var result = await ExecuteStepAsync(request);

            CustomState.Status = WorkerStatus.WsIdle;
            CustomState.TotalStepsCompleted++;

            // Send completion event to Coordinator (propagate upward)
            await PublishAsync(new StepCompletedEventProto
            {
                RequestId = request.RequestId,
                StepId = request.StepId,
                WorkerId = CustomState.WorkerId,
                Success = result.Success,
                // IMPORTANT:
                // - For json/json_array outputs, result.Value is a Dictionary/List, and ToString() is useless.
                // - Coordinator needs the RAW assistant response to parse/aggregate deterministically.
                Result = result.AssistantResponse ?? result.Value?.ToString() ?? "",
                Error = result.Error ?? "",
                TokensUsed = result.TokensUsed,
                LlmCalls = result.LlmCalls,
                DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
            }, EventDirection.Up);
        }
        catch (Exception ex)
        {
            CustomState.Status = WorkerStatus.WsError;

            await PublishAsync(new StepCompletedEventProto
            {
                RequestId = request.RequestId,
                StepId = request.StepId,
                WorkerId = CustomState.WorkerId,
                Success = false,
                Error = ex.Message,
                DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
            }, EventDirection.Up);
        }
    }

    // ============================================================
    //  Step Execution
    // ============================================================

    private async Task<PrimitiveResult> ExecuteStepAsync(ExecuteStepRequestEvent request)
    {
        return request.StepType switch
        {
            "llm_call" => await ExecuteLlmCallAsync(request),
            _ => PrimitiveResult.Fail($"Worker does not support step type: {request.StepType}")
        };
    }

    private async Task<PrimitiveResult> ExecuteLlmCallAsync(ExecuteStepRequestEvent request)
    {
        // Convert parameters from Protobuf Value
        var parameters = ConvertFromProtoMap(request.Parameters);
        var variables = ConvertFromProtoMap(request.Variables);

        // Parse parameters
        var prompt = parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
        var systemPrompt = parameters.GetValueOrDefault("system")?.ToString();
        var outputType = parameters.GetValueOrDefault("output")?.ToString() ?? "text";
        var strictParse = ResolveBool(parameters.GetValueOrDefault("strict_parse"), true);

        // Guardrails (configurable via DSL)
        var maxLength = ResolveInt(parameters.GetValueOrDefault("max_length"), 102400);
        maxLength = Math.Clamp(maxLength, 1024, 1024 * 1024); // [1KB, 1MB]

        var timeoutSeconds = ResolveInt(parameters.GetValueOrDefault("timeout_seconds"), 180);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600); // [5s, 1h]

        var idleTimeoutSeconds = ResolveInt(parameters.GetValueOrDefault("idle_timeout_seconds"), 30);
        idleTimeoutSeconds = Math.Clamp(idleTimeoutSeconds, 1, timeoutSeconds);

        // Render template
        prompt = _templateEngine.Render(prompt, variables);
        if (systemPrompt != null)
        {
            systemPrompt = _templateEngine.Render(systemPrompt, variables);
        }

        // Prepare per-step chat request (system prompt is passed via Context override).
        var chat = ChatRequest.Create(prompt);
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            chat.RequestId = request.RequestId;
        }

        chat.StageHint = request.StepId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            chat.AddContext("system_prompt", systemPrompt!);
        }

        // Bind step metadata to history writes (async-local, safe for concurrent tasks).
        using var _ = BeginStepHistory(
            stepId: request.StepId ?? string.Empty,
            stepType: request.StepType ?? "llm_call",
            systemPrompt: systemPrompt,
            requestId: request.RequestId);

        var callTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        var idleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);
        using var timeoutCts = new CancellationTokenSource(callTimeout);
        var ct = timeoutCts.Token;

        var finalContent = string.Empty;
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;

        try
        {
            var supportsStreaming = await SupportsStreamingAsync(ct);
            if (supportsStreaming)
            {
                var sb = new StringBuilder();
                var chunkIndex = 0;
                var startAt = DateTimeOffset.UtcNow;

                // Streaming events throttling (reduce event storm & threadpool starvation)
                const int StreamPublishEveryN = 16;
                var streamPublishMinInterval = TimeSpan.FromMilliseconds(250);
                var lastPublishAt = DateTimeOffset.MinValue;

                var stream = ChatStreamAsync(chat, ct);
                var enumerator = stream.GetAsyncEnumerator(ct);

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

                        // Wait for next chunk, but don't block forever.
                        var moveNextTask = enumerator.MoveNextAsync().AsTask();
                        var waitTimeout = remaining < idleTimeout ? remaining : idleTimeout;
                        var completed = await Task.WhenAny(moveNextTask, Task.Delay(waitTimeout));
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
                            break;

                        var delta = enumerator.Current ?? string.Empty;
                        if (string.IsNullOrEmpty(delta))
                            continue;

                        sb.Append(delta);
                        finalContent = sb.ToString();

                        // Red-flag: Length (stop early, avoid output explosion causing memory/rendering to be overwhelmed)
                        if (finalContent.Length > maxLength)
                        {
                            return new PrimitiveResult { Success = false, Error = $"redflag-length>{maxLength}" };
                        }

                        // Streaming report (Success=false indicates intermediate state)
                        var now = DateTimeOffset.UtcNow;
                        var isFirst = chunkIndex == 0;
                        var shouldPublish =
                            isFirst ||
                            (chunkIndex % StreamPublishEveryN == 0) ||
                            (now - lastPublishAt >= streamPublishMinInterval);

                        if (shouldPublish)
                        {
                            lastPublishAt = now;

                            await PublishAsync(new StepCompletedEventProto
                            {
                                RequestId = request.RequestId,
                                StepId = request.StepId,
                                WorkerId = CustomState.WorkerId,
                                Success = false,
                                Result = finalContent,
                                Error = "",
                                // IMPORTANT:
                                // - This is streaming intermediate state event, Coordinator only uses for UI display
                                // - TokensUsed/LlmCalls should not accumulate (otherwise will be repeatedly summed, causing statistics explosion)
                                TokensUsed = 0,
                                LlmCalls = 0,
                                DurationMs = 0
                            }, EventDirection.Up);
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
                        await Task.WhenAny(disposeTask, Task.Delay(1000));
                    }
                    catch
                    {
                        // ignored
                    }
                }

                // Token usage is not available in streaming mode; use a cheap estimate.
                totalPromptTokens = Math.Max(1, prompt.Length / 4);
                totalCompletionTokens = Math.Max(1, finalContent.Length / 4);
            }
            else
            {
                ChatResponse response;
                try
                {
                    response = await ChatAsync(chat, ct).WaitAsync(callTimeout);
                }
                catch (TimeoutException)
                {
                    return new PrimitiveResult { Success = false, Error = $"llm-timeout>{timeoutSeconds}s" };
                }

                finalContent = response.Content ?? string.Empty;
                totalPromptTokens = response.Usage?.PromptTokens ?? Math.Max(1, prompt.Length / 4);
                totalCompletionTokens = response.Usage?.CompletionTokens ?? Math.Max(1, finalContent.Length / 4);
            }
        }
        catch (OperationCanceledException)
        {
            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-timeout>{timeoutSeconds}s",
                SystemPrompt = systemPrompt,
                UserPrompt = prompt,
                AssistantResponse = finalContent,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LLM] ✗ Error in {StepId}: {Message}", request.StepId, ex.Message);
            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-error:{ex.Message}",
                SystemPrompt = systemPrompt,
                UserPrompt = prompt,
                AssistantResponse = finalContent,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }

        // Red-flag: Length (fallback)
        if (finalContent.Length > maxLength)
        {
            return new PrimitiveResult { Success = false, Error = $"redflag-length>{maxLength}" };
        }

        // Parse output (aligned with Coordinator semantics: text not parsed; non-text can be strict/loose)
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

        // Persist a structured per-step interaction into AIMemory (optional).
        // This is the "long-term" layer; State.History stays as a short-term window.
        await PersistInteractionToMemoryAsync(
            request,
            systemPrompt,
            prompt,
            finalContent,
            totalPromptTokens,
            totalCompletionTokens);

        return new PrimitiveResult
        {
            Success = true,
            Value = parsedValue,
            TokensUsed = totalPromptTokens + totalCompletionTokens,
            PromptTokens = totalPromptTokens,
            CompletionTokens = totalCompletionTokens,
            LlmCalls = 1,
            SystemPrompt = systemPrompt,
            UserPrompt = prompt,
            AssistantResponse = finalContent
        };
    }

    private async Task PersistInteractionToMemoryAsync(
        ExecuteStepRequestEvent request,
        string? systemPrompt,
        string userPrompt,
        string assistantResponse,
        int promptTokens,
        int completionTokens)
    {
        if (AIMemory == null)
            return;

        try
        {
            var payload = new
            {
                kind = "aevatar.cognitive.llm_interaction.v1",
                agentId = Id,
                agentKind = "cognitive_worker",
                workerId = CustomState.WorkerId ?? "",
                requestId = request.RequestId ?? "",
                stepId = request.StepId ?? "",
                stepType = request.StepType ?? "",
                systemPrompt,
                userPrompt,
                assistantResponse,
                promptTokens,
                completionTokens,
                totalTokens = promptTokens + completionTokens,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            };

            var json = JsonSerializer.Serialize(payload);
            await AIMemory.AddMessageAsync("assistant", json);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to persist llm interaction to AIMemory (best-effort).");
        }
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

    // ============================================================
    //  Helper Methods
    // ============================================================

    /// <summary>
    /// Convert Protobuf Value Map to regular dictionary
    /// </summary>
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
}