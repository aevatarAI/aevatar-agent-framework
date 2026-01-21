using Tui.Widgets.Core;

namespace Tui.Declarative.Reconciliation;

public sealed class WidgetNode
{
    public string Key { get; }
    public IWidget Widget { get; }
    public IReadOnlyList<WidgetNode> Children { get; private set; }

    public WidgetNode(string key, IWidget widget)
    {
        Key = key;
        Widget = widget;
        Children = Array.Empty<WidgetNode>();
    }

    public void SetChildren(IReadOnlyList<WidgetNode> children)
        => Children = children;
}
