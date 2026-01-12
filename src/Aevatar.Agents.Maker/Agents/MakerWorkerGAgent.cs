using System.Diagnostics;
using System.Text;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.AI;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Worker Agent
//  Pure event-driven with Streaming LLM support
//  
//  Optimizations:
//  - Streaming LLM responses for lower latency
//  - Cancellation support for early termination
//  - TTFT (Time To First Token) monitoring
//  - Progressive format validation
//
//  Note: Resilience (retry, circuit breaker) is built into
//  AevatarLLMProviderBase - no manual wrapping needed.
// ============================================================

/// <summary>
/// MAKER Worker Agent - executes LLM proposal generations.
/// All communication happens through events (no direct method calls).
/// 
/// Event Flow:
/// 1. Receives InitializeWorkerRequest -> Initializes LLM -> Publishes WorkerInitialized
/// 2. Receives GenerateProposalRequest -> Calls LLM (streaming) -> Publishes ProposalResult
/// 3. Receives CancelCurrentRequest -> Cancels ongoing LLM call (early termination)
/// </summary>
public class MakerWorkerGAgent : AIGAgentBase<MakerWorkerState, MakerWorkerConfig>
{
    // Cancellation support for early termination
    private CancellationTokenSource? _currentRequestCts;
    private readonly object _ctsLock = new();
    
    // Active prefix tracking - requests not matching this prefix are skipped immediately
    // This prevents wasting LLM calls on stale requests that would be discarded anyway
    private volatile string? _activePrefix;

    public MakerWorkerGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"MAKER Worker [{CustomState.WorkerId}]");
    }

    #region Event Handlers

    /// <summary>
    /// Handle initialization request from coordinator.
    /// </summary>
    [EventHandler]
    public async Task HandleInitializeWorkerRequest(InitializeWorkerRequest request)
    {
        Logger.LogInformation("[WORKER-INIT] Worker {Id} initializing with provider: {Provider} (index: {Index})",
            Id, request.ProviderName, request.WorkerIndex);

        try
        {
            // Initialize AI capabilities
            // Note: LLMProvider has built-in resilience (retry, circuit breaker)
            await InitializeAsync(request.ProviderName, config =>
            {
                config.Temperature = request.Temperature;
                config.MaxOutputTokens = 2048;
            });
            
            Logger.LogInformation("[WORKER-INIT] Worker {Id} successfully initialized with {Provider}",
                Id, request.ProviderName);

            // Setup state
            CustomState.WorkerId = Id.Length > 8 ? Id[..8] : Id;
            CustomState.CoordinatorId = request.CoordinatorId;
            CustomState.TotalProposals = 0;
            CustomState.SuccessfulProposals = 0;
            CustomState.FailedProposals = 0;

            // Setup config
            CustomConfig.LlmProviderName = request.ProviderName;
            CustomConfig.DefaultTemperature = request.Temperature;
            CustomConfig.DefaultMaxTokens = 2048;
            CustomConfig.WorkerIndex = request.WorkerIndex;

            // Notify coordinator that worker is ready (UP to parent)
            await PublishAsync(new WorkerInitialized
            {
                WorkerId = CustomState.WorkerId,
                CoordinatorId = request.CoordinatorId,
                WorkerIndex = request.WorkerIndex
            }, EventDirection.Up);

            Logger.LogInformation("Worker {WorkerId} initialized for coordinator {CoordinatorId}",
                CustomState.WorkerId, request.CoordinatorId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Worker {Id} failed to initialize", Id);
            throw;
        }
    }

    /// <summary>
    /// Handle active prefix update from coordinator.
    /// Requests not matching this prefix will be skipped immediately (no LLM call).
    /// </summary>
    [EventHandler]
    public Task HandleUpdateActivePrefix(UpdateActivePrefix request)
    {
        var oldPrefix = _activePrefix;
        _activePrefix = request.ActivePrefix;
        
        Logger.LogWarning(">>> [PREFIX-RECV] Worker {WorkerId} active prefix updated: '{Old}' -> '{New}'",
            CustomState.WorkerId, oldPrefix ?? "null", request.ActivePrefix);
        
        // Also cancel any ongoing request since prefix changed
        lock (_ctsLock)
        {
            if (_currentRequestCts != null && !_currentRequestCts.IsCancellationRequested)
            {
                Logger.LogWarning("[PREFIX] Worker {WorkerId} cancelling ongoing request due to prefix change",
                    CustomState.WorkerId);
                _currentRequestCts.Cancel();
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Handle cancellation request from coordinator (early termination).
    /// </summary>
    [EventHandler]
    public Task HandleCancelCurrentRequest(CancelCurrentRequest request)
    {
        lock (_ctsLock)
        {
            if (_currentRequestCts != null && !_currentRequestCts.IsCancellationRequested)
            {
                Logger.LogWarning("[CANCEL] Worker {WorkerId} received cancel request (reason: {Reason}), cancelling LLM call",
                    CustomState.WorkerId, request.Reason);
                _currentRequestCts.Cancel();
            }
            else
            {
                Logger.LogWarning("[CANCEL] Worker {WorkerId} received cancel but no active request to cancel (already finished?)",
                    CustomState.WorkerId);
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle proposal generation request from coordinator.
    /// Uses streaming for lower latency and supports early termination.
    /// </summary>
    [EventHandler]
    public async Task HandleGenerateProposalRequest(GenerateProposalRequest request)
    {
        // ============================================================
        //  CRITICAL: Check if request is stale BEFORE starting LLM call
        //  This prevents wasting LLM tokens on requests that would be discarded
        // ============================================================
        var requestPrefix = ExtractPrefix(request.RequestId);

        // 如果是新 Task，清理旧前缀，避免跨任务误判为 stale
        if (!string.Equals(CustomState.CurrentTaskId, request.TaskId, StringComparison.Ordinal))
        {
            _activePrefix = null;
        }

        var currentActivePrefix = _activePrefix;
        if (!string.IsNullOrEmpty(currentActivePrefix) && 
            !string.IsNullOrEmpty(requestPrefix) &&
            !string.Equals(requestPrefix, currentActivePrefix, StringComparison.Ordinal))
        {
            Logger.LogWarning(">>> [SKIP-STALE] Worker {WorkerId} skipping stale request {RequestId} (activePrefix='{Prefix}')",
                CustomState.WorkerId, request.RequestId, currentActivePrefix);
            return; // Skip this request entirely - don't waste LLM call
        }

        // 接受本轮请求后，如果当前前缀为空，则设为本轮前缀（保持幂等，不覆盖 coordinator 下发的新前缀）
        if (string.IsNullOrEmpty(_activePrefix))
        {
            _activePrefix = requestPrefix;
        }
        
        CustomState.CurrentTaskId = request.TaskId;
        CustomState.CurrentRequestId = request.RequestId;
        CustomState.LastActivity = Timestamp.FromDateTime(DateTime.UtcNow);

        var startTime = Stopwatch.GetTimestamp();
        
        Logger.LogDebug("Worker {WorkerId} processing request {RequestId} for task {TaskId}",
            CustomState.WorkerId, request.RequestId, request.TaskId);

        // Create cancellation token for this request
        CancellationTokenSource cts;
        lock (_ctsLock)
        {
            _currentRequestCts?.Dispose();
            _currentRequestCts = new CancellationTokenSource();
            cts = _currentRequestCts;
        }

        string? content = null;
        string? error = null;
        var success = false;
        int promptTokens = 0, completionTokens = 0, totalTokens = 0;
        long? ttftMs = null;

        // Generate proposal ID early for streaming events
        // CRITICAL: Increment counter FIRST to avoid race condition with concurrent requests
        // Each Worker may receive multiple requests in parallel, must ensure unique IDs
        CustomState.TotalProposals++;
        var proposalId = request.IsDecomposition
            ? $"D{CustomState.TotalProposals}"
            : $"S{CustomState.TotalProposals}";

        try
        {
            // ─────────────────────────────────────────────────────────
            //  Emit a synthetic "first token" to ensure UI gets
            //  WorkerStarted + LlmCallStart even before real stream arrives
            // ─────────────────────────────────────────────────────────
            await PublishAsync(new StreamingToken
            {
                RequestId = CustomState.CurrentRequestId,
                TaskId = CustomState.CurrentTaskId,
                WorkerId = CustomState.WorkerId,
                ProposalId = proposalId,
                Token = string.Empty,
                AccumulatedContent = string.Empty,
                TokenIndex = 0,
                IsFirstToken = true,
                IsLastToken = false,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProviderName = CustomConfig.LlmProviderName,
                SystemPrompt = request.SystemPrompt ?? string.Empty,
                UserPrompt = request.UserPrompt ?? string.Empty
            }, EventDirection.Up, cts.Token);

            var llmRequest = new AevatarLLMRequest
            {
                SystemPrompt = request.SystemPrompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxTokens
                },
                Messages = [new AevatarChatMessage { Role = AevatarChatRole.User, Content = request.UserPrompt }]
            };

            // Check if streaming is supported
            var modelInfo = await LLMProvider.GetModelInfoAsync(cts.Token);
            
            if (modelInfo.SupportsStreaming)
            {
                (content, promptTokens, completionTokens, ttftMs) = 
                    await GenerateWithStreamingAsync(llmRequest, request.IsDecomposition, startTime, proposalId, cts.Token);
            }
            else
            {
                // Fallback: non-streaming (LLMProvider has built-in resilience)
                var response = await GenerateLLMWithHooksAsync(
                    requestId: string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId,
                    llmRequest: llmRequest,
                    cancellationToken: cts.Token);
                content = response.Content;
                
                if (response.Usage != null)
                {
                    promptTokens = response.Usage.PromptTokens;
                    completionTokens = response.Usage.CompletionTokens;
                }
            }

            totalTokens = promptTokens + completionTokens;
            success = !string.IsNullOrWhiteSpace(content);

            if (success)
            {
                CustomState.SuccessfulProposals++;
            }
            else
            {
                CustomState.FailedProposals++;
                error = "Empty response from LLM";
            }
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Worker {WorkerId} request {RequestId} was cancelled (early termination)",
                CustomState.WorkerId, request.RequestId);
            error = "Cancelled (early termination - consensus reached)";
            CustomState.FailedProposals++;
        }
        catch (CircuitBreakerOpenException cbEx)
        {
            // Circuit breaker is open - LLM provider is temporarily unavailable
            Logger.LogWarning("Worker {WorkerId} circuit breaker open for {Provider} until {Until}",
                CustomState.WorkerId, cbEx.ProviderName, cbEx.OpenUntil);
            error = $"Provider temporarily unavailable (circuit breaker open until {cbEx.OpenUntil:HH:mm:ss})";
            CustomState.FailedProposals++;
        }
        catch (LLMCallException llmEx)
        {
            // LLM call failed after all retries
            Logger.LogWarning("Worker {WorkerId} LLM call failed after {Attempts} attempts: {Error}",
                CustomState.WorkerId, llmEx.AttemptsUsed, llmEx.Message);
            error = $"LLM call failed after {llmEx.AttemptsUsed} attempts: {llmEx.InnerException?.Message ?? llmEx.Message}";
            CustomState.FailedProposals++;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Worker {WorkerId} failed to generate proposal for {RequestId}",
                CustomState.WorkerId, request.RequestId);
            error = ex.Message;
            CustomState.FailedProposals++;
        }
        finally
        {
            lock (_ctsLock)
            {
                if (_currentRequestCts == cts)
                {
                    _currentRequestCts = null;
                }
            }
            cts.Dispose();
        }

        var latencyMs = (long)((Stopwatch.GetTimestamp() - startTime) * 1000.0 / Stopwatch.Frequency);

        // Send result back to coordinator (UP direction to parent stream)
        Logger.LogInformation("[PROPOSAL-SEND] Worker {WorkerId} ({Provider}) sending proposal {ProposalId}, RequestId='{RequestId}', TaskId='{TaskId}', ContentLen={ContentLen}, Success={Success}",
            CustomState.WorkerId, CustomConfig.LlmProviderName, proposalId, request.RequestId, request.TaskId, content?.Length ?? 0, success);
        
        await PublishAsync(new ProposalResult
        {
            RequestId = request.RequestId,
            TaskId = request.TaskId,
            WorkerId = CustomState.WorkerId,
            ProposalId = proposalId,
            Content = content ?? string.Empty,
            Success = success,
            Error = error ?? string.Empty,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            LatencyMs = latencyMs,
            ProviderName = CustomConfig.LlmProviderName,
            TtftMs = ttftMs ?? 0  // Time To First Token
        }, EventDirection.Up);
        
        Logger.LogDebug(
            "Worker {WorkerId} completed request {RequestId} in {LatencyMs}ms (TTFT: {TTFT}ms, tokens: {Prompt}+{Completion}={Total})",
            CustomState.WorkerId, request.RequestId, latencyMs, ttftMs ?? -1, promptTokens, completionTokens, totalTokens);
    }

    #endregion

    #region Streaming Support

    /// <summary>
    /// Generate LLM response using streaming for lower latency.
    /// Publishes real-time StreamingToken events for live UI updates.
    /// </summary>
    private async Task<(string Content, int PromptTokens, int CompletionTokens, long? TtftMs)> GenerateWithStreamingAsync(
        AevatarLLMRequest request,
        bool isDecomposition,
        long startTimestamp,
        string proposalId,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        var firstTokenReceived = false;
        long? ttftMs = null;
        int promptTokens = 0, completionTokens = 0;
        var tokenIndex = 0;

        Logger.LogWarning("[STREAMING] Worker {WorkerId} ({Provider}) starting streaming for task {TaskId}, proposalId={ProposalId}",
            CustomState.WorkerId, CustomConfig.LlmProviderName, CustomState.CurrentTaskId, proposalId);

        // Stream tokens with Hook/Harness stages (best-effort).
        var requestId = string.IsNullOrWhiteSpace(CustomState.CurrentRequestId)
            ? Guid.NewGuid().ToString("N")
            : CustomState.CurrentRequestId;

        await foreach (var token in GenerateLLMStreamWithHooksAsync(requestId, request, ct))
        {
            // Track Time To First Token
            var isFirst = !firstTokenReceived;
            if (isFirst)
            {
                firstTokenReceived = true;
                ttftMs = (long)((Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency);
                
                Logger.LogWarning("[STREAMING] Worker {WorkerId} first token received, TTFT: {TtftMs}ms, token: '{Token}'",
                    CustomState.WorkerId, ttftMs, 
                    token.Content?.Length > 50 ? token.Content[..50] + "..." : token.Content);
            }

            // Accumulate content
            if (!string.IsNullOrEmpty(token.Content))
            {
                sb.Append(token.Content);
                tokenIndex++;
                
                // ============================================================
                // STREAMING: Send events frequently for real-time UI
                // - Every 2 tokens for responsive display
                // - Accumulated content sent every 300 chars to reduce bandwidth
                // ============================================================
                var shouldSendEvent = isFirst                           // Always send first token
                    || tokenIndex % 2 == 0                              // Every 2nd token for responsiveness
                    || sb.Length % 300 < token.Content.Length;          // Every ~300 chars boundary
                
                if (shouldSendEvent)
                {
                    var streamingToken = new StreamingToken
                    {
                        RequestId = CustomState.CurrentRequestId,
                        TaskId = CustomState.CurrentTaskId,
                        WorkerId = CustomState.WorkerId,
                        ProposalId = proposalId,
                        Token = token.Content,
                        // Only send full accumulated content on first token and periodically
                        AccumulatedContent = (isFirst || sb.Length % 500 < token.Content.Length) 
                            ? sb.ToString() 
                            : "",  // Empty = frontend appends Token only
                        TokenIndex = tokenIndex,
                        IsFirstToken = isFirst,
                        IsLastToken = false,
                        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                        ProviderName = CustomConfig.LlmProviderName
                    };
                    
                    // Only send prompts on first token
                    if (isFirst)
                    {
                        streamingToken.SystemPrompt = request.SystemPrompt ?? "";
                        streamingToken.UserPrompt = request.Messages?.FirstOrDefault()?.Content ?? "";
                    }
                    
                    await PublishAsync(streamingToken, EventDirection.Up, ct);
                }
            }

            // Early format validation for decomposition (JSON expected)
            if (isDecomposition && sb.Length > 200)
            {
                var partial = sb.ToString();
                if (IsObviouslyInvalidJson(partial))
                {
                    Logger.LogWarning("Worker {WorkerId} detected invalid JSON format early, continuing anyway",
                        CustomState.WorkerId);
                    // Note: We don't cancel here - let the LLM finish and report the error
                    // Cancellation would waste the already-consumed tokens
                }
            }
        }

        // Send final token marker
        await PublishAsync(new StreamingToken
        {
            RequestId = CustomState.CurrentRequestId,
            TaskId = CustomState.CurrentTaskId,
            WorkerId = CustomState.WorkerId,
            ProposalId = proposalId,
            Token = string.Empty,
            AccumulatedContent = sb.ToString(),
            TokenIndex = tokenIndex,
            IsFirstToken = false,
            IsLastToken = true,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ProviderName = CustomConfig.LlmProviderName
        }, EventDirection.Up, ct);

        // Estimate completion tokens if not provided
        if (completionTokens == 0 && sb.Length > 0)
        {
            // Rough estimate: ~4 chars per token for English
            completionTokens = sb.Length / 4;
        }

        return (sb.ToString(), promptTokens, completionTokens, ttftMs);
    }

    /// <summary>
    /// Quick check if partial content is obviously not valid JSON.
    /// Used for early detection of format errors.
    /// </summary>
    private static bool IsObviouslyInvalidJson(string partial)
    {
        var trimmed = partial.TrimStart();
        
        // JSON should start with { or [
        if (trimmed.Length > 0 && trimmed[0] != '{' && trimmed[0] != '[')
        {
            // Allow markdown code blocks
            if (trimmed.StartsWith("```"))
                return false;
            
            return true;
        }
        
        return false;
    }

    private static string? ExtractPrefix(string? requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return null;
        var lastColon = requestId.LastIndexOf(':');
        if (lastColon <= 0) return requestId;
        return requestId[..(lastColon + 1)]; // keep trailing colon so StartsWith matches rounds
    }

    #endregion
}
