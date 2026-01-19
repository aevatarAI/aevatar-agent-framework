using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tui.Core.Primitives;

namespace Tui.Core.Terminal;

[SupportedOSPlatform("macos")]
[SupportedOSPlatform("linux")]
public sealed class PosixTerminalBackend : ITerminalBackend
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _isMac;
    private bool _entered;
    private TermiosDarwin _originalDarwin;
    private TermiosLinux _originalLinux;
    private PosixSignalRegistration? _sigWinch;

    public Viewport Viewport { get; private set; }

    public event Action<Viewport>? Resized;

    public PosixTerminalBackend()
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("POSIX terminal backend is not available on Windows.");

        _isMac = OperatingSystem.IsMacOS();
        _input = Console.OpenStandardInput();
        _output = Console.OpenStandardOutput();
        Viewport = ReadViewport();
    }

    public ValueTask EnterAsync(CancellationToken ct = default)
    {
        if (_entered)
            return ValueTask.CompletedTask;

        if (_isMac)
        {
            var termios = CreateDarwinTermios();
            EnsureSuccess(PosixNativeDarwin.tcgetattr(0, ref termios), "tcgetattr");
            _originalDarwin = termios;
            var raw = termios;
            PosixNativeDarwin.cfmakeraw(ref raw);
            EnsureSuccess(PosixNativeDarwin.tcsetattr(0, PosixNativeDarwin.TCSANOW, ref raw), "tcsetattr");
        }
        else
        {
            var termios = CreateLinuxTermios();
            EnsureSuccess(PosixNativeLinux.tcgetattr(0, ref termios), "tcgetattr");
            _originalLinux = termios;
            var raw = termios;
            PosixNativeLinux.cfmakeraw(ref raw);
            EnsureSuccess(PosixNativeLinux.tcsetattr(0, PosixNativeLinux.TCSANOW, ref raw), "tcsetattr");
        }

        Console.TreatControlCAsInput = true;
        _sigWinch = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ => OnResize());
        _entered = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask ExitAsync(CancellationToken ct = default)
    {
        if (!_entered)
            return ValueTask.CompletedTask;

        if (_isMac)
        {
            var termios = _originalDarwin;
            PosixNativeDarwin.tcsetattr(0, PosixNativeDarwin.TCSANOW, ref termios);
        }
        else
        {
            var termios = _originalLinux;
            PosixNativeLinux.tcsetattr(0, PosixNativeLinux.TCSANOW, ref termios);
        }

        _sigWinch?.Dispose();
        _sigWinch = null;
        _entered = false;
        return ValueTask.CompletedTask;
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _input.ReadAsync(buffer, ct);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        => _output.WriteAsync(buffer, ct);

    public ValueTask DisposeAsync()
    {
        return ExitAsync();
    }

    private void OnResize()
    {
        var viewport = ReadViewport();
        Viewport = viewport;
        Resized?.Invoke(viewport);
    }

    private Viewport ReadViewport()
    {
        WinSize size;
        if (_isMac)
        {
            var result = PosixNativeDarwin.ioctl(0, PosixNativeDarwin.TIOCGWINSZ, out size);
            if (result != 0)
                return new Viewport(Console.WindowWidth, Console.WindowHeight);
        }
        else
        {
            var result = PosixNativeLinux.ioctl(0, PosixNativeLinux.TIOCGWINSZ, out size);
            if (result != 0)
                return new Viewport(Console.WindowWidth, Console.WindowHeight);
        }

        return new Viewport(size.ws_col, size.ws_row);
    }

    private static void EnsureSuccess(int result, string name)
    {
        if (result == 0)
            return;

        var errno = Marshal.GetLastWin32Error();
        throw new InvalidOperationException($"{name} failed with errno {errno}.");
    }

    private static TermiosDarwin CreateDarwinTermios()
        => new() { c_cc = new byte[20] };

    private static TermiosLinux CreateLinuxTermios()
        => new() { c_cc = new byte[32] };

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TermiosDarwin
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] c_cc;
        public uint c_ispeed;
        public uint c_ospeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TermiosLinux
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;
        public uint c_ispeed;
        public uint c_ospeed;
    }

    private static class PosixNativeDarwin
    {
        public const int TCSANOW = 0;
        public const uint TIOCGWINSZ = 0x40087468;

        [DllImport("libc", SetLastError = true)]
        public static extern int tcgetattr(int fd, ref TermiosDarwin termios);

        [DllImport("libc", SetLastError = true)]
        public static extern int tcsetattr(int fd, int optionalActions, ref TermiosDarwin termios);

        [DllImport("libc", SetLastError = true)]
        public static extern void cfmakeraw(ref TermiosDarwin termios);

        [DllImport("libc", SetLastError = true)]
        public static extern int ioctl(int fd, uint request, out WinSize size);
    }

    private static class PosixNativeLinux
    {
        public const int TCSANOW = 0;
        public const uint TIOCGWINSZ = 0x5413;

        [DllImport("libc", SetLastError = true)]
        public static extern int tcgetattr(int fd, ref TermiosLinux termios);

        [DllImport("libc", SetLastError = true)]
        public static extern int tcsetattr(int fd, int optionalActions, ref TermiosLinux termios);

        [DllImport("libc", SetLastError = true)]
        public static extern void cfmakeraw(ref TermiosLinux termios);

        [DllImport("libc", SetLastError = true)]
        public static extern int ioctl(int fd, uint request, out WinSize size);
    }
}
