using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core.Observability;
using Aevatar.Agents.Core.Telemetry;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    /// <summary>
    /// Process a chat request and return a response.
    /// </summary>
    /// <param name="request">Chat request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chat response</returns>
    public virtual async Task<ChatResponse> ChatAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        var provider = _activeProviderConfig?.ProviderType ?? "unknown";
        var model = Config.Model ?? "default";
        var stopwatch = Stopwatch.StartNew();

        // Start LLM Telemetry
        using var activity = LLMTelemetry.StartLLMCall(Id, provider, model, isStreaming: false);
        using var logScope = LoggingScope.CreateLLMCallScope(Logger, Id, provider, model);

        AgentLogMessages.LLMCallStarting(Logger, Id, provider, model);

        try
        {
            // Keep history bounded before building request (prevents token blow-up).
            await CompactChatHistoryIfNeededAsync(cancellationToken);

            // Ensure built-in tools are registered and cached.
            await InitializeToolsAsync(cancellationToken);

            // Best-effort: if MCP servers are configured but were unreachable earlier,
            // retry per chat call (throttled) so tools can "eventually become available".
            await TryReconnectMcpOnChatAsync(cancellationToken);

            // Build LLM request from chat request
            var llmRequest = BuildLLMRequest(request);

            // Optional: persist conversation to State.History (default off)
            if (EnableChatHistoryInState)
            {
                AddMessageToHistory(request.Message, AevatarChatRole.User);
            }

            // Optional: persist conversation to MemoryStore (default off, best-effort)
            await AppendChatMemoryAsync(AevatarChatRole.User, request.Message ?? string.Empty, request,
                cancellationToken);

            // Call LLM
            var llmResponse = await GenerateLLMWithHooksAsync(request, llmRequest, cancellationToken);
            ToolCallInfo? toolCall = null;

            // Tool/function calling loop
            if (llmResponse.AevatarFunctionCall != null)
            {
                var (finalResponse, lastToolCall) = await ExecuteToolCallLoopAsync(
                    request,
                    llmRequest,
                    llmResponse,
                    cancellationToken);
                llmResponse = finalResponse;
                toolCall = lastToolCall;
            }

            stopwatch.Stop();

            // Build chat response
            var response = new ChatResponse
            {
                Content = llmResponse.Content,
                RequestId = request.RequestId
            };

            if (toolCall != null)
            {
                response.ToolCalled = true;
                response.ToolCall = toolCall;
            }

            if (EnableChatHistoryInState && !string.IsNullOrEmpty(response.Content))
            {
                AddMessageToHistory(response.Content, AevatarChatRole.Assistant);
            }

            // Optional: persist assistant output to MemoryStore (default off, best-effort)
            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                await AppendChatMemoryAsync(AevatarChatRole.Assistant, response.Content!, request, cancellationToken);
            }

            // Compact again after appending new messages (keeps state bounded for next call).
            await CompactChatHistoryIfNeededAsync(cancellationToken);

            // Add token usage if available
            var promptTokens = 0;
            var completionTokens = 0;
            if (llmResponse.Usage != null)
            {
                promptTokens = llmResponse.Usage.PromptTokens;
                completionTokens = llmResponse.Usage.CompletionTokens;

                response.Usage = new AevatarTokenUsage
                {
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = llmResponse.Usage.TotalTokens
                };
            }

            // Record LLM Telemetry
            LLMTelemetry.RecordLLMCallCompleted(
                activity, provider, model, stopwatch.ElapsedMilliseconds,
                promptTokens, completionTokens,
                promptChars: request.Message?.Length ?? 0,
                responseChars: response.Content?.Length ?? 0);

            AgentLogMessages.LLMCallCompleted(Logger, Id, promptTokens, completionTokens,
                stopwatch.ElapsedMilliseconds);

            // Publish chat response event
            await PublishAsync(new ChatResponseEvent
            {
                RequestId = request.RequestId,
                Content = response.Content,
                TokensUsed = response.Usage?.TotalTokens ?? 0,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, ct: cancellationToken);

            // Record the AI decision as an event (Event Sourcing)
            RaiseAIDecision(
                request.Message ?? string.Empty,
                response.Content ?? string.Empty,
                response.Usage?.TotalTokens ?? 0,
                new Dictionary<string, string>
                {
                    ["request_id"] = request.RequestId,
                    ["chat_type"] = toolCall != null ? "tool_execution" : "sync"
                });

            // Auto-confirm if configured and EventStore is present
            if (AutoConfirmEvents && EventStore != null)
            {
                await ConfirmEventsAsync(cancellationToken);
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            // External cancellation (e.g., HTTP request aborted). This is expected and should not be
            // logged as an error-level "LLM call failed".
            Logger.LogDebug("Agent [{AgentId}] LLM call canceled by caller.", Id);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorType = ex.GetType().Name;

            LLMTelemetry.RecordLLMCallFailed(activity, provider, model, errorType, ex.Message,
                stopwatch.ElapsedMilliseconds);
            AgentLogMessages.LLMCallFailed(Logger, Id, errorType, ex.Message, ex);

            throw;
        }
    }

    /// <summary>
    /// Build LLM request from chat request.
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequest(
        ChatRequest request)
    {
        var settings = GetLLMSettings(request);
        if (request.StopSequences.Count > 0)
        {
            settings.StopSequences = request.StopSequences.ToList();
        }

        // Build message list:
        // - Default (history disabled): only include current user message.
        // - History enabled: include previous State.History + current user message.
        var messages = new List<AevatarChatMessage>();
        if (EnableChatHistoryInState)
        {
            lock (_historyLock)
            {
                if (State.History != null && State.History.Count > 0)
                {
                    foreach (var msg in State.History)
                    {
                        messages.Add(msg);
                    }
                }
            }
        }

        messages.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = request.Message
        });

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = BuildEffectiveSystemPromptWithSummary(),
            Messages = messages,
            Settings = settings
        };

        AttachToolsToRequest(llmRequest);

        if (!string.IsNullOrWhiteSpace(request.StageHint))
        {
            llmRequest.Context = new Dictionary<string, object>
            {
                ["stage_hint"] = request.StageHint!
            };
        }

        return llmRequest;
    }

    /// <summary>
    /// Determine the effective system prompt, preferring configuration override.
    /// </summary>
    protected virtual string? GetEffectiveSystemPrompt()
    {
        return !string.IsNullOrWhiteSpace(Config.SystemPrompt)
            ? Config.SystemPrompt
            : SystemPrompt;
    }

    /// <summary>
    /// Get LLM settings from chat request.
    /// </summary>
    protected virtual AevatarLLMSettings GetLLMSettings(ChatRequest request)
    {
        // Use request values if provided (considering 0 as a valid temperature), otherwise use configuration
        // For temperature: accept any value >= 0 as valid override
        // For maxTokens: only positive values are valid overrides
        var temperature = request.Temperature >= 0 ? request.Temperature : Config.Temperature;
        var maxTokens = request.MaxTokens > 0 ? request.MaxTokens : Config.MaxOutputTokens;

        return new AevatarLLMSettings
        {
            Temperature = temperature,
            MaxTokens = maxTokens,
            ModelId = Config.Model
        };
    }

    /// <summary>
    /// Create a chat request with the given message.
    /// </summary>
    public virtual ChatRequest CreateChatRequest(string message)
    {
        return ChatRequest.Create(message);
    }

    /// <summary>
    /// Generate a response to a message (convenience method).
    /// </summary>
    public virtual Task<ChatResponse> GenerateResponseAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatAsync(request, cancellationToken);
    }

    /// <summary>
    /// Generate a streaming response to a chat request.
    /// </summary>
    public virtual async IAsyncEnumerable<string> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException(
                "AI Agent must be initialized before use. Call InitializeAsync() first.");

        // Keep history bounded before building request (prevents token blow-up).
        await CompactChatHistoryIfNeededAsync(cancellationToken);

        // Ensure built-in tools are registered and cached.
        await InitializeToolsAsync(cancellationToken);

        // Best-effort MCP retry (throttled) before building request so tools can be visible to LLM.
        await TryReconnectMcpOnChatAsync(cancellationToken);

        // Build LLM request
        var llmRequest = BuildLLMRequest(request);

        // Optional: persist the user message (default off)
        if (EnableChatHistoryInState)
        {
            AddMessageToHistory(request.Message, AevatarChatRole.User);
        }

        // Optional: persist conversation to MemoryStore (default off, best-effort)
        await AppendChatMemoryAsync(AevatarChatRole.User, request.Message ?? string.Empty, request, cancellationToken);

        // Stream from LLM (with Hook/Harness stages; best-effort)
        var enumerator = GenerateLLMStreamWithHooksAsync(request.RequestId, llmRequest, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        var assistantBuffer = EnableChatHistoryInState
            ? new System.Text.StringBuilder()
            : null;
        var reasoningBuffer = EnableChatHistoryInState
            ? new System.Text.StringBuilder()
            : null;
        var completedSuccessfully = false;

        try
        {
            while (true)
            {
                // Fast cancel path: even if provider is slow to observe the token,
                // we still stop yielding as soon as cancellation is requested.
                cancellationToken.ThrowIfCancellationRequested();

                AevatarLLMToken token;
                try
                {
                    var hasNext = await enumerator.MoveNextAsync();
                    if (!hasNext) break;
                    token = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Normal: streaming request canceled by caller (e.g., client disconnected).
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in streaming chat request {RequestId}", request.RequestId);
                    throw;
                }

                // Streaming + tools:
                // - If the model returns a function call mid-stream, execute tools non-streaming and
                //   emit the final answer as a single chunk (best-effort).
                if (token.AevatarFunctionCall != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var toolCallResponse = new AevatarLLMResponse
                    {
                        AevatarFunctionCall = token.AevatarFunctionCall,
                        // Capture any reasoning accumulated so far for the history/logging if needed
                        Content = assistantBuffer?.ToString() ?? string.Empty
                    };

                    var accumulatedReasoning = reasoningBuffer?.ToString();
                    if (!string.IsNullOrEmpty(accumulatedReasoning))
                    {
                        toolCallResponse.Metadata ??= new Dictionary<string, object>();
                        toolCallResponse.Metadata["reasoning_content"] = accumulatedReasoning;
                    }

                    var (finalResponse, _) = await ExecuteToolCallLoopAsync(
                        request,
                        llmRequest,
                        toolCallResponse,
                        cancellationToken);

                    var finalText = finalResponse.Content ?? string.Empty;
                    if (!string.IsNullOrEmpty(finalText))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        assistantBuffer?.Append(finalText);
                        yield return finalText;
                    }

                    completedSuccessfully = true;
                    break;
                }

                var content = token.Content;
                var reasoning = token.ReasoningContent;
                var isComplete = token.IsComplete;

                if (!string.IsNullOrEmpty(reasoning))
                {
                    reasoningBuffer?.Append(reasoning);
                }

                if (!string.IsNullOrEmpty(content))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    assistantBuffer?.Append(content);
                    yield return content;
                }

                if (isComplete)
                    break;
            }

            completedSuccessfully = true;
        }
        finally
        {
            await enumerator.DisposeAsync();

            // Persist assistant message only if stream completed successfully
            if (EnableChatHistoryInState && completedSuccessfully)
            {
                var assistantText = assistantBuffer?.ToString() ?? string.Empty;
                var finalReasoning = reasoningBuffer?.ToString();

                if (!string.IsNullOrEmpty(assistantText) || !string.IsNullOrEmpty(finalReasoning))
                {
                    var msg = new AevatarChatMessage
                    {
                        Role = AevatarChatRole.Assistant,
                        Content = assistantText,
                        Timestamp = TimestampHelper.GetUtcNow()
                    };

                    if (!string.IsNullOrEmpty(finalReasoning))
                    {
                        msg.Metadata.Add("reasoning_content", finalReasoning);
                    }

                    AddMessageToHistory(msg);
                }
            }

            // Optional: persist assistant output to MemoryStore (default off, best-effort)
            if (completedSuccessfully)
            {
                var assistantText = assistantBuffer?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(assistantText))
                {
                    await AppendChatMemoryAsync(AevatarChatRole.Assistant, assistantText, request, cancellationToken);
                }
            }

            // Compact after streaming finishes (keeps state bounded for next call).
            // If caller canceled, skip to avoid surfacing extra TaskCanceledException noise.
            if (!cancellationToken.IsCancellationRequested)
            {
                await CompactChatHistoryIfNeededAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// Generate a streaming response to a message (convenience method).
    /// </summary>
    public virtual IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        var request = CreateChatRequest(message);
        return ChatStreamAsync(request, cancellationToken);
    }

    /// <summary>
    /// Check if the LLM provider supports streaming.
    /// </summary>
    public virtual async Task<bool> SupportsStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            return false;

        var modelInfo = await LLMProvider.GetModelInfoAsync(cancellationToken);
        return modelInfo.SupportsStreaming;
    }
}