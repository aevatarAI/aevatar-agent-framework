using Tui.Declarative.Elements;
using Tui.Widgets.Containers;
using Tui.Widgets.Core;

namespace Tui.Declarative.Reconciliation;

public sealed class Reconciler
{
    public WidgetNode Reconcile(Element element, WidgetNode? previous)
    {
        if (previous is null)
            return CreateNode(element);

        if (!IsSameIdentity(element, previous))
        {
            Unmount(previous);
            return CreateNode(element);
        }

        element.Configure(previous.Widget);
        ApplyChildren(previous, element.Children);
        return previous;
    }

    private WidgetNode CreateNode(Element element)
    {
        var widget = element.CreateWidget();
        element.Configure(widget);
        var node = new WidgetNode(element.Key, widget);

        if (widget is IWidgetLifecycle lifecycle)
            lifecycle.OnMounted();

        ApplyChildren(node, element.Children);
        return node;
    }

    private void ApplyChildren(WidgetNode node, IReadOnlyList<ElementChild> children)
    {
        if (children.Count == 0)
        {
            node.SetChildren(Array.Empty<WidgetNode>());
            if (node.Widget is IWidgetContainer container)
                container.SetChildren(Array.Empty<WidgetSlot>());
            return;
        }

        var previousByKey = node.Children.ToDictionary(child => child.Key, child => child);
        var newNodes = new List<WidgetNode>(children.Count);
        var slots = new List<WidgetSlot>(children.Count);
        var activeKeys = new HashSet<string>();

        foreach (var child in children)
        {
            var key = child.Element.Key;
            previousByKey.TryGetValue(key, out var previousChild);
            var nextChild = Reconcile(child.Element, previousChild);
            newNodes.Add(nextChild);
            slots.Add(new WidgetSlot(nextChild.Widget, child.Layout));
            activeKeys.Add(key);
        }

        foreach (var oldChild in node.Children)
        {
            if (!activeKeys.Contains(oldChild.Key))
                Unmount(oldChild);
        }

        node.SetChildren(newNodes);
        if (node.Widget is IWidgetContainer containerWidget)
            containerWidget.SetChildren(slots);
    }

    private static bool IsSameIdentity(Element element, WidgetNode node)
        => element.Key == node.Key && element.WidgetType == node.Widget.GetType();

    private static void Unmount(WidgetNode node)
    {
        foreach (var child in node.Children)
            Unmount(child);

        if (node.Widget is IWidgetLifecycle lifecycle)
            lifecycle.OnUnmounted();
    }
}
