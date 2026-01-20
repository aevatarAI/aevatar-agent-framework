using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Hooks.BuiltIn;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Hook/Harness pipeline (best-effort)
    // ============================================================

    private AevatarAgentHookPipeline? _hookPipeline;

    /// <summary>
    /// Additional hooks injected by host (DI) or set by derived agents.
    /// Default: empty.
    /// </summary>
    protected IEnumerable<IAevatarAgentHook> AdditionalHooks { get; set; } = Array.Empty<IAevatarAgentHook>();

    /// <summary>
    /// Hook options (DI-bindable).
    /// Default: conservative safe budgets.
    /// </summary>
    protected AevatarAgentHookOptions HookOptions { get; set; } = new();

    /// <summary>
    /// Create built-in hooks for the harness layer.
    /// NOTE: Populated in later tasks (BuiltIn hooks).
    /// </summary>
    protected virtual IEnumerable<IAevatarAgentHook> CreateBuiltInHooks()
        => new IAevatarAgentHook[]
        {
            new ExecutionTraceProgressHook((evt, ct) => PublishAsync(evt, EventDirection.Down, ct), Logger),
            new ToolOutputTruncationHook(),
            new ContextBudgetMonitorHook(Logger)
        };

    // ------------------------------------------------------------
    // Explicit injection (type-safe, best-effort)
    // ------------------------------------------------------------

    internal void InjectHookOptions(AevatarAgentHookOptions? options)
    {
        if (options == null)
            return;

        HookOptions = options;
        _hookPipeline = null; // Ensure injected options take effect.
    }

    internal void InjectAdditionalHooks(IEnumerable<IAevatarAgentHook>? hooks)
    {
        if (hooks == null)
            return;

        // Materialize to avoid deferred enumerables re-resolving from DI.
        AdditionalHooks = hooks as IAevatarAgentHook[] ?? hooks.ToArray();
        _hookPipeline = null; // Ensure injected hooks take effect.
    }

    private AevatarAgentHookPipeline GetHookPipeline()
    {
        return _hookPipeline ??= CreateHookPipeline();
    }

    protected virtual AevatarAgentHookPipeline CreateHookPipeline()
    {
        var hooks = CreateBuiltInHooks().Concat(AdditionalHooks);
        var logger = new LoggerAdapter<AevatarAgentHookPipeline>(Logger);
        return new AevatarAgentHookPipeline(hooks, HookOptions, logger);
    }

    private AevatarAgentHookContext CreateHookContext(
        string requestId,
        ChatRequest? chatRequest = null,
        bool isStreaming = false,
        AevatarAgentHookStopStatus? stopStatus = null,
        TimeSpan? duration = null,
        Exception? stopException = null,
        string? stopReason = null,
        AevatarLLMRequest? llmRequest = null,
        AevatarLLMResponse? llmResponse = null,
        string? toolName = null,
        Dictionary<string, object>? toolArguments = null,
        ToolExecutionResult? toolResult = null,
        string? toolCallId = null)
    {
        var pipeline = GetHookPipeline();
        var policy = pipeline.CreatePolicySnapshot(AllowInternalTools, AllowDangerousTools);

        return new AevatarAgentHookContext(
            agentId: Id.ToString(),
            agentType: GetType().FullName ?? GetType().Name,
            requestId: requestId,
            policy: policy)
        {
            ChatRequest = chatRequest,
            IsStreaming = isStreaming,
            StopStatus = stopStatus,
            StopReason = stopReason,
            Duration = duration,
            StopException = stopException,
            LlmRequest = llmRequest,
            LlmResponse = llmResponse,
            ToolName = toolName,
            ToolArguments = toolArguments,
            ToolResult = toolResult,
            ToolCallId = toolCallId
        };
    }

    private static string BuildStopReason(AevatarAgentHookStopStatus status)
    {
        return status switch
        {
            AevatarAgentHookStopStatus.Completed => "completed",
            AevatarAgentHookStopStatus.Aborted => "aborted",
            AevatarAgentHookStopStatus.Error => "error",
            _ => "unknown"
        };
    }

    private async Task RunSessionStartHooksAsync(ChatRequest request, bool isStreaming, CancellationToken cancellationToken)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
            return;

        var ctx = CreateHookContext(
            request.RequestId,
            chatRequest: request,
            isStreaming: isStreaming);
        await pipeline.RunOnSessionStartAsync(ctx, cancellationToken);
    }

    private async Task RunStopHooksAsync(
        ChatRequest request,
        bool isStreaming,
        AevatarAgentHookStopStatus status,
        TimeSpan duration,
        Exception? exception)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
            return;

        var ctx = CreateHookContext(
            request.RequestId,
            chatRequest: request,
            isStreaming: isStreaming,
            stopStatus: status,
            duration: duration,
            stopException: exception,
            stopReason: BuildStopReason(status));
        await pipeline.RunOnStopAsync(ctx, CancellationToken.None);
    }

    private async Task RunSessionEndHooksAsync(
        ChatRequest request,
        bool isStreaming,
        AevatarAgentHookStopStatus status,
        TimeSpan duration,
        Exception? exception)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
            return;

        var ctx = CreateHookContext(
            request.RequestId,
            chatRequest: request,
            isStreaming: isStreaming,
            stopStatus: status,
            duration: duration,
            stopException: exception,
            stopReason: BuildStopReason(status));
        await pipeline.RunOnSessionEndAsync(ctx, CancellationToken.None);
    }

    protected async Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(
        string requestId,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
        {
            return await LLMProvider.GenerateAsync(llmRequest, cancellationToken);
        }

        var ctx = CreateHookContext(requestId, llmRequest: llmRequest);

        await pipeline.RunBeforeLLMRequestAsync(ctx, cancellationToken);

        // Defense in depth:
        // - Hooks may set allowlist metadata in Context.
        // - Hooks must NOT widen tool visibility; re-apply tool policy after hooks.
        // Only enforce tool policy if tools are already attached for this request.
        // (Internal calls like history summarization intentionally do not attach tools.)
        if (llmRequest.Functions != null)
        {
            AttachToolsToRequest(llmRequest);
        }

        AevatarLLMResponse response;
        try
        {
            response = await LLMProvider.GenerateAsync(llmRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await pipeline.RunOnErrorAsync(ctx, ex, cancellationToken);
            throw;
        }

        ctx.LlmResponse = response;
        await pipeline.RunAfterLLMResponseAsync(ctx, cancellationToken);
        return response;
    }

    /// <summary>
    /// Stream tokens from provider with Hook/Harness stages (best-effort).
    ///
    /// 中文 + ASCII:
    /// - Streaming callers (e.g. Maker/Cognitive) historically bypassed the hook pipeline by calling
    ///   <c>LLMProvider.GenerateStreamAsync</c> directly.
    /// - This helper brings the same "BeforeLLMRequest / OnError" governance to streaming paths,
    ///   without changing existing streaming semantics.
    /// - We intentionally do NOT run AfterLLMResponse here because streaming callers typically aggregate
    ///   content themselves; a future extension can add an explicit "AfterStreamComplete" stage.
    /// </summary>
    protected async IAsyncEnumerable<AevatarLLMToken> GenerateLLMStreamWithHooksAsync(
        string requestId,
        AevatarLLMRequest llmRequest,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
        {
            await foreach (var token in LLMProvider.GenerateStreamAsync(llmRequest, cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                yield return token;
            }

            yield break;
        }

        var safeRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        var ctx = CreateHookContext(safeRequestId, llmRequest: llmRequest);

        await pipeline.RunBeforeLLMRequestAsync(ctx, cancellationToken);

        // Defense in depth: re-apply tool visibility policy after hooks (only if tools are attached).
        if (llmRequest.Functions != null)
        {
            AttachToolsToRequest(llmRequest);
        }

        IAsyncEnumerable<AevatarLLMToken> stream;
        try
        {
            stream = LLMProvider.GenerateStreamAsync(llmRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await pipeline.RunOnErrorAsync(ctx, ex, cancellationToken);
            throw;
        }

        var enumerator = stream.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                AevatarLLMToken token;
                try
                {
                    var hasNext = await enumerator.MoveNextAsync();
                    if (!hasNext) break;
                    token = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await pipeline.RunOnErrorAsync(ctx, ex, cancellationToken);
                    throw;
                }

                yield return token;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
        => GenerateLLMWithHooksAsync(request.RequestId, llmRequest, cancellationToken);

    private async Task<ToolExecutionResult> ExecuteAllowedToolWithHooksAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
        {
            return await ExecuteAllowedToolAsync(toolName, args, executionContext, llmRequest, cancellationToken);
        }

        var requestId = executionContext.GetSessionId?.Invoke() ?? Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(executionContext.ToolCallId))
            executionContext.ToolCallId = Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(executionContext.ToolName))
            executionContext.ToolName = toolName;

        var ctx = CreateHookContext(
            requestId,
            llmRequest: llmRequest,
            toolName: toolName,
            toolArguments: args,
            toolCallId: executionContext.ToolCallId);

        await pipeline.RunBeforeToolExecuteAsync(ctx, cancellationToken);

        // Optional: allow hooks to deny tool execution (purely restrictive).
        if (ctx.TryGetToolDenyReason(out var denyReason))
        {
            var payload = JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool execution denied by hook.",
                tool = toolName,
                reason = denyReason ?? "Denied"
            });

            return new ToolExecutionResult
            {
                ToolCallId = Guid.NewGuid().ToString("N"),
                ToolName = toolName,
                IsSuccess = false,
                ErrorMessage = denyReason ?? $"Tool '{toolName}' denied by hook.",
                Content = payload,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        var result = await ExecuteAllowedToolAsync(toolName, args, executionContext, llmRequest, cancellationToken);
        ctx.ToolResult = result;

        await pipeline.RunAfterToolExecuteAsync(ctx, cancellationToken);
        return result;
    }
}
