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

public sealed class SessionAgUiStream : IAgUiEventStream, IAsyncDisposable
{
    private readonly string _sessionId;
    private readonly ILogger _logger;
    private readonly Channel<AgUiEvent> _channel;
    private readonly ConcurrentBag<Task<IMessageStreamSubscription>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, byte> _streamingMessages = new(StringComparer.Ordinal);
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

        // ============================================================
        //  ChatStreamChunkEvent → 实时 streaming
        // ============================================================
        Track(stream.SubscribeAsync<ChatStreamChunkEvent>(evt =>
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.RequestId))
                return Task.CompletedTask;

            var messageId = BuildMessageId(evt.RequestId);
            _streamingMessages.TryAdd(messageId, 0);

            Publish(new TextMessageContentEvent
            {
                Timestamp = Now(),
                MessageId = messageId,
                Delta = evt.Content ?? string.Empty
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

        // ============================================================
        //  ChatResponseEvent → 结束事件 (非流式时发送内容)
        // ============================================================
        Track(stream.SubscribeAsync<ChatResponseEvent>(evt =>
        {
            if (evt == null || string.IsNullOrWhiteSpace(evt.RequestId))
                return Task.CompletedTask;

            var messageId = BuildMessageId(evt.RequestId);
            var content = evt.Content ?? string.Empty;
            var hadChunks = _streamingMessages.TryRemove(messageId, out _);

            // 只有在没收到 chunks 时才发送完整内容（非流式回退）
            if (!hadChunks && content.Length > 0)
            {
                Publish(new TextMessageContentEvent
                {
                    Timestamp = Now(),
                    MessageId = messageId,
                    Delta = content
                });
            }

            Publish(new TextMessageEndEvent
            {
                Timestamp = Now(),
                MessageId = messageId
            });

            Publish(new RunFinishedEvent
            {
                Timestamp = Now(),
                ThreadId = _sessionId,
                RunId = evt.RequestId
            });
            return Task.CompletedTask;
        }, null, CancellationToken.None));

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

        // ExecutionTraceEvent
        Track(stream.SubscribeAsync<ExecutionTraceEvent>(evt =>
        {
            var agui = AgUiTraceProjector.Map(evt, new AgUiTraceProjectorOptions
            {
                ResolveThreadId = _ => _sessionId
            });
            foreach (var e in agui)
                Publish(e);
            return Task.CompletedTask;
        }, null, CancellationToken.None));
    }

    private string BuildMessageId(string requestId)
        => $"msg:{_sessionId}:assistant:{requestId}";

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
        _streamingMessages.Clear();

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
