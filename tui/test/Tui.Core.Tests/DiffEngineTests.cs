using System.Text;
using Tui.Core.Primitives;
using Tui.Core.Render;
using Tui.Core.Terminal;

namespace Tui.Core.Tests;

public sealed class DiffEngineTests
{
    [Fact]
    public void ComputeRuns_ShouldApplyToEmulator()
    {
        var prev = new ScreenBuffer(5, 2);
        var next = new ScreenBuffer(5, 2);

        var styleA = new CellStyle(TuiColor.Default, TuiColor.Default, TextAttribute.None);
        var styleB = new CellStyle(TuiColor.FromAnsi16(2), TuiColor.Default, TextAttribute.Bold);

        next.Set(0, 0, new Cell(new Rune('A'), styleA));
        next.Set(1, 0, new Cell(new Rune('B'), styleA));
        next.Set(2, 0, new Cell(new Rune('C'), styleB));
        next.Set(0, 1, new Cell(new Rune('X'), styleA));

        var diff = DiffEngine.ComputeRuns(prev, next);

        var emulator = new TerminalEmulator(5, 2);
        emulator.ApplyRuns(diff.Runs);

        Assert.Equal(next.Get(0, 0), emulator.Buffer.Get(0, 0));
        Assert.Equal(next.Get(1, 0), emulator.Buffer.Get(1, 0));
        Assert.Equal(next.Get(2, 0), emulator.Buffer.Get(2, 0));
        Assert.Equal(next.Get(0, 1), emulator.Buffer.Get(0, 1));
        Assert.Equal(4, diff.Stats.ChangedCells);
        Assert.True(diff.Runs.Count >= 2);
    }
}
