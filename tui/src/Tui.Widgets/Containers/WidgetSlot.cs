using Tui.Core.Layout;
using Tui.Widgets.Core;

namespace Tui.Widgets.Containers;

public readonly struct WidgetSlot
{
    public IWidget Widget { get; }
    public FlexItem Layout { get; }

    public WidgetSlot(IWidget widget, FlexItem layout)
    {
        Widget = widget;
        Layout = layout;
    }
}
