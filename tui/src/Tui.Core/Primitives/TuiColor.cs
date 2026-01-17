namespace Tui.Core.Primitives;

public enum TuiColorKind
{
    Default = 0,
    Ansi16 = 1,
    Ansi256 = 2,
    Rgb = 3
}

public readonly struct TuiColor : IEquatable<TuiColor>
{
    public static TuiColor Default => new(TuiColorKind.Default, 0, 0, 0, 0);

    public TuiColorKind Kind { get; }
    public byte Index { get; }
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    private TuiColor(TuiColorKind kind, byte index, byte r, byte g, byte b)
    {
        Kind = kind;
        Index = index;
        R = r;
        G = g;
        B = b;
    }

    public static TuiColor FromAnsi16(byte index)
        => new(TuiColorKind.Ansi16, index, 0, 0, 0);

    public static TuiColor FromAnsi256(byte index)
        => new(TuiColorKind.Ansi256, index, 0, 0, 0);

    public static TuiColor FromRgb(byte r, byte g, byte b)
        => new(TuiColorKind.Rgb, 0, r, g, b);

    public bool Equals(TuiColor other)
        => Kind == other.Kind
           && Index == other.Index
           && R == other.R
           && G == other.G
           && B == other.B;

    public override bool Equals(object? obj)
        => obj is TuiColor other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine((int)Kind, Index, R, G, B);

    public static bool operator ==(TuiColor left, TuiColor right) => left.Equals(right);
    public static bool operator !=(TuiColor left, TuiColor right) => !left.Equals(right);
}
