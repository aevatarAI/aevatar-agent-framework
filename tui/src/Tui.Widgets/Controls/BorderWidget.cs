using System.Text;
using Tui.Core.Primitives;
using Tui.Widgets.Core;

namespace Tui.Widgets.Controls;

public sealed class BorderWidget : IWidget
{
    public string? Title { get; set; }
    public CellStyle Style { get; set; }

    public BorderWidget(string? title = null, CellStyle? style = null)
    {
        Title = title;
        Style = style ?? CellStyle.Default;
    }

    public void Render(WidgetRenderContext context)
    {
        var bounds = context.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        DrawBorders(context.Buffer, bounds);

        if (!string.IsNullOrEmpty(Title) && bounds.Width > 2)
        {
            var title = Title!;
            var max = Math.Min(title.Length, bounds.Width - 2);
            for (var i = 0; i < max; i++)
            {
                context.Buffer.Set(
                    bounds.X + 1 + i,
                    bounds.Y,
                    new Cell(new Rune(title[i]), Style));
            }
        }
    }

    private void DrawBorders(Tui.Core.Render.ScreenBuffer buffer, Rect bounds)
    {
        var left = bounds.X;
        var right = bounds.X + bounds.Width - 1;
        var top = bounds.Y;
        var bottom = bounds.Y + bounds.Height - 1;

        var corner = new Cell(new Rune('+'), Style);
        var horizontal = new Cell(new Rune('-'), Style);
        var vertical = new Cell(new Rune('|'), Style);

        buffer.Set(left, top, corner);
        buffer.Set(right, top, corner);
        buffer.Set(left, bottom, corner);
        buffer.Set(right, bottom, corner);

        for (var x = left + 1; x < right; x++)
        {
            buffer.Set(x, top, horizontal);
            buffer.Set(x, bottom, horizontal);
        }

        for (var y = top + 1; y < bottom; y++)
        {
            buffer.Set(left, y, vertical);
            buffer.Set(right, y, vertical);
        }
    }
}
