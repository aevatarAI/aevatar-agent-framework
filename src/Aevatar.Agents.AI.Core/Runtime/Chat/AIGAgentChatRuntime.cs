using System.Runtime.CompilerServices;
using System.Text;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed class AIGAgentChatRuntime
{
    private readonly IAIGAgentChatRuntimeHost _host;

    internal AIGAgentChatRuntime(IAIGAgentChatRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        _host.EnsureInitialized();

        var sessionStartedAt = DateTimeOffset.UtcNow;
        var sessionStarted = false;
        var stopStatus = AevatarAgentHookStopStatus.Completed;
        Exception? stopException = null;

        await _host.RunSessionStartHooksAsync(request, isStreaming: false, cancellationToken);
        sessionStarted = true;

        var (provider, model) = _host.GetProviderAndModelForTelemetry();
        using var llmCall = new LlmCallInstrumentationScope(_host.Logger, _host.AgentId, provider, model, isStreaming: false);

        try
        {
            // Keep history bounded before building request (prevents token blow-up).
            await _host.CompactChatHistoryIfNeededAsync(cancellationToken);

            // Ensure built-in tools are registered and cached.
            await _host.InitializeToolsAsync(cancellationToken);

            // Best-effort: if MCP servers are configured but were unreachable earlier,
            // retry per chat call (throttled) so tools can "eventually become available".
            await _host.TryReconnectMcpOnChatAsync(cancellationToken);

            // Build LLM request from chat request
            var llmRequest = _host.BuildLLMRequest(request);

            // Optional: persist conversation to State.History (default off)
            if (_host.EnableChatHistoryInState)
            {
                _host.AddMessageToHistory(request.Message, AevatarChatRole.User);
            }

            // Optional: persist conversation to MemoryStore (default off, best-effort)
            await _host.AppendChatMemoryAsync(AevatarChatRole.User, request.Message ?? string.Empty, request, cancellationToken);

            // Call LLM
            var llmResponse = await _host.GenerateLLMWithHooksAsync(request, llmRequest, cancellationToken);
            ToolCallInfo? toolCall = null;

            // Tool/function calling loop
            if (llmResponse.AevatarFunctionCall != null)
            {
                var (finalResponse, lastToolCall) = await _host.ExecuteToolCallLoopAsync(
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

            if (_host.EnableChatHistoryInState && !string.IsNullOrEmpty(response.Content))
            {
                _host.AddMessageToHistory(response.Content, AevatarChatRole.Assistant);
            }

            // Optional: persist assistant output to MemoryStore (default off, best-effort)
            if (!string.IsNullOrWhiteSpace(response.Content))
            {
                await _host.AppendChatMemoryAsync(AevatarChatRole.Assistant, response.Content!, request, cancellationToken);
            }

            // Compact again after appending new messages (keeps state bounded for next call).
            await _host.CompactChatHistoryIfNeededAsync(cancellationToken);

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
            await _host.PublishAsync(new ChatResponseEvent
            {
                RequestId = request.RequestId,
                Content = response.Content,
                TokensUsed = response.Usage?.TotalTokens ?? 0,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, EventDirection.Down, cancellationToken);

            // Record the AI decision as an event (Event Sourcing)
            _host.RaiseAIDecision(
                request.Message ?? string.Empty,
                response.Content ?? string.Empty,
                response.Usage?.TotalTokens ?? 0,
                new Dictionary<string, string>
                {
                    ["request_id"] = request.RequestId,
                    ["chat_type"] = toolCall != null ? "tool_execution" : "sync"
                });

            // Auto-confirm if configured and EventStore is present
            if (_host.AutoConfirmEvents && _host.EventStore != null)
            {
                await _host.ConfirmEventsAsync(cancellationToken);
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            llmCall.StopWithoutRecording();
            stopStatus = AevatarAgentHookStopStatus.Aborted;

            // External cancellation (e.g., HTTP request aborted). This is expected and should not be
            // logged as an error-level "LLM call failed".
            _host.Logger.LogDebug("Agent [{AgentId}] LLM call canceled by caller.", _host.AgentId);
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
                await _host.RunStopHooksAsync(request, isStreaming: false, stopStatus, duration, stopException);
                await _host.RunSessionEndHooksAsync(request, isStreaming: false, stopStatus, duration, stopException);
            }
        }
    }

    internal async IAsyncEnumerable<string> ChatStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _host.EnsureInitialized();

        var sessionStartedAt = DateTimeOffset.UtcNow;
        var sessionStarted = false;
        var stopStatus = AevatarAgentHookStopStatus.Completed;
        Exception? stopException = null;

        await _host.RunSessionStartHooksAsync(request, isStreaming: true, cancellationToken);
        sessionStarted = true;

        AevatarLLMRequest? llmRequest = null;
        IAsyncEnumerator<AevatarLLMToken>? enumerator = null;

        var assistantBuffer = _host.EnableChatHistoryInState ? new StringBuilder() : null;
        var reasoningBuffer = _host.EnableChatHistoryInState ? new StringBuilder() : null;
        var completedSuccessfully = false;

        try
        {
            llmRequest = await PrepareChatStreamAsync(request, cancellationToken);

            // Stream from LLM (with Hook/Harness stages; best-effort)
            _host.Logger.LogDebug("[ChatStreamAsync] Getting async enumerator from GenerateLLMStreamWithHooksAsync...");
            enumerator = _host.GenerateLLMStreamWithHooksAsync(request.RequestId, llmRequest, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            _host.Logger.LogDebug("[ChatStreamAsync] Got enumerator, entering streaming loop...");

            var loopIteration = 0;
            while (true)
            {
                loopIteration++;
                _host.Logger.LogDebug("[ChatStreamAsync] Loop iteration {Iter}, calling MoveNextAsync...", loopIteration);
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
                    _host.Logger.LogDebug("[ChatStreamAsync] Token is null, breaking loop");
                    break;
                }

                _host.Logger.LogDebug("[ChatStreamAsync] Got token: Content={ContentLen}chars, FunctionCall={HasFunc}, IsComplete={IsComplete}",
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
                        switch (_host.GetStreamingToolCallMode())
                        {
                            case AIGAgentBase.StreamingToolCallMode.EmitFinalAnswerAsSingleChunk:
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
                            case AIGAgentBase.StreamingToolCallMode.ContinueStreamingAfterTools:
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
                await _host.CompactChatHistoryIfNeededAsync(cancellationToken);
            }

            if (sessionStarted)
            {
                if (!completedSuccessfully && stopStatus == AevatarAgentHookStopStatus.Completed)
                    stopStatus = AevatarAgentHookStopStatus.Aborted;

                var duration = DateTimeOffset.UtcNow - sessionStartedAt;
                await _host.RunStopHooksAsync(request, isStreaming: true, stopStatus, duration, stopException);
                await _host.RunSessionEndHooksAsync(request, isStreaming: true, stopStatus, duration, stopException);
            }
        }
    }

    private async Task<AevatarLLMRequest> PrepareChatStreamAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        // Keep history bounded before building request (prevents token blow-up).
        await _host.CompactChatHistoryIfNeededAsync(cancellationToken);

        // Ensure built-in tools are registered and cached.
        await _host.InitializeToolsAsync(cancellationToken);

        // Best-effort MCP retry (throttled) before building request so tools can be visible to LLM.
        await _host.TryReconnectMcpOnChatAsync(cancellationToken);

        // Build LLM request
        var llmRequest = _host.BuildLLMRequest(request);

        // Optional: persist the user message (default off)
        if (_host.EnableChatHistoryInState)
        {
            _host.AddMessageToHistory(request.Message, AevatarChatRole.User);
        }

        // Optional: persist conversation to MemoryStore (default off, best-effort)
        await _host.AppendChatMemoryAsync(AevatarChatRole.User, request.Message ?? string.Empty, request, cancellationToken);

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
            _host.LogChatStreamTokenReadException(ex, request);
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

        var (finalResponse, _) = await _host.ExecuteToolCallLoopAsync(
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
        if (_host.EnableChatHistoryInState)
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

                _host.AddMessageToHistory(msg);
            }
        }

        // Optional: persist assistant output to MemoryStore (default off, best-effort)
        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            await _host.AppendChatMemoryAsync(AevatarChatRole.Assistant, assistantText, request, cancellationToken);
        }
    }
}

