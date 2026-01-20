using System.Text;
using Tui.Core.Primitives;

namespace Tui.Core.Render;

public readonly struct DiffResult
{
    public IReadOnlyList<RenderRun> Runs { get; }
    public RenderStats Stats { get; }

    public DiffResult(IReadOnlyList<RenderRun> runs, RenderStats stats)
    {
        Runs = runs;
        Stats = stats;
    }
}

public static class DiffEngine
{
    public static DiffResult ComputeRuns(ScreenBuffer previous, ScreenBuffer next)
    {
        if (previous.Width != next.Width || previous.Height != next.Height)
            throw new InvalidOperationException("Buffer size mismatch.");

        var runs = new List<RenderRun>();
        var stats = new RenderStats();
        var changedCells = 0;

        for (var y = 0; y < next.Height; y++)
        {
            var prevRow = previous.GetRowSpan(y);
            var nextRow = next.GetRowSpan(y);
            var x = 0;

            while (x < next.Width)
            {
                if (nextRow[x].Equals(prevRow[x]))
                {
                    x++;
                    continue;
                }

                var style = nextRow[x].Style;
                var runes = new List<Rune>();
                var start = x;

                while (x < next.Width)
                {
                    var nextCell = nextRow[x];
                    if (nextCell.Equals(prevRow[x]))
                        break;
                    if (!nextCell.Style.Equals(style))
                        break;

                    runes.Add(nextCell.Rune);
                    changedCells++;
                    x++;
                }

                if (runes.Count > 0)
                    runs.Add(new RenderRun(y, start, style, runes.ToArray()));
            }
        }

        stats.AddDiff(changedCells, runs.Count);
        return new DiffResult(runs, stats);
    }
}
