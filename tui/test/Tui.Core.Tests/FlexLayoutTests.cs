using Tui.Core.Layout;
using Tui.Core.Primitives;

namespace Tui.Core.Tests;

public sealed class FlexLayoutTests
{
    [Fact]
    public void Layout_Row_ShouldDistributeGrow()
    {
        var container = new Rect(0, 0, 10, 1);
        var items = new[]
        {
            new FlexItem(basis: 2, grow: 1),
            new FlexItem(basis: 2, grow: 1)
        };

        var rects = FlexLayout.Layout(container, FlexDirection.Row, items);

        Assert.Equal(5, rects[0].Width);
        Assert.Equal(5, rects[1].Width);
        Assert.Equal(0, rects[0].X);
        Assert.Equal(5, rects[1].X);
    }

    [Fact]
    public void Layout_Column_ShouldRespectMinOnShrink()
    {
        var container = new Rect(0, 0, 1, 3);
        var items = new[]
        {
            new FlexItem(basis: 2, min: 1, shrink: 1),
            new FlexItem(basis: 2, min: 1, shrink: 1)
        };

        var rects = FlexLayout.Layout(container, FlexDirection.Column, items);
        var total = rects[0].Height + rects[1].Height;

        Assert.Equal(3, total);
        Assert.True(rects[0].Height >= 1);
        Assert.True(rects[1].Height >= 1);
    }
}
