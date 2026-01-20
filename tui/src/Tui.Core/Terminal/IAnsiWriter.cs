using Tui.Core.Render;

namespace Tui.Core.Terminal;

public interface IAnsiWriter : IAsyncDisposable
{
    ValueTask WriteRunsAsync(IReadOnlyList<RenderRun> runs, RenderStats stats, CancellationToken ct = default);
    ValueTask SetAltScreenAsync(bool enable, CancellationToken ct = default);
    ValueTask ShowCursorAsync(bool visible, CancellationToken ct = default);
    ValueTask MoveCursorAsync(int x, int y, CancellationToken ct = default);
    ValueTask ClearAsync(CancellationToken ct = default);
}
