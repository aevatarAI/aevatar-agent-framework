using Tui.Core.Primitives;

namespace Tui.Core.Input;

public enum Key
{
    Unknown = 0,
    Escape,
    Enter,
    Backspace,
    Tab,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    Delete,
    Insert,
    CtrlC
}

[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    Alt = 1 << 1,
    Ctrl = 1 << 2
}

public abstract record InputEvent;

public sealed record KeyEvent(Key Key, KeyModifiers Modifiers = KeyModifiers.None) : InputEvent;

public sealed record TextInputEvent(string Text) : InputEvent;

public sealed record ResizeEvent(Viewport Viewport) : InputEvent;
