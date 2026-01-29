using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Core.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using AgUiToolCallStartEvent = Aevatar.Agents.AGUI.ToolCallStartEvent;
using AgUiToolCallEndEvent = Aevatar.Agents.AGUI.ToolCallEndEvent;
using CoreToolCallStartEvent = Aevatar.Agents.AI.Core.Messages.ToolCallStartEvent;
using CoreToolCallEndEvent = Aevatar.Agents.AI.Core.Messages.ToolCallEndEvent;

namespace Aevatar.Agents.Sessions.Runtime;

// ============================================================
//  SessionAgUiStream - 极简版
//
//  职责：
//  - 订阅 agent stream 的 Chat/Tool 事件
//  - 投影为 AG-UI 事件
//  - 通过 Channel 推送给 SSE
// ============================================================

public sealed class SessionAgUiStream : IAsyncDisposable
{
    private readonly string _sessionId;
    private readonly ILogger _logger;
    private readonly Channel<AgUiEvent> _channel;
    private readonly ConcurrentBag<Task<IMessageStreamSubscription>> _subscriptions = new();
    private int _disposed;

    public SessionAgUiStream(
        string sessionId,
        string agentId,
        IAgentMessageStreamResolver streamResolver,
        ILogger logger)
    {
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _channel = Channel.CreateUnbounded<AgUiEvent>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = false,
            AllowSynchronousContinuations = true
        });

        var stream = streamResolver.GetStream(agentId);
        SubscribeToAgentEvents(stream, agentId);
    }

    public async IAsyncEnumerable<AgUiEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogDebug("[SessionAgUiStream] SSE subscriber connected: {SessionId}", _sessionId);
        try
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
                yield return evt;
        }
        finally
        {
            _logger.LogDebug("[SessionAgUiStream] SSE subscriber disconnected: {SessionId}", _sessionId);
        }
    }

    public void Publish(AgUiEvent evt)
    {
        if (evt == null || Volatile.Read(ref _disposed) != 0)
            return;
        _channel.Writer.TryWrite(evt);
    }

    private void SubscribeToAgentEvents(IMessageStream stream, string agentId)
    {
        _logger.LogDebug("[SessionAgUiStream] Subscribing to agent {AgentId}", agentId);

        // Tool 事件
        Track(stream.SubscribeAsync<CoreToolCallStartEvent>(evt =>
        {
            Publish(new AgUiToolCallStartEvent
            {
                Timestamp = ToUnixMs(evt.Timestamp),
                MessageId = evt.MessageId ?? string.Empty,
                ToolCallId = evt.ToolCallId ?? string.Empty,
                ToolName = evt.ToolName ?? string.Empty
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        Track(stream.SubscribeAsync<CoreToolCallEndEvent>(evt =>
        {
            Publish(new AgUiToolCallEndEvent
            {
                Timestamp = ToUnixMs(evt.Timestamp),
                MessageId = evt.MessageId ?? string.Empty,
                ToolCallId = evt.ToolCallId ?? string.Empty
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        // ExecutionTraceEvent - 过滤掉内部 chat handler 事件
        Track(stream.SubscribeAsync<ExecutionTraceEvent>(evt =>
        {
            // 跳过内部 chat 相关的 handler 事件，这些不应暴露给 UI
            if (evt.Fields.TryGetValue(ExecutionTraceEventFields.HandlerName, out var handlerField))
            {
                var handlerName = handlerField.StringValue ?? string.Empty;
                if (handlerName.StartsWith("HandleChat", StringComparison.Ordinal))
                    return Task.CompletedTask;
            }

            var agui = AgUiTraceProjector.Map(evt, new AgUiTraceProjectorOptions
            {
                ResolveThreadId = _ => _sessionId
            });
            foreach (var e in agui)
                Publish(e);
            return Task.CompletedTask;
        }, null, CancellationToken.None));
    }

    private static long Now()
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long ToUnixMs(Timestamp? ts)
        => ts == null ? Now() : new DateTimeOffset(ts.ToDateTime().ToUniversalTime()).ToUnixTimeMilliseconds();

    private void Track(Task<IMessageStreamSubscription> task)
        => _subscriptions.Add(task);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();

        foreach (var task in _subscriptions)
        {
            try
            {
                var sub = await task.ConfigureAwait(false);
                await sub.UnsubscribeAsync().ConfigureAwait(false);
                await sub.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
