using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Utilities;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Text.Json;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  WorkflowCoordinatorAgent - LLM execution (Coordinator-side)
//
//  WHY:
//  - This code naturally expands (streaming + guardrails + UI events).
//  - Separate into file to avoid polluting core orchestration logic.
// ============================================================

public partial class WorkflowCoordinatorAgent
{
    // ============================================================
    //  Simple Steps - Coordinator executes directly
    // ============================================================

    public async Task<PrimitiveResult> ExecuteLlmCallDirectAsync(
        StepDefinition step,
        string? preRenderedPrompt = null,
        string? preRenderedSystem = null)
    {
        // Use pre-rendered prompt (if provided), otherwise render on-the-fly
        var prompt = preRenderedPrompt ?? _templateEngine.Render(
            step.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "",
            _workflowVariables);

        var systemPrompt = preRenderedSystem;
        if (systemPrompt == null && step.Parameters.TryGetValue("system", out var sysObj) && sysObj != null)
        {
            systemPrompt = _templateEngine.Render(sysObj.ToString()!, _workflowVariables);
        }

        // Unify all Coordinator-side LLM calls with the same reliability guardrails used by vote proposals:
        // - max_length / timeout_seconds / idle_timeout_seconds
        // - streaming hang protection
        // - strict parsing for json/json_array (red-flag on parse null)
        return await ExecuteLlmCallWithStreamingAsync(step, step, systemPrompt, prompt);
    }

    /// <summary>
    /// Execute LLM call, streaming events sent to specified step
    /// Used when vote parallel generation, each proposal has independent event stream
    /// </summary>
    private async Task<PrimitiveResult> ExecuteLlmCallWithStreamingAsync(
        StepDefinition generator,
        StepDefinition eventStep,
        string? systemPrompt,
        string userPrompt)
    {
        // ============================================================
        //  Reliability guardrails:
        //  - vote will initiate multiple LLM calls in parallel
        //  - Any call stuck without returning will cause vote to deadlock at Task.WhenAll
        //  - Unified handling here: timeout + exception convergence (timeout/exception → return failed PrimitiveResult)
        // ============================================================

        Logger.LogInformation("[LLM] ▶ ExecuteLlmCallWithStreamingAsync ENTER for {StepId} (promptLen={Len})",
            eventStep.Id, userPrompt.Length);

        var outputType = generator.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";

        var agentOverride = ResolveAgentOverride(generator);
        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt)
            ? agentOverride?.SystemPrompt
            : systemPrompt;

        // Guardrails (configurable via DSL / workflow defaults)
        var maxLength = ResolveIntParameter(generator.Parameters, "max_length", 102400);
        maxLength = Math.Clamp(maxLength, 1024, 1024 * 1024); // [1KB, 1MB]

        var timeoutSeconds = ResolveIntParameter(generator.Parameters, "timeout_seconds", 360);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600); // [5s, 1h]

        var idleTimeoutSeconds = ResolveIntParameter(generator.Parameters, "idle_timeout_seconds", 30);
        idleTimeoutSeconds = Math.Clamp(idleTimeoutSeconds, 1, timeoutSeconds);

        var strictParse = ResolveBoolParameter(generator.Parameters, "strict_parse", true);

        // NOTE:
        // - Don't bind external CancellationToken (current Coordinator execution chain not connected), at least ensure won't hang indefinitely
        // - Use AIGAgentBase.ChatStreamAsync/ChatAsync for provider/tool loop/unified behavior.
        var callTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        var idleTimeout = TimeSpan.FromSeconds(idleTimeoutSeconds);
        using var timeoutCts = new CancellationTokenSource(callTimeout);
        var ct = timeoutCts.Token;

        // Prepare per-step chat request (system prompt is passed via Context override).
        var chat = ChatRequest.Create(userPrompt);
        ApplySessionContext(chat);
        chat.StageHint = eventStep.Id ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(effectiveSystemPrompt))
        {
            chat.AddContext("system_prompt", effectiveSystemPrompt!);
        }
        if (agentOverride?.Temperature is not null)
            chat.SetTemperatureIfNotSet(agentOverride.Temperature.Value);
        if (agentOverride?.MaxTokens is not null)
            chat.SetMaxTokensIfNotSet(agentOverride.MaxTokens.Value);

        // Bind step metadata to history writes (async-local, safe for concurrent vote fan-out).
        using var _ = BeginStepHistory(
            stepId: eventStep.Id ?? string.Empty,
            stepType: eventStep.Type ?? "llm_call",
            systemPrompt: effectiveSystemPrompt);

        var output = string.Empty;
        var promptTokens = 0;
        var completionTokens = 0;

        try
        {
            var supportsStreaming = await SupportsStreamingAsync(ct);

            if (supportsStreaming)
            {
                var sb = new System.Text.StringBuilder();
                var chunkIndex = 0;
                var startAt = DateTimeOffset.UtcNow;

                // Streaming events throttling (reduce event storm & threadpool starvation)
                const int StreamPublishEveryN = 16;
                var streamPublishMinInterval = TimeSpan.FromMilliseconds(250);
                var lastPublishAt = DateTimeOffset.MinValue;

                Logger.LogInformation("[STREAM] Starting streaming for {StepId}, Type={Type}",
                    eventStep.Id, eventStep.Type);

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
                            return new PrimitiveResult
                            {
                                Success = false,
                                Error = $"llm-timeout>{timeoutSeconds}s",
                                SystemPrompt = effectiveSystemPrompt,
                                UserPrompt = userPrompt,
                                AssistantResponse = output,
                                TokensUsed = 0,
                                LlmCalls = 1
                            };
                        }

                        var moveNextTask = enumerator.MoveNextAsync().AsTask();
                        var waitTimeout = remaining < idleTimeout ? remaining : idleTimeout;
                        var completed = await Task.WhenAny(moveNextTask, Task.Delay(waitTimeout));
                        if (completed != moveNextTask)
                        {
                            // No tokens for idleTimeout => treat as hang (or total timeout if remaining < idleTimeout).
                            return new PrimitiveResult
                            {
                                Success = false,
                                Error = waitTimeout == idleTimeout
                                    ? $"llm-idle-timeout>{idleTimeoutSeconds}s"
                                    : $"llm-timeout>{timeoutSeconds}s",
                                SystemPrompt = effectiveSystemPrompt,
                                UserPrompt = userPrompt,
                                AssistantResponse = output,
                                TokensUsed = 0,
                                LlmCalls = 1
                            };
                        }

                        if (!await moveNextTask)
                        {
                            break;
                        }

                        var delta = enumerator.Current ?? string.Empty;
                        if (string.IsNullOrEmpty(delta))
                            continue;

                        if (chunkIndex == 0)
                        {
                            var elapsedMs = (long)(DateTimeOffset.UtcNow - startAt).TotalMilliseconds;
                            // #region agent log
                            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                JsonSerializer.Serialize(new
                                {
                                    sessionId = SessionId ?? Id.ToString(),
                                    runId = CustomState.ExecutionId ?? string.Empty,
                                    hypothesisId = "H31",
                                    location = "CognitiveCoordinatorGAgent.Llm.cs:ExecuteLlmCallWithStreamingAsync",
                                    message = "llm_first_chunk",
                                    data = new
                                    {
                                        stepId = eventStep.Id ?? string.Empty,
                                        elapsedMs,
                                        idleTimeoutSeconds,
                                        timeoutSeconds,
                                        deltaLength = delta.Length
                                    },
                                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                }) + Environment.NewLine);
                            // #endregion
                        }

                        sb.Append(delta);
                        output = sb.ToString();

                        // Prevent output explosion causing memory/rendering/logs to be overwhelmed (this kind of "stuck" looks like infinite loop)
                        if (output.Length > maxLength)
                        {
                            return PrimitiveResult.Fail($"redflag-length>{maxLength}");
                        }

                        // Streaming events sent to specified step (each proposal displayed independently)
                        var now = DateTimeOffset.UtcNow;
                        var isFirst = chunkIndex == 0;
                        var shouldPublish =
                            isFirst ||
                            (chunkIndex % StreamPublishEveryN == 0) ||
                            (now - lastPublishAt >= streamPublishMinInterval);

                        if (shouldPublish)
                        {
                            lastPublishAt = now;
                            EmitStepEvent(eventStep, StepStatus.Running,
                                $"Streaming... ({Math.Max(1, output.Length / 4)} tokens)",
                                progress: 0,
                                systemPrompt: systemPrompt,
                                userPrompt: userPrompt,
                                assistantResponse: output);
                        }

                        chunkIndex++;

                        // Safety stop for pathological streams
                        if (chunkIndex > 20000)
                        {
                            return PrimitiveResult.Fail("llm-stream-too-long>20000");
                        }
                    }
                }
                finally
                {
                    // Don't allow DisposeAsync to block forever if provider is misbehaving.
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
                promptTokens = Math.Max(1, userPrompt.Length / 4);
                completionTokens = Math.Max(1, output.Length / 4);
            }
            else
            {
                try
                {
                    var response = await ChatAsync(chat, ct).WaitAsync(callTimeout);
                    output = response.Content ?? string.Empty;
                    promptTokens = response.Usage?.PromptTokens ?? Math.Max(1, userPrompt.Length / 4);
                    completionTokens = response.Usage?.CompletionTokens ?? Math.Max(1, output.Length / 4);
                }
                catch (TimeoutException)
                {
                    return new PrimitiveResult
                    {
                        Success = false,
                        Error = $"llm-timeout>{timeoutSeconds}s",
            SystemPrompt = effectiveSystemPrompt,
                        UserPrompt = userPrompt,
                        AssistantResponse = output,
                        TokensUsed = 0,
                        LlmCalls = 1
                    };
                }
            }

            AddStats(promptTokens + completionTokens, 1);

            if (output.Length > maxLength)
            {
                return PrimitiveResult.Fail($"redflag-length>{maxLength}");
            }

            var parser = _parserFactory.Create(outputType);
            var parsed = parser.Parse(output);
            if (parsed == null)
            {
                if (!strictParse)
                {
                    parsed = output;
                }
                else
                {
                    if (ToolEvolutionOptions.AutoTriggerOnParseFailure)
                    {
                        SetToolEvolutionTrigger(
                            reason: "llm-parse-failed",
                            step: eventStep,
                            toolName: null);
                    }
                    return PrimitiveResult.Fail("redflag-parse-null");
                }
            }

            return new PrimitiveResult
            {
                Success = true,
                Value = parsed,
                TokensUsed = promptTokens + completionTokens,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                LlmCalls = 1,
                SystemPrompt = effectiveSystemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output
            };
        }
        catch (OperationCanceledException)
        {
            Logger.LogWarning("[LLM] ✗ Timeout/cancel: {StepId} after {Timeout} (promptLen={Len}, outLen={OutLen})",
                eventStep.Id, callTimeout, userPrompt.Length, output.Length);

            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-timeout>{(int)callTimeout.TotalSeconds}s",
                SystemPrompt = effectiveSystemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LLM] ✗ Error in {StepId}: {Message}", eventStep.Id, ex.Message);

            return new PrimitiveResult
            {
                Success = false,
                Error = $"llm-error:{ex.Message}",
                SystemPrompt = effectiveSystemPrompt,
                UserPrompt = userPrompt,
                AssistantResponse = output,
                TokensUsed = 0,
                LlmCalls = 1
            };
        }
    }

    private sealed record AgentOverride(
        string Role,
        string? SystemPrompt,
        double? Temperature,
        int? MaxTokens);

    private AgentOverride? ResolveAgentOverride(StepDefinition step)
    {
        if (!step.Parameters.TryGetValue("agent", out var agentObj) || agentObj == null)
            return null;

        var raw = agentObj.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var role = _templateEngine.Render(raw, _workflowVariables).Trim();
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

}

