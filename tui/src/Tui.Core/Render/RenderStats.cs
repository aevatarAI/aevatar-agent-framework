namespace Tui.Core.Render;

public sealed class RenderStats
{
    public int ChangedCells { get; private set; }
    public int Runs { get; private set; }
    public int CursorMoves { get; private set; }
    public int StyleSets { get; private set; }
    public int BytesWritten { get; private set; }

    public void Reset()
    {
        ChangedCells = 0;
        Runs = 0;
        CursorMoves = 0;
        StyleSets = 0;
        BytesWritten = 0;
    }

    public void AddDiff(int changedCells, int runs)
    {
        ChangedCells += changedCells;
        Runs += runs;
    }

    public void AddWriter(int cursorMoves, int styleSets, int bytesWritten)
    {
        CursorMoves += cursorMoves;
        StyleSets += styleSets;
        BytesWritten += bytesWritten;
    }
}
