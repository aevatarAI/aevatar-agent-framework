using Tui.Core.Input;

namespace Tui.Core.Tests;

public sealed class InputParserTests
{
    [Fact]
    public void Feed_ShouldParseArrowAcrossChunks()
    {
        var parser = new InputParser();

        Assert.Empty(parser.Feed(new byte[] { 0x1B }));
        Assert.Empty(parser.Feed(new byte[] { (byte)'[' }));
        var events = parser.Feed(new byte[] { (byte)'A' });

        var key = Assert.Single(events) as KeyEvent;
        Assert.NotNull(key);
        Assert.Equal(Key.Up, key!.Key);
    }

    [Fact]
    public void Flush_ShouldEmitEscapeWhenPending()
    {
        var parser = new InputParser();
        Assert.Empty(parser.Feed(new byte[] { 0x1B }));

        var flushed = parser.Flush();
        var key = Assert.Single(flushed) as KeyEvent;

        Assert.NotNull(key);
        Assert.Equal(Key.Escape, key!.Key);
    }

    [Fact]
    public void Feed_ShouldParseTextAndBackspace()
    {
        var parser = new InputParser();
        var events = parser.Feed(new byte[] { (byte)'a', (byte)'b', 0x7F });

        Assert.Equal(3, events.Count);
        Assert.IsType<TextInputEvent>(events[0]);
        Assert.IsType<TextInputEvent>(events[1]);
        Assert.IsType<KeyEvent>(events[2]);
    }
}
