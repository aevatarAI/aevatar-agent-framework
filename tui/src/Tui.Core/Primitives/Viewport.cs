namespace Tui.Core.Primitives;

public readonly struct Viewport : IEquatable<Viewport>
{
    public int Width { get; }
    public int Height { get; }

    public Viewport(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public bool Equals(Viewport other)
        => Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj)
        => obj is Viewport other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Width, Height);

    public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);
    public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);
}
