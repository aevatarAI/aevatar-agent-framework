using System.Text;
using Tui.Core.Primitives;

namespace Tui.Core.Render;

public readonly struct RenderRun
{
    public int Row { get; }
    public int Column { get; }
    public CellStyle Style { get; }
    public ReadOnlyMemory<Rune> Runes { get; }

    public int Length => Runes.Length;

    public RenderRun(int row, int column, CellStyle style, ReadOnlyMemory<Rune> runes)
    {
        Row = row;
        Column = column;
        Style = style;
        Runes = runes;
    }
}
