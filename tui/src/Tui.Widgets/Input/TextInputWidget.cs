using System.Text;
using Tui.Core.Input;
using Tui.Core.Primitives;
using Tui.Widgets.Core;

namespace Tui.Widgets.Input;

public sealed class TextInputWidget : IWidget, IFocusableWidget
{
    public string Text { get; private set; } = string.Empty;
    public int CursorIndex { get; private set; }
    public bool IsFocused { get; private set; }
    public CellStyle TextStyle { get; set; } = CellStyle.Default;
    public CellStyle CursorStyle { get; set; } = new(TuiColor.Default, TuiColor.Default, TextAttribute.Inverse);

    public void Render(WidgetRenderContext context)
    {
        if (context.Bounds.Width <= 0 || context.Bounds.Height <= 0)
            return;

        var visibleText = GetVisibleText(context.Bounds.Width);
        for (var i = 0; i < context.Bounds.Width; i++)
        {
            var ch = i < visibleText.Length ? visibleText[i] : ' ';
            var style = IsFocused && i == GetCursorColumn(context.Bounds.Width) ? CursorStyle : TextStyle;
            context.Buffer.Set(
                context.Bounds.X + i,
                context.Bounds.Y,
                new Cell(new Rune(ch), style));
        }
    }

    public void SetFocus(bool focused)
        => IsFocused = focused;

    public void HandleInput(InputEvent input)
    {
        if (!IsFocused)
            return;

        switch (input)
        {
            case TextInputEvent text:
                InsertText(text.Text);
                break;
            case KeyEvent key:
                HandleKey(key.Key);
                break;
        }
    }

    private void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var before = Text[..CursorIndex];
        var after = Text[CursorIndex..];
        Text = string.Concat(before, text, after);
        CursorIndex += text.Length;
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Left:
                if (CursorIndex > 0)
                    CursorIndex--;
                break;
            case Key.Right:
                if (CursorIndex < Text.Length)
                    CursorIndex++;
                break;
            case Key.Backspace:
                if (CursorIndex > 0)
                    DeleteAt(CursorIndex - 1);
                break;
            case Key.Delete:
                if (CursorIndex < Text.Length)
                    DeleteAt(CursorIndex);
                break;
            case Key.Home:
                CursorIndex = 0;
                break;
            case Key.End:
                CursorIndex = Text.Length;
                break;
        }
    }

    private void DeleteAt(int index)
    {
        if (index < 0 || index >= Text.Length)
            return;

        Text = Text.Remove(index, 1);
        if (CursorIndex > index)
            CursorIndex--;
    }

    private string GetVisibleText(int width)
    {
        if (Text.Length <= width)
            return Text;

        var start = Math.Max(0, CursorIndex - width + 1);
        return Text.Substring(start, width);
    }

    private int GetCursorColumn(int width)
    {
        if (Text.Length <= width)
            return CursorIndex;

        var start = Math.Max(0, CursorIndex - width + 1);
        return CursorIndex - start;
    }
}
