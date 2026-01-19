using Tui.Core.Input;

namespace Tui.Widgets.Input;

public interface IFocusableWidget
{
    bool IsFocused { get; }
    void SetFocus(bool focused);
    void HandleInput(InputEvent input);
}
