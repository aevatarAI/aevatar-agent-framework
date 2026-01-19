using System.Text;
using Tui.Core.Input;
using Tui.Core.Primitives;
using Tui.Core.Render;
using Tui.Widgets.Controls;
using Tui.Widgets.Core;
using Tui.Widgets.Input;

namespace Tui.Widgets.Tests;

public sealed class WidgetSnapshotTests
{
    [Fact]
    public void TextWidget_ShouldRenderSingleLine()
    {
        var buffer = new ScreenBuffer(10, 1);
        var widget = new TextWidget("Hi");

        widget.Render(new WidgetRenderContext(buffer, new Rect(0, 0, 10, 1), false));

        Assert.Equal("Hi        ", DumpLine(buffer, 0));
    }

    [Fact]
    public void BorderAndTextInput_ShouldRenderSnapshot()
    {
        var buffer = new ScreenBuffer(8, 3);
        var border = new BorderWidget("T");
        var input = new TextInputWidget();
        input.SetFocus(true);
        input.HandleInput(new TextInputEvent("abc"));

        border.Render(new WidgetRenderContext(buffer, new Rect(0, 0, 8, 3), false));
        input.Render(new WidgetRenderContext(buffer, new Rect(1, 1, 6, 1), true));

        Assert.Equal("+T-----+", DumpLine(buffer, 0));
        Assert.Equal("|abc   |", DumpLine(buffer, 1));
        Assert.Equal("+------+", DumpLine(buffer, 2));
    }

    private static string DumpLine(ScreenBuffer buffer, int y)
    {
        var row = buffer.GetRowSpan(y);
        var sb = new StringBuilder(buffer.Width);
        foreach (var cell in row)
            sb.Append(cell.Rune.ToString());
        return sb.ToString();
    }
}
