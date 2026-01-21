namespace Tui.Core.Primitives;

public readonly struct Rect : IEquatable<Rect>
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public int Right => X + Width;
    public int Bottom => Y + Height;

    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public bool Contains(int x, int y)
        => x >= X && x < Right && y >= Y && y < Bottom;

    public Rect Intersect(Rect other)
    {
        var nx = Math.Max(X, other.X);
        var ny = Math.Max(Y, other.Y);
        var nr = Math.Min(Right, other.Right);
        var nb = Math.Min(Bottom, other.Bottom);
        var nwidth = Math.Max(0, nr - nx);
        var nheight = Math.Max(0, nb - ny);
        return new Rect(nx, ny, nwidth, nheight);
    }

    public bool Equals(Rect other)
        => X == other.X
           && Y == other.Y
           && Width == other.Width
           && Height == other.Height;

    public override bool Equals(object? obj)
        => obj is Rect other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(Rect left, Rect right) => left.Equals(right);
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);
}
