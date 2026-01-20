using System.Text;
using Tui.Core.Primitives;
using Tui.Core.Render;

namespace Tui.Core.Tests;

public sealed class ScreenBufferTests
{
    [Fact]
    public void SetAndGet_ShouldRoundTrip()
    {
        var buffer = new ScreenBuffer(3, 2);
        var cell = new Cell(new Rune('Z'), CellStyle.Default);

        var ok = buffer.Set(1, 1, cell);

        Assert.True(ok);
        Assert.Equal(cell, buffer.Get(1, 1));
    }

    [Fact]
    public void FillRect_ShouldClipToBounds()
    {
        var buffer = new ScreenBuffer(4, 3);
        var cell = new Cell(new Rune('X'), CellStyle.Default);

        buffer.FillRect(new Rect(-1, -1, 3, 3), cell);

        Assert.Equal(cell, buffer.Get(0, 0));
        Assert.Equal(cell, buffer.Get(1, 1));
        Assert.Equal(Cell.Empty, buffer.Get(3, 2));
    }
}
