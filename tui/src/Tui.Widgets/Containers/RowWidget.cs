using Tui.Core.Layout;
using Tui.Core.Primitives;
using Tui.Widgets.Core;
using Tui.Widgets.Input;

namespace Tui.Widgets.Containers;

public sealed class RowWidget : IWidget, IWidgetContainer
{
    private readonly List<WidgetSlot> _children = new();

    public RowWidget(IEnumerable<WidgetSlot> children)
    {
        _children.AddRange(children);
    }

    public RowWidget()
    {
    }

    public void Add(WidgetSlot slot)
        => _children.Add(slot);

    public void SetChildren(IReadOnlyList<WidgetSlot> slots)
    {
        _children.Clear();
        _children.AddRange(slots);
    }

    public void Render(WidgetRenderContext context)
    {
        if (_children.Count == 0)
            return;

        var layouts = new List<FlexItem>(_children.Count);
        foreach (var child in _children)
            layouts.Add(child.Layout);

        var rects = FlexLayout.Layout(context.Bounds, FlexDirection.Row, layouts);
        for (var i = 0; i < _children.Count; i++)
        {
            var widget = _children[i].Widget;
            var focused = widget is IFocusableWidget focusable && focusable.IsFocused;
            widget.Render(new WidgetRenderContext(context.Buffer, rects[i], focused));
        }
    }
}
