using Tui.Core.Primitives;

namespace Tui.Core.Render;

public sealed class ScreenBuffer
{
    private Cell[] _cells;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Cell DefaultCell { get; }

    public ScreenBuffer(int width, int height, Cell? defaultCell = null)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        DefaultCell = defaultCell ?? Cell.Empty;
        _cells = new Cell[width * height];
        Clear();
    }

    public Cell Get(int x, int y)
    {
        if (!IsInside(x, y))
            return DefaultCell;

        return _cells[y * Width + x];
    }

    public bool Set(int x, int y, Cell cell)
    {
        if (!IsInside(x, y))
            return false;

        _cells[y * Width + x] = cell;
        return true;
    }

    public void Clear()
        => Array.Fill(_cells, DefaultCell);

    public void FillRect(Rect rect, Cell cell)
    {
        var clipped = rect.Intersect(new Rect(0, 0, Width, Height));
        if (clipped.Width == 0 || clipped.Height == 0)
            return;

        for (var y = clipped.Y; y < clipped.Bottom; y++)
        {
            var rowIndex = y * Width;
            for (var x = clipped.X; x < clipped.Right; x++)
                _cells[rowIndex + x] = cell;
        }
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        var next = new Cell[width * height];
        Array.Fill(next, DefaultCell);

        var copyWidth = Math.Min(width, Width);
        var copyHeight = Math.Min(height, Height);
        for (var y = 0; y < copyHeight; y++)
        {
            Array.Copy(_cells, y * Width, next, y * width, copyWidth);
        }

        Width = width;
        Height = height;
        _cells = next;
    }

    public ReadOnlySpan<Cell> GetRowSpan(int y)
    {
        if (y < 0 || y >= Height)
            return ReadOnlySpan<Cell>.Empty;

        return new ReadOnlySpan<Cell>(_cells, y * Width, Width);
    }

    public Span<Cell> GetRowSpanMutable(int y)
    {
        if (y < 0 || y >= Height)
            return Span<Cell>.Empty;

        return new Span<Cell>(_cells, y * Width, Width);
    }

    public void CopyFrom(ScreenBuffer source)
    {
        if (source.Width != Width || source.Height != Height)
            throw new InvalidOperationException("Buffer size mismatch.");

        source._cells.CopyTo(_cells, 0);
    }

    private bool IsInside(int x, int y)
        => x >= 0 && x < Width && y >= 0 && y < Height;
}
