using System.Runtime.Versioning;
using System.Text;
using Tui.Core.Input;
using Tui.Core.Layout;
using Tui.Core.Primitives;
using Tui.Core.Render;
using Tui.Core.Terminal;
using Tui.Widgets.Controls;
using Tui.Widgets.Input;

if (!IsPosix())
{
    Console.WriteLine("当前示例仅支持 macOS/Linux。");
    return;
}

using var cts = new CancellationTokenSource();
#pragma warning disable CA1416
var backend = new PosixTerminalBackend();
#pragma warning restore CA1416
await using var writer = new AnsiWriter(Console.OpenStandardOutput());
var parser = new InputParser();
var loop = new RenderLoop(backend, parser, writer);

var lastEvent = "none";
var title = new TextWidget("Tui.Samples - Widgets Demo (ESC/Ctrl+C to exit)", new CellStyle(TuiColor.FromAnsi16(2), TuiColor.Default, TextAttribute.Bold));
var footer = new TextWidget(string.Empty);
var border = new BorderWidget("Input");
var input = new TextInputWidget();
var focus = new FocusManager();
focus.Register(input);

await loop.RunAsync(context =>
{
    foreach (var evt in context.Events)
    {
        switch (evt)
        {
            case KeyEvent key when key.Key == Key.CtrlC || key.Key == Key.Escape:
                cts.Cancel();
                break;
            case KeyEvent key:
                lastEvent = $"Key: {key.Key}";
                focus.HandleInput(evt);
                break;
            case TextInputEvent text:
                lastEvent = $"Text: {text.Text}";
                focus.HandleInput(evt);
                break;
            case ResizeEvent resize:
                lastEvent = $"Resize: {resize.Viewport.Width}x{resize.Viewport.Height}";
                break;
        }
    }

    footer.Text = $"Last: {lastEvent}";
    RenderFrame(context.Buffer, title, border, input, footer);
    return ValueTask.CompletedTask;
}, cts.Token);

static void RenderFrame(ScreenBuffer buffer, TextWidget title, BorderWidget border, TextInputWidget input, TextWidget footer)
{
    var container = new Rect(0, 0, buffer.Width, buffer.Height);
    var slots = new[]
    {
        new FlexItem(basis: 1, grow: 0, shrink: 0),
        new FlexItem(basis: 3, grow: 0, shrink: 0),
        new FlexItem(basis: 1, grow: 0, shrink: 0)
    };

    var rects = FlexLayout.Layout(container, FlexDirection.Column, slots);
    title.Render(new WidgetRenderContext(buffer, rects[0], false));

    var boxRect = rects[1];
    border.Render(new WidgetRenderContext(buffer, boxRect, false));
    var inner = new Rect(boxRect.X + 1, boxRect.Y + 1, Math.Max(0, boxRect.Width - 2), Math.Max(0, boxRect.Height - 2));
    input.Render(new WidgetRenderContext(buffer, inner, input.IsFocused));

    footer.Render(new WidgetRenderContext(buffer, rects[2], false));
}

static void WriteText(ScreenBuffer buffer, int x, int y, string text, CellStyle style)
{
    for (var i = 0; i < text.Length; i++)
    {
        buffer.Set(x + i, y, new Cell(new Rune(text[i]), style));
    }
}

[SupportedOSPlatformGuard("macos")]
[SupportedOSPlatformGuard("linux")]
static bool IsPosix()
    => OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();
