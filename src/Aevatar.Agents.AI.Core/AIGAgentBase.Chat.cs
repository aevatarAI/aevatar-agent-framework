using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

// ReSharper disable InconsistentNaming
namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ------------------------------------------------------------
    //  Baseline tool allowlist (framework-level)
    //
    //  WHY:
    //  - Some platforms want role-driven tool surfaces configured by YAML / policy.
    //  - We already support per-request allowlists via AIGAgentKeys.ToolAllowlist;
    //    this adds a *baseline* allowlist applied to every LLM request.
    //
    //  NOTE:
    //  - This affects both:
    //    - tool visibility (function schema)
    //    - tool execution (defense-in-depth recheck)
    // ------------------------------------------------------------

    private HashSet<string>? _fixedToolAllowlist;
    private LlmRequestRuntime? _llmRequestRuntime;
    private LlmRequestRuntime LlmRequest => _llmRequestRuntime ??= new LlmRequestRuntime(LlmRequestContext);

    private static readonly MethodInfo ChatRequestHandlerMethod =
        typeof(AIGAgentBase).GetMethod(nameof(HandleChatRequestEvent),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    private bool HasCustomChatRequestHandler()
    {
        var handlers = GetEventHandlers();
        foreach (var handler in handlers)
        {
            if (handler.IsAllEventHandler)
                continue;
            if (handler.ParameterType != typeof(ChatRequestEvent))
                continue;
            if (!Equals(handler.Method, ChatRequestHandlerMethod))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Event-driven chat entry (for YAML/role-based agents).
    /// </summary>
    [EventHandler(AllowSelfHandling = true)]
    protected virtual async Task HandleChatRequestEvent(ChatRequestEvent evt)
    {
        if (evt == null)
            return;

        // If a derived class provides its own ChatRequestEvent handler, skip the base handler to avoid duplicates.
        if (HasCustomChatRequestHandler())
            return;

        var requestId = string.IsNullOrWhiteSpace(evt.RequestId)
            ? Guid.NewGuid().ToString("N")
            : evt.RequestId;

        var request = new ChatRequest
        {
            RequestId = requestId,
            Message = evt.Message ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(evt.UserId))
        {
            request.Context["user_id"] = evt.UserId.Trim();
        }

        if (evt.Context != null)
        {
            foreach (var (key, value) in evt.Context)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    request.Context[key] = value ?? string.Empty;
                }
            }
        }

        if (evt.MaxTokens > 0)
        {
            request.MaxTokens = evt.MaxTokens;
        }

        if (evt.Temperature > 0)
        {
            request.Temperature = evt.Temperature;
        }

        // Transfer image keys for multimodal input
        if (evt.ImageKeys.Count > 0)
        {
            request.AddImageKeys(evt.ImageKeys);
        }

        const int DefaultStreamChunkEveryN = 8;
        var chunkEvery = evt.StreamChunkEveryN > 0 ? evt.StreamChunkEveryN : DefaultStreamChunkEveryN;
        chunkEvery = Math.Clamp(chunkEvery, 1, 128);

        var buffer = new StringBuilder();
        var pending = new StringBuilder();
        var rawChunkCount = 0;
        var publishedIndex = 0;

        await foreach (var chunk in ChatStreamAsync(request, CancellationToken.None))
        {
            if (string.IsNullOrEmpty(chunk))
                continue;

            buffer.Append(chunk);
            pending.Append(chunk);
            rawChunkCount++;

            if (rawChunkCount % chunkEvery != 0)
                continue;

            publishedIndex++;
            await PublishAsync(new ChatStreamChunkEvent
            {
                RequestId = requestId,
                Content = pending.ToString(),
                ChunkIndex = publishedIndex,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            pending.Clear();
        }

        if (pending.Length > 0)
        {
            publishedIndex++;
            await PublishAsync(new ChatStreamChunkEvent
            {
                RequestId = requestId,
                Content = pending.ToString(),
                ChunkIndex = publishedIndex,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        await PublishAsync(new ChatResponseEvent
        {
            RequestId = requestId,
            Content = buffer.ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }


    /// <summary>
    /// Set a fixed baseline tool allowlist for all LLM requests generated by this agent.
    /// Passing null/empty will clear the baseline allowlist (fallback to default behavior).
    /// </summary>
    public void SetFixedToolAllowlist(IEnumerable<string>? toolNames)
    {
        if (toolNames == null)
        {
            _fixedToolAllowlist = null;
            return;
        }

        var list = toolNames
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .ToList();

        _fixedToolAllowlist = list.Count == 0
            ? null
            : new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Streaming tool-call handling mode.
    ///
    /// 中文 + ASCII:
    /// - 默认：遇到工具调用时，停止 streaming，执行 tools（non-streaming），并把最终答案作为一个 chunk 返回
    /// - 目的：把“策略”从 while 循环里抽离出来，避免以后改行为时动到核心 streaming 逻辑
    /// </summary>
    protected enum StreamingToolCallMode
    {
        EmitFinalAnswerAsSingleChunk = 0,
        ContinueStreamingAfterTools = 1
    }

    /// <summary>
    /// Controls how <see cref="ChatStreamAsync"/> behaves when the model returns a function call mid-stream.
    /// Default: <see cref="StreamingToolCallMode.EmitFinalAnswerAsSingleChunk"/>.
    /// </summary>
    protected virtual StreamingToolCallMode StreamingToolCalls => StreamingToolCallMode.EmitFinalAnswerAsSingleChunk;

    /// <summary>
    /// Streaming token read error logging hook.
    ///
    /// 中文 + ASCII:
    /// - 默认：LogError + throw（保持现有行为）
    /// - Override：允许把特定 provider 的 mid-stream 断连降级为 Warning/Debug（但仍然 throw）
    /// </summary>
    protected virtual void LogChatStreamTokenReadException(Exception exception, ChatRequest request)
    {
        Logger.LogError(exception, "Error in streaming chat request {RequestId}", request.RequestId);
    }

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
        EnsureInitialized();

        var sessionStartedAt = DateTimeOffset.UtcNow;
        var sessionStarted = false;
        var stopStatus = AevatarAgentHookStopStatus.Completed;
        Exception? stopException = null;

        await RunSessionStartHooksAsync(request, isStreaming: false, cancellationToken);
        sessionStarted = true;

        var (provider, model) = GetProviderAndModelForTelemetry();
        using var llmCall = new LlmCallInstrumentationScope(Logger, Id, provider, model, isStreaming: false);

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

            // Resolve images for multimodal requests
            await ResolveAndAttachImagesAsync(request, llmRequest, cancellationToken);

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

            llmCall.RecordCompleted(
                promptTokens,
                completionTokens,
                promptChars: request.Message?.Length ?? 0,
                responseChars: response.Content?.Length ?? 0);

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
            llmCall.StopWithoutRecording();
            stopStatus = AevatarAgentHookStopStatus.Aborted;

            // External cancellation (e.g., HTTP request aborted). This is expected and should not be
            // logged as an error-level "LLM call failed".
            Logger.LogDebug("Agent [{AgentId}] LLM call canceled by caller.", Id);
            throw;
        }
        catch (Exception ex)
        {
            llmCall.RecordFailed(ex);
            stopStatus = AevatarAgentHookStopStatus.Error;
            stopException = ex;

            throw;
        }
        finally
        {
            if (sessionStarted)
            {
                var duration = DateTimeOffset.UtcNow - sessionStartedAt;
                await RunStopHooksAsync(request, isStreaming: false, stopStatus, duration, stopException);
                await RunSessionEndHooksAsync(request, isStreaming: false, stopStatus, duration, stopException);
            }
        }
    }

    /// <summary>
    /// Build LLM request from chat request.
    /// </summary>
    protected virtual AevatarLLMRequest BuildLLMRequest(
        ChatRequest request)
    {
        return LlmRequest.BuildRequest(request);
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
        EnsureInitialized();

        var sessionStartedAt = DateTimeOffset.UtcNow;
        var sessionStarted = false;
        var stopStatus = AevatarAgentHookStopStatus.Completed;
        Exception? stopException = null;

        await RunSessionStartHooksAsync(request, isStreaming: true, cancellationToken);
        sessionStarted = true;

        AevatarLLMRequest? llmRequest = null;
        IAsyncEnumerator<AevatarLLMToken>? enumerator = null;

        var assistantBuffer = EnableChatHistoryInState
            ? new StringBuilder()
            : null;
        var reasoningBuffer = EnableChatHistoryInState
            ? new StringBuilder()
            : null;
        var completedSuccessfully = false;

        try
        {
            llmRequest = await PrepareChatStreamAsync(request, cancellationToken);

            // Stream from LLM (with Hook/Harness stages; best-effort)
            Logger.LogDebug("[ChatStreamAsync] Getting async enumerator from GenerateLLMStreamWithHooksAsync...");
            enumerator = GenerateLLMStreamWithHooksAsync(request.RequestId, llmRequest, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            Logger.LogDebug("[ChatStreamAsync] Got enumerator, entering streaming loop...");

            var loopIteration = 0;
            while (true)
            {
                loopIteration++;
                Logger.LogDebug("[ChatStreamAsync] Loop iteration {Iter}, calling MoveNextAsync...", loopIteration);
                cancellationToken.ThrowIfCancellationRequested();

                AevatarLLMToken? token;
                try
                {
                    token = await TryReadNextStreamingTokenAsync(enumerator, request, cancellationToken);
                }
                catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
                {
                    stopStatus = AevatarAgentHookStopStatus.Aborted;
                    stopException = oce;
                    throw;
                }
                catch (Exception ex)
                {
                    stopStatus = AevatarAgentHookStopStatus.Error;
                    stopException = ex;
                    throw;
                }

            if (token == null)
            {
                Logger.LogDebug("[ChatStreamAsync] Token is null, breaking loop");
                break;
            }
            Logger.LogDebug("[ChatStreamAsync] Got token: Content={ContentLen}chars, FunctionCall={HasFunc}, IsComplete={IsComplete}",
                token.Content?.Length ?? 0, token.AevatarFunctionCall != null, token.IsComplete);

                // Streaming + tools:
                // - If the model returns a function call mid-stream, execute tools non-streaming and
                //   emit the final answer as a single chunk (best-effort).
                if (token.AevatarFunctionCall != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string? finalText;
                    try
                    {
                        switch (StreamingToolCalls)
                        {
                            case StreamingToolCallMode.EmitFinalAnswerAsSingleChunk:
                            {
                                finalText = await ExecuteToolCallFromStreamingTokenAsync(
                                    request,
                                    llmRequest,
                                    token,
                                    assistantBuffer,
                                    reasoningBuffer,
                                    cancellationToken);
                                break;
                            }
                            case StreamingToolCallMode.ContinueStreamingAfterTools:
                                throw new NotSupportedException(
                                    "ContinueStreamingAfterTools is not supported yet. " +
                                    "The current provider streaming pipeline cannot resume after executing tool calls.");
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
                    {
                        stopStatus = AevatarAgentHookStopStatus.Aborted;
                        stopException = oce;
                        throw;
                    }
                    catch (Exception ex)
                    {
                        stopStatus = AevatarAgentHookStopStatus.Error;
                        stopException = ex;
                        throw;
                    }

                    if (!string.IsNullOrEmpty(finalText))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        assistantBuffer?.Append(finalText);
                        yield return finalText;
                    }

                    completedSuccessfully = true;
                    yield break;
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
            if (enumerator != null)
            {
                await enumerator.DisposeAsync();
            }

            await PersistStreamingAssistantOutputAsync(
                request,
                assistantBuffer,
                reasoningBuffer,
                completedSuccessfully,
                cancellationToken);

            // Compact after streaming finishes (keeps state bounded for next call).
            // If caller canceled, skip to avoid surfacing extra TaskCanceledException noise.
            if (!cancellationToken.IsCancellationRequested)
            {
                await CompactChatHistoryIfNeededAsync(cancellationToken);
            }

            if (sessionStarted)
            {
                if (!completedSuccessfully && stopStatus == AevatarAgentHookStopStatus.Completed)
                    stopStatus = AevatarAgentHookStopStatus.Aborted;

                var duration = DateTimeOffset.UtcNow - sessionStartedAt;
                await RunStopHooksAsync(request, isStreaming: true, stopStatus, duration, stopException);
                await RunSessionEndHooksAsync(request, isStreaming: true, stopStatus, duration, stopException);
            }
        }
    }

    private async Task<AevatarLLMRequest> PrepareChatStreamAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        // Keep history bounded before building request (prevents token blow-up).
        await CompactChatHistoryIfNeededAsync(cancellationToken);

        // Ensure built-in tools are registered and cached.
        await InitializeToolsAsync(cancellationToken);

        // Best-effort MCP retry (throttled) before building request so tools can be visible to LLM.
        await TryReconnectMcpOnChatAsync(cancellationToken);

        // Build LLM request
        var llmRequest = BuildLLMRequest(request);

        // Resolve images for multimodal streaming requests
        await ResolveAndAttachImagesAsync(request, llmRequest, cancellationToken);

        // Optional: persist the user message (default off)
        if (EnableChatHistoryInState)
        {
            AddMessageToHistory(request.Message, AevatarChatRole.User);
        }

        // Optional: persist conversation to MemoryStore (default off, best-effort)
        await AppendChatMemoryAsync(AevatarChatRole.User, request.Message ?? string.Empty, request, cancellationToken);

        return llmRequest;
    }

    private async Task<AevatarLLMToken?> TryReadNextStreamingTokenAsync(
        IAsyncEnumerator<AevatarLLMToken> enumerator,
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var hasNext = await enumerator.MoveNextAsync();
            return hasNext ? enumerator.Current : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal: streaming request canceled by caller (e.g., client disconnected).
            throw;
        }
        catch (Exception ex)
        {
            LogChatStreamTokenReadException(ex, request);
            throw;
        }
    }

    private async Task<string> ExecuteToolCallFromStreamingTokenAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        AevatarLLMToken token,
        StringBuilder? assistantBuffer,
        StringBuilder? reasoningBuffer,
        CancellationToken cancellationToken)
    {
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

        return finalResponse.Content ?? string.Empty;
    }

    private async Task PersistStreamingAssistantOutputAsync(
        ChatRequest request,
        StringBuilder? assistantBuffer,
        StringBuilder? reasoningBuffer,
        bool completedSuccessfully,
        CancellationToken cancellationToken)
    {
        if (!completedSuccessfully)
            return;

        var assistantText = assistantBuffer?.ToString() ?? string.Empty;
        var finalReasoning = reasoningBuffer?.ToString();

        // Persist assistant message only if stream completed successfully
        if (EnableChatHistoryInState)
        {
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
        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            await AppendChatMemoryAsync(AevatarChatRole.Assistant, assistantText, request, cancellationToken);
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

        if (ActiveProviderConfig?.EnableStreaming == false)
            return false;

        if (ActiveProviderConfig?.EnableStreaming == true)
            return true;

        var modelInfo = await LLMProvider.GetModelInfoAsync(cancellationToken);
        return modelInfo.SupportsStreaming;
    }
}