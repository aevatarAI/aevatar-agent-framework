using System.Text;
using Tui.Core.Primitives;
using Tui.Core.Render;
using Tui.Core.Terminal;

namespace Tui.Core.Tests;

public sealed class AnsiWriterTests
{
    [Fact]
    public async Task WriteRuns_ShouldEmitCursorMoveAndText()
    {
        await using var stream = new MemoryStream();
        await using var writer = new AnsiWriter(stream);

        var runes = new[] { new Rune('A') };
        var run = new RenderRun(0, 0, CellStyle.Default, runes);
        var stats = new RenderStats();

        await writer.WriteRunsAsync(new[] { run }, stats);

        var payload = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Equal("\x1b[1;1HA", payload);
        Assert.Equal(1, stats.CursorMoves);
        Assert.Equal(0, stats.StyleSets);
    }
}
