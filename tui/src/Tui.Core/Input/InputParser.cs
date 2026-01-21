using System.Text;

namespace Tui.Core.Input;

public interface IInputParser
{
    IReadOnlyList<InputEvent> Feed(ReadOnlySpan<byte> bytes);
    IReadOnlyList<InputEvent> Flush();
    void Reset();
}

public sealed class InputParser : IInputParser
{
    private enum State
    {
        Normal = 0,
        Escape = 1,
        Csi = 2
    }

    private State _state;
    private readonly StringBuilder _csiBuffer = new();
    private readonly List<InputEvent> _events = new();

    public IReadOnlyList<InputEvent> Feed(ReadOnlySpan<byte> bytes)
    {
        _events.Clear();
        foreach (var b in bytes)
            ProcessByte(b);
        return _events.ToArray();
    }

    public IReadOnlyList<InputEvent> Flush()
    {
        _events.Clear();
        if (_state == State.Escape)
            _events.Add(new KeyEvent(Key.Escape));
        _state = State.Normal;
        _csiBuffer.Clear();
        return _events.ToArray();
    }

    public void Reset()
    {
        _state = State.Normal;
        _csiBuffer.Clear();
        _events.Clear();
    }

    private void ProcessByte(byte b)
    {
        switch (_state)
        {
            case State.Normal:
                ProcessNormal(b);
                break;
            case State.Escape:
                ProcessEscape(b);
                break;
            case State.Csi:
                ProcessCsi(b);
                break;
        }
    }

    private void ProcessNormal(byte b)
    {
        switch (b)
        {
            case 0x1B:
                _state = State.Escape;
                return;
            case 0x0D:
                _events.Add(new KeyEvent(Key.Enter));
                return;
            case 0x0A:
                return;
            case 0x09:
                _events.Add(new KeyEvent(Key.Tab));
                return;
            case 0x7F:
                _events.Add(new KeyEvent(Key.Backspace));
                return;
            case 0x03:
                _events.Add(new KeyEvent(Key.CtrlC, KeyModifiers.Ctrl));
                return;
        }

        if (b >= 0x20 && b <= 0x7E)
        {
            _events.Add(new TextInputEvent(((char)b).ToString()));
        }
    }

    private void ProcessEscape(byte b)
    {
        if (b == (byte)'[')
        {
            _state = State.Csi;
            _csiBuffer.Clear();
            return;
        }

        _events.Add(new KeyEvent(Key.Escape));
        _state = State.Normal;
        ProcessNormal(b);
    }

    private void ProcessCsi(byte b)
    {
        if (b >= (byte)'0' && b <= (byte)'9')
        {
            _csiBuffer.Append((char)b);
            return;
        }

        if (b == (byte)';')
        {
            _csiBuffer.Append(';');
            return;
        }

        var key = b switch
        {
            (byte)'A' => Key.Up,
            (byte)'B' => Key.Down,
            (byte)'C' => Key.Right,
            (byte)'D' => Key.Left,
            (byte)'H' => Key.Home,
            (byte)'F' => Key.End,
            (byte)'~' => ParseCsiTilde(_csiBuffer.ToString()),
            _ => Key.Unknown
        };

        if (key != Key.Unknown)
            _events.Add(new KeyEvent(key));

        _state = State.Normal;
        _csiBuffer.Clear();
    }

    private static Key ParseCsiTilde(string code)
        => code switch
        {
            "1" => Key.Home,
            "2" => Key.Insert,
            "3" => Key.Delete,
            "4" => Key.End,
            "5" => Key.PageUp,
            "6" => Key.PageDown,
            "7" => Key.Home,
            "8" => Key.End,
            _ => Key.Unknown
        };
}
