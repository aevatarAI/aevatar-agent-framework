using System.Text;
using Tui.Core.Primitives;
using Tui.Core.Render;

namespace Tui.Core.Terminal;

public sealed class TerminalEmulator
{
    public ScreenBuffer Buffer { get; }

    public TerminalEmulator(int width, int height, Cell? defaultCell = null)
    {
        Buffer = new ScreenBuffer(width, height, defaultCell);
    }

    // ------------------------------------------------------------
    // 以 Cell 为唯一真相源：这里只做应用 diff run
    // ------------------------------------------------------------
    public void ApplyRuns(IReadOnlyList<RenderRun> runs)
    {
        foreach (var run in runs)
        {
            var x = run.Column;
            var y = run.Row;
            if (y < 0 || y >= Buffer.Height)
                continue;

            var span = run.Runes.Span;
            for (var i = 0; i < span.Length; i++)
            {
                if (x + i >= Buffer.Width)
                    break;
                Buffer.Set(x + i, y, new Cell(span[i], run.Style));
            }
        }
    }

    public string[] DumpLines()
    {
        var lines = new string[Buffer.Height];
        for (var y = 0; y < Buffer.Height; y++)
        {
            var row = Buffer.GetRowSpan(y);
            var sb = new StringBuilder(Buffer.Width);
            foreach (var cell in row)
                sb.Append(cell.Rune.ToString());
            lines[y] = sb.ToString();
        }

        return lines;
    }
}
