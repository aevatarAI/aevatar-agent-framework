using Tui.Core.Input;
using Tui.Core.Primitives;
using Tui.Core.Terminal;

namespace Tui.Core.Render;

public readonly struct FrameContext
{
    public Viewport Viewport { get; }
    public IReadOnlyList<InputEvent> Events { get; }
    public ScreenBuffer Buffer { get; }
    public RenderStats Stats { get; }

    public FrameContext(Viewport viewport, IReadOnlyList<InputEvent> events, ScreenBuffer buffer, RenderStats stats)
    {
        Viewport = viewport;
        Events = events;
        Buffer = buffer;
        Stats = stats;
    }
}

public sealed class RenderLoop
{
    private readonly ITerminalBackend _backend;
    private readonly IInputParser _parser;
    private readonly IAnsiWriter _writer;
    private ScreenBuffer _previous;
    private ScreenBuffer _next;
    private readonly RenderStats _stats = new();
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _hasResize;

    public RenderLoop(ITerminalBackend backend, IInputParser parser, IAnsiWriter writer)
    {
        _backend = backend;
        _parser = parser;
        _writer = writer;
        var viewport = backend.Viewport;
        _previous = new ScreenBuffer(viewport.Width, viewport.Height);
        _next = new ScreenBuffer(viewport.Width, viewport.Height);
    }

    public async Task RunAsync(Func<FrameContext, ValueTask> render, CancellationToken ct = default)
    {
        await _backend.EnterAsync(ct);
        _backend.Resized += OnResized;
        await _writer.SetAltScreenAsync(true, ct);
        await _writer.ShowCursorAsync(false, ct);

        try
        {
            await RenderOnceAsync(render, Array.Empty<InputEvent>(), ct);

            var inputBuffer = new byte[4096];
            while (!ct.IsCancellationRequested)
            {
                var read = await _backend.ReadAsync(inputBuffer, ct);
                var events = read > 0
                    ? _parser.Feed(inputBuffer.AsSpan(0, read))
                    : Array.Empty<InputEvent>();

                var resizeEvent = CollectResizeEvent();
                if (resizeEvent is not null)
                    events = MergeEvents(events, resizeEvent);

                if (events.Count > 0)
                    await RenderOnceAsync(render, events, ct);
            }
        }
        finally
        {
            _backend.Resized -= OnResized;
            await _writer.ShowCursorAsync(true, ct);
            await _writer.SetAltScreenAsync(false, ct);
            await _backend.ExitAsync(ct);
        }
    }

    private async Task RenderOnceAsync(Func<FrameContext, ValueTask> render, IReadOnlyList<InputEvent> events, CancellationToken ct)
    {
        _stats.Reset();
        _next.Clear();

        var context = new FrameContext(new Viewport(_next.Width, _next.Height), events, _next, _stats);
        await render(context);

        var diff = DiffEngine.ComputeRuns(_previous, _next);
        _stats.AddDiff(diff.Stats.ChangedCells, diff.Stats.Runs);
        await _writer.WriteRunsAsync(diff.Runs, _stats, ct);
        _previous.CopyFrom(_next);
    }

    private void OnResized(Viewport viewport)
    {
        _pendingWidth = viewport.Width;
        _pendingHeight = viewport.Height;
        _hasResize = true;
    }

    private ResizeEvent? CollectResizeEvent()
    {
        if (!_hasResize)
            return null;

        _hasResize = false;
        var viewport = new Viewport(_pendingWidth, _pendingHeight);
        _previous = new ScreenBuffer(viewport.Width, viewport.Height);
        _next = new ScreenBuffer(viewport.Width, viewport.Height);
        return new ResizeEvent(viewport);
    }

    private static IReadOnlyList<InputEvent> MergeEvents(IReadOnlyList<InputEvent> events, ResizeEvent resizeEvent)
    {
        var merged = new List<InputEvent>(events.Count + 1);
        foreach (var evt in events)
            merged.Add(evt);
        merged.Add(resizeEvent);
        return merged;
    }
}
