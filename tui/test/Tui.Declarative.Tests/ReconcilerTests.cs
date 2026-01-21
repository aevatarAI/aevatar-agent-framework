using Tui.Declarative.Elements;
using Tui.Declarative.Reconciliation;
using Tui.Core.Layout;
using Tui.Core.Primitives;
using Tui.Core.Render;
using Tui.Widgets.Controls;
using Tui.Widgets.Core;
using Tui.Widgets.Containers;

namespace Tui.Declarative.Tests;

public sealed class ReconcilerTests
{
    [Fact]
    public void Reconcile_ShouldReuseWidgetByKeyAndType()
    {
        var reconciler = new Reconciler();
        var first = new LeafElement<TextWidget>(
            "title",
            () => new TextWidget("A"),
            widget => widget.Text = "A");
        var node = reconciler.Reconcile(first, null);

        var second = new LeafElement<TextWidget>(
            "title",
            () => new TextWidget("B"),
            widget => widget.Text = "B");
        var next = reconciler.Reconcile(second, node);

        Assert.Same(node, next);
        Assert.Same(node.Widget, next.Widget);
        Assert.Equal("B", ((TextWidget)next.Widget).Text);
    }

    [Fact]
    public void Reconcile_ShouldUnmountWhenIdentityChanges()
    {
        var reconciler = new Reconciler();
        var lifecycle = new LifecycleWidget();
        var first = new LeafElement<LifecycleWidget>("a", () => lifecycle);
        var node = reconciler.Reconcile(first, null);

        var second = new LeafElement<LifecycleWidget>("b", () => new LifecycleWidget());
        reconciler.Reconcile(second, node);

        Assert.Equal(1, lifecycle.MountedCount);
        Assert.Equal(1, lifecycle.UnmountedCount);
    }

    [Fact]
    public void Reconcile_ShouldBindContainerChildren()
    {
        var reconciler = new Reconciler();
        var element = new ContainerElement<RowWidget>(
            "row",
            () => new RowWidget(),
            new[]
            {
                new ElementChild(
                    new LeafElement<TextWidget>("a", () => new TextWidget("A")),
                    new FlexItem(basis: 1, grow: 0, shrink: 0)),
                new ElementChild(
                    new LeafElement<TextWidget>("b", () => new TextWidget("B")),
                    new FlexItem(basis: 1, grow: 0, shrink: 0))
            });

        var node = reconciler.Reconcile(element, null);
        var buffer = new ScreenBuffer(4, 1);
        node.Widget.Render(new WidgetRenderContext(buffer, new Rect(0, 0, 4, 1), false));

        Assert.Equal("AB  ", DumpLine(buffer));
    }

    private sealed class LifecycleWidget : IWidget, IWidgetLifecycle
    {
        public int MountedCount { get; private set; }
        public int UnmountedCount { get; private set; }

        public void Render(WidgetRenderContext context)
        {
        }

        public void OnMounted()
            => MountedCount++;

        public void OnUnmounted()
            => UnmountedCount++;
    }

    private static string DumpLine(ScreenBuffer buffer)
    {
        var row = buffer.GetRowSpan(0);
        var sb = new System.Text.StringBuilder(buffer.Width);
        foreach (var cell in row)
            sb.Append(cell.Rune.ToString());
        return sb.ToString();
    }
}
