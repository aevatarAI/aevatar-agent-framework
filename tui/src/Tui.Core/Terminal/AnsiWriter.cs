using System.Text;
using Tui.Core.Primitives;
using Tui.Core.Render;
using Tui.Core.Text;

namespace Tui.Core.Terminal;

public sealed class AnsiWriter : IAnsiWriter
{
    private readonly Stream _output;
    private readonly ITextMeasurer _textMeasurer;
    private CellStyle _currentStyle = CellStyle.Default;
    private int _cursorX = -1;
    private int _cursorY = -1;

    public AnsiWriter(Stream output, ITextMeasurer? textMeasurer = null)
    {
        _output = output;
        _textMeasurer = textMeasurer ?? new AsciiTextMeasurer();
    }

    public async ValueTask WriteRunsAsync(IReadOnlyList<RenderRun> runs, RenderStats stats, CancellationToken ct = default)
    {
        if (runs.Count == 0)
            return;

        var sb = new StringBuilder();
        var cursorMoves = 0;
        var styleSets = 0;

        foreach (var run in runs)
        {
            if (_cursorX != run.Column || _cursorY != run.Row)
            {
                AppendCursorMove(sb, run.Row, run.Column);
                _cursorX = run.Column;
                _cursorY = run.Row;
                cursorMoves++;
            }

            if (!_currentStyle.Equals(run.Style))
            {
                AppendStyle(sb, run.Style);
                _currentStyle = run.Style;
                styleSets++;
            }

            foreach (var rune in run.Runes.Span)
            {
                AppendRune(sb, rune);
                _cursorX += MeasureRune(rune);
            }
        }

        var payload = Encoding.UTF8.GetBytes(sb.ToString());
        await _output.WriteAsync(payload, ct);
        stats.AddWriter(cursorMoves, styleSets, payload.Length);
    }

    public async ValueTask SetAltScreenAsync(bool enable, CancellationToken ct = default)
    {
        var seq = enable ? "\x1b[?1049h" : "\x1b[?1049l";
        await WriteControlAsync(seq, ct);
    }

    public async ValueTask ShowCursorAsync(bool visible, CancellationToken ct = default)
    {
        var seq = visible ? "\x1b[?25h" : "\x1b[?25l";
        await WriteControlAsync(seq, ct);
    }

    public async ValueTask MoveCursorAsync(int x, int y, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        AppendCursorMove(sb, y, x);
        await WriteControlAsync(sb.ToString(), ct);
        _cursorX = x;
        _cursorY = y;
    }

    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        await WriteControlAsync("\x1b[2J", ct);
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private async ValueTask WriteControlAsync(string seq, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(seq);
        await _output.WriteAsync(payload, ct);
    }

    private static void AppendCursorMove(StringBuilder sb, int row, int column)
    {
        sb.Append("\x1b[");
        sb.Append(row + 1);
        sb.Append(';');
        sb.Append(column + 1);
        sb.Append('H');
    }

    private static void AppendStyle(StringBuilder sb, CellStyle style)
    {
        sb.Append("\x1b[0");
        AppendAttributes(sb, style.Attributes);
        AppendColor(sb, style.Foreground, isForeground: true);
        AppendColor(sb, style.Background, isForeground: false);
        sb.Append('m');
    }

    private static void AppendAttributes(StringBuilder sb, TextAttribute attributes)
    {
        if (attributes == TextAttribute.None)
            return;

        if (attributes.HasFlag(TextAttribute.Bold))
            sb.Append(";1");
        if (attributes.HasFlag(TextAttribute.Dim))
            sb.Append(";2");
        if (attributes.HasFlag(TextAttribute.Italic))
            sb.Append(";3");
        if (attributes.HasFlag(TextAttribute.Underline))
            sb.Append(";4");
        if (attributes.HasFlag(TextAttribute.Blink))
            sb.Append(";5");
        if (attributes.HasFlag(TextAttribute.Inverse))
            sb.Append(";7");
        if (attributes.HasFlag(TextAttribute.Hidden))
            sb.Append(";8");
        if (attributes.HasFlag(TextAttribute.Strike))
            sb.Append(";9");
    }

    private static void AppendColor(StringBuilder sb, TuiColor color, bool isForeground)
    {
        switch (color.Kind)
        {
            case TuiColorKind.Default:
                sb.Append(isForeground ? ";39" : ";49");
                return;
            case TuiColorKind.Ansi16:
                sb.Append(";");
                sb.Append(Ansi16Code(color.Index, isForeground));
                return;
            case TuiColorKind.Ansi256:
                sb.Append(isForeground ? ";38;5;" : ";48;5;");
                sb.Append(color.Index);
                return;
            case TuiColorKind.Rgb:
                sb.Append(isForeground ? ";38;2;" : ";48;2;");
                sb.Append(color.R);
                sb.Append(';');
                sb.Append(color.G);
                sb.Append(';');
                sb.Append(color.B);
                return;
            default:
                sb.Append(isForeground ? ";39" : ";49");
                return;
        }
    }

    private static int Ansi16Code(byte index, bool isForeground)
    {
        var baseCode = isForeground ? 30 : 40;
        if (index < 8)
            return baseCode + index;

        var brightBase = isForeground ? 90 : 100;
        return brightBase + (index - 8);
    }

    private static void AppendRune(StringBuilder sb, Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        var length = rune.EncodeToUtf16(buffer);
        sb.Append(buffer[..length]);
    }

    private int MeasureRune(Rune rune)
    {
        Span<char> buffer = stackalloc char[2];
        var length = rune.EncodeToUtf16(buffer);
        return _textMeasurer.Measure(buffer[..length]);
    }
}
