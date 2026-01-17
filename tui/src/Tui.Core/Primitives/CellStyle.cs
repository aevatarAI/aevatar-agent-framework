namespace Tui.Core.Primitives;

[Flags]
public enum TextAttribute
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blink = 1 << 4,
    Inverse = 1 << 5,
    Hidden = 1 << 6,
    Strike = 1 << 7
}

public readonly struct CellStyle : IEquatable<CellStyle>
{
    public static CellStyle Default => new(TuiColor.Default, TuiColor.Default, TextAttribute.None);

    public TuiColor Foreground { get; }
    public TuiColor Background { get; }
    public TextAttribute Attributes { get; }

    public CellStyle(TuiColor foreground, TuiColor background, TextAttribute attributes)
    {
        Foreground = foreground;
        Background = background;
        Attributes = attributes;
    }

    public bool Equals(CellStyle other)
        => Foreground == other.Foreground
           && Background == other.Background
           && Attributes == other.Attributes;

    public override bool Equals(object? obj)
        => obj is CellStyle other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Foreground, Background, (int)Attributes);

    public static bool operator ==(CellStyle left, CellStyle right) => left.Equals(right);
    public static bool operator !=(CellStyle left, CellStyle right) => !left.Equals(right);
}
