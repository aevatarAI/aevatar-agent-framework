using System.Text;
using Tui.Core.Primitives;
using Tui.Widgets.Core;

namespace Tui.Widgets.Controls;

public sealed class TextWidget : IWidget
{
    public string Text { get; set; }
    public CellStyle Style { get; set; }

    public TextWidget(string text, CellStyle? style = null)
    {
        Text = text;
        Style = style ?? CellStyle.Default;
    }

    public void Render(WidgetRenderContext context)
    {
        if (context.Bounds.Width <= 0 || context.Bounds.Height <= 0)
            return;

        var text = Text ?? string.Empty;
        var max = Math.Min(text.Length, context.Bounds.Width);
        for (var i = 0; i < max; i++)
        {
            context.Buffer.Set(
                context.Bounds.X + i,
                context.Bounds.Y,
                new Cell(new Rune(text[i]), Style));
        }
    }
}
