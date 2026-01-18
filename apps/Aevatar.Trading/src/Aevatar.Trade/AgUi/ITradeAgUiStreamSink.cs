namespace Aevatar.Trade.AgUi;

public interface ITradeAgUiStreamSink
{
    Task StartMessageAsync(string messageId, string role, string? name = null, CancellationToken ct = default);
    Task AppendDeltaAsync(string messageId, string delta, CancellationToken ct = default);
    Task EndMessageAsync(string messageId, CancellationToken ct = default);
}

