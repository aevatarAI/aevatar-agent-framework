using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed class AIGAgentHookRuntime
{
    private readonly IAIGAgentHookRuntimeHost _host;
    private AevatarAgentHookPipeline? _hookPipeline;

    internal AIGAgentHookRuntime(IAIGAgentHookRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal void Invalidate() => _hookPipeline = null;

    private AevatarAgentHookPipeline GetHookPipeline()
        => _hookPipeline ??= CreateHookPipeline();

    private AevatarAgentHookPipeline CreateHookPipeline()
    {
        var hooks = _host.CreateBuiltInHooks().Concat(_host.AdditionalHooks);
        var logger = new LoggerAdapter<AevatarAgentHookPipeline>(_host.Logger);
        return new AevatarAgentHookPipeline(hooks, _host.HookOptions, logger);
    }

    private sealed class LoggerAdapter<T>(ILogger inner) : ILogger<T>
    {
        private readonly ILogger _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
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
        string? toolCallId = null,
        string? eventId = null,
        string? eventType = null,
        string? eventHandlerName = null,
        string? eventHandlerType = null,
        TimeSpan? eventHandlerDuration = null,
        Exception? eventHandlerException = null)
    {
        var pipeline = GetHookPipeline();
        var policy = pipeline.CreatePolicySnapshot(_host.AllowInternalTools, _host.AllowDangerousTools);

        return new AevatarAgentHookContext(
            agentId: _host.AgentId,
            agentType: _host.AgentType,
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
            ToolCallId = toolCallId,
            EventId = eventId,
            EventType = eventType,
            EventHandlerName = eventHandlerName,
            EventHandlerType = eventHandlerType,
            EventHandlerDuration = eventHandlerDuration,
            EventHandlerException = eventHandlerException
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

    internal async Task OnEventHandlerStartAsync(
        EventEnvelope envelope,
        GAgentBase.EventHandlerMetadata handler,
        object? payload,
        CancellationToken ct)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
            return;

        var requestId = _host.ResolveEventRequestId(envelope);
        var ctx = CreateHookContext(
            requestId,
            eventId: string.IsNullOrWhiteSpace(envelope.Id) ? requestId : envelope.Id,
            eventType: _host.ResolveEventType(envelope, payload),
            eventHandlerName: handler.Method.Name,
            eventHandlerType: handler.Method.DeclaringType?.FullName);

        await pipeline.RunBeforeEventHandlerAsync(ctx, ct);
    }

    internal async Task OnEventHandlerEndAsync(
        EventEnvelope envelope,
        GAgentBase.EventHandlerMetadata handler,
        object? payload,
        TimeSpan duration,
        Exception? exception,
        CancellationToken ct)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
            return;

        var requestId = _host.ResolveEventRequestId(envelope);
        var ctx = CreateHookContext(
            requestId,
            eventId: string.IsNullOrWhiteSpace(envelope.Id) ? requestId : envelope.Id,
            eventType: _host.ResolveEventType(envelope, payload),
            eventHandlerName: handler.Method.Name,
            eventHandlerType: handler.Method.DeclaringType?.FullName,
            eventHandlerDuration: duration,
            eventHandlerException: exception);

        await pipeline.RunAfterEventHandlerAsync(ctx, ct);
    }

    internal async Task RunSessionStartHooksAsync(ChatRequest request, bool isStreaming, CancellationToken ct)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
            return;

        var ctx = CreateHookContext(
            request.RequestId,
            chatRequest: request,
            isStreaming: isStreaming);
        await pipeline.RunOnSessionStartAsync(ctx, ct);
    }

    internal async Task RunStopHooksAsync(
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

    internal async Task RunSessionEndHooksAsync(
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

    internal async Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(
        string requestId,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
        {
            return await _host.LLMProvider.GenerateAsync(llmRequest, ct);
        }

        var ctx = CreateHookContext(requestId, llmRequest: llmRequest);

        await pipeline.RunBeforeLLMRequestAsync(ctx, ct);

        // Defense in depth:
        // - Hooks may set allowlist metadata in Context.
        // - Hooks must NOT widen tool visibility; re-apply tool policy after hooks.
        // Only enforce tool policy if tools are already attached for this request.
        // (Internal calls like history summarization intentionally do not attach tools.)
        if (llmRequest.Functions != null)
        {
            _host.AttachToolsToRequest(llmRequest);
        }

        AevatarLLMResponse response;
        try
        {
            response = await _host.LLMProvider.GenerateAsync(llmRequest, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await pipeline.RunOnErrorAsync(ctx, ex, ct);
            throw;
        }

        ctx.LlmResponse = response;
        await pipeline.RunAfterLLMResponseAsync(ctx, ct);
        return response;
    }

    internal async IAsyncEnumerable<AevatarLLMToken> GenerateLLMStreamWithHooksAsync(
        string requestId,
        AevatarLLMRequest llmRequest,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
        {
            await foreach (var token in _host.LLMProvider.GenerateStreamAsync(llmRequest, ct).WithCancellation(ct))
            {
                yield return token;
            }

            yield break;
        }

        var safeRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        var ctx = CreateHookContext(safeRequestId, llmRequest: llmRequest);

        await pipeline.RunBeforeLLMRequestAsync(ctx, ct);

        // Defense in depth: re-apply tool visibility policy after hooks (only if tools are attached).
        if (llmRequest.Functions != null)
        {
            _host.AttachToolsToRequest(llmRequest);
        }

        IAsyncEnumerable<AevatarLLMToken> stream;
        try
        {
            stream = _host.LLMProvider.GenerateStreamAsync(llmRequest, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await pipeline.RunOnErrorAsync(ctx, ex, ct);
            throw;
        }

        var enumerator = stream.GetAsyncEnumerator(ct);
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
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await pipeline.RunOnErrorAsync(ctx, ex, ct);
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

    internal Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
        => GenerateLLMWithHooksAsync(request.RequestId, llmRequest, ct);

    internal async Task<ToolExecutionResult> ExecuteAllowedToolWithHooksAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
    {
        var pipeline = GetHookPipeline();
        if (!pipeline.HasHooks)
        {
            return await _host.ExecuteAllowedToolAsync(toolName, args, executionContext, llmRequest, ct);
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

        await pipeline.RunBeforeToolExecuteAsync(ctx, ct);

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

        var result = await _host.ExecuteAllowedToolAsync(toolName, args, executionContext, llmRequest, ct);
        ctx.ToolResult = result;
        CopyToolMetadata(executionContext, ctx);

        await pipeline.RunAfterToolExecuteAsync(ctx, ct);
        return result;
    }

    private static void CopyToolMetadata(ToolExecutionContext executionContext, AevatarAgentHookContext context)
    {
        if (executionContext.Metadata.Count == 0)
            return;

        foreach (var (key, value) in executionContext.Metadata)
        {
            if (value == null) continue;
            context.Metadata[key] = value;
        }
    }
}

