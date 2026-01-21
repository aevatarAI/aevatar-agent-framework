using Tui.Core.Primitives;
using Tui.Core.Render;

namespace Tui.Widgets.Core;

public readonly struct WidgetRenderContext
{
    public ScreenBuffer Buffer { get; }
    public Rect Bounds { get; }
    public bool IsFocused { get; }

    public WidgetRenderContext(ScreenBuffer buffer, Rect bounds, bool isFocused)
    {
        Buffer = buffer;
        Bounds = bounds;
        IsFocused = isFocused;
    }
}
