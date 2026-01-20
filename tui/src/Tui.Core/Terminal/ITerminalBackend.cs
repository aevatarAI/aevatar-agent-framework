using Tui.Core.Primitives;

namespace Tui.Core.Terminal;

public interface ITerminalBackend : IAsyncDisposable
{
    Viewport Viewport { get; }
    event Action<Viewport>? Resized;

    ValueTask EnterAsync(CancellationToken ct = default);
    ValueTask ExitAsync(CancellationToken ct = default);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);
}
