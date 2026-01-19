using System.Text;

namespace Tui.Core.Primitives;

public readonly struct Cell : IEquatable<Cell>
{
    public static Cell Empty => new(new Rune(' '), CellStyle.Default);

    public Rune Rune { get; }
    public CellStyle Style { get; }

    public Cell(Rune rune, CellStyle style)
    {
        Rune = rune;
        Style = style;
    }

    public bool Equals(Cell other)
        => Rune.Equals(other.Rune) && Style.Equals(other.Style);

    public override bool Equals(object? obj)
        => obj is Cell other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Rune, Style);

    public static bool operator ==(Cell left, Cell right) => left.Equals(right);
    public static bool operator !=(Cell left, Cell right) => !left.Equals(right);
}
