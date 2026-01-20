using Tui.Core.Input;

namespace Tui.Widgets.Input;

public sealed class FocusManager
{
    private readonly List<IFocusableWidget> _widgets = new();
    private int _index = -1;

    public void Register(IFocusableWidget widget)
    {
        if (_widgets.Contains(widget))
            return;

        _widgets.Add(widget);
        if (_index == -1)
            SetFocus(0);
    }

    public void HandleInput(InputEvent input)
    {
        if (_widgets.Count == 0)
            return;

        if (input is KeyEvent { Key: Key.Tab })
        {
            MoveNext();
            return;
        }

        if (_index >= 0)
            _widgets[_index].HandleInput(input);
    }

    public void MoveNext()
    {
        if (_widgets.Count == 0)
            return;

        var next = (_index + 1) % _widgets.Count;
        SetFocus(next);
    }

    public void SetFocus(int index)
    {
        if (index < 0 || index >= _widgets.Count)
            return;

        if (_index >= 0)
            _widgets[_index].SetFocus(false);

        _index = index;
        _widgets[_index].SetFocus(true);
    }
}
