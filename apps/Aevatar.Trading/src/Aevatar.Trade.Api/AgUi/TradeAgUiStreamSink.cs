using System;
using Aevatar.Agents.AGUI;
using Aevatar.Trade.AgUi;

namespace Aevatar.Trade.Api.AgUi;

internal sealed class TradeAgUiStreamSink : ITradeAgUiStreamSink
{
    private readonly TradeAgUiHub _hub;

    public TradeAgUiStreamSink(TradeAgUiHub hub)
    {
        _hub = hub;
    }

    public Task StartMessageAsync(string messageId, string role, string? name = null, CancellationToken ct = default)
    {
        _hub.Events.Publish(new TextMessageStartEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId,
            Role = role
        });
        _hub.SetMessage(messageId, role, "", name);
        return Task.CompletedTask;
    }

    public Task AppendDeltaAsync(string messageId, string delta, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(delta))
            return Task.CompletedTask;

        _hub.Events.Publish(new TextMessageContentEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId,
            Delta = delta
        });
        _hub.AppendToMessage(messageId, "assistant", delta);
        return Task.CompletedTask;
    }

    public Task EndMessageAsync(string messageId, CancellationToken ct = default)
    {
        _hub.Events.Publish(new TextMessageEndEvent
        {
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MessageId = messageId
        });
        return Task.CompletedTask;
    }
}

