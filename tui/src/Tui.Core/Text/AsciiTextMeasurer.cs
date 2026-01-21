namespace Tui.Core.Text;

public sealed class AsciiTextMeasurer : ITextMeasurer
{
    public int Measure(ReadOnlySpan<char> text)
        => text.Length;
}
