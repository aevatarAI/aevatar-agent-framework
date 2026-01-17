namespace Tui.Core.Text;

public interface ITextMeasurer
{
    int Measure(ReadOnlySpan<char> text);
}
