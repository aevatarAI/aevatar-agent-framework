using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace VibeResearching.Vibe.Tools;

// ============================================================
//  python_exec (dangerous tool, opt-in)
//
//  Why:
//  - Math/Physics/Bio tasks often need "executable verification".
//
//  Safety:
//  - Marked as dangerous => hidden unless AllowDangerousTools=true
//  - Time-bounded + output-bounded
// ============================================================

internal sealed class PythonExecTool : AevatarToolBase
{
    private readonly PythonExecToolOptions _options;

    public PythonExecTool(PythonExecToolOptions options)
    {
        _options = options ?? new PythonExecToolOptions();
    }

    public override string Name => "python_exec";
    public override string Description => "Execute Python code (dangerous; time/output bounded). Returns stdout/stderr and exitCode.";
    public override ToolCategory Category => ToolCategory.Utility;

    protected override bool IsDangerous() => true;

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = ["code"],
            Items = new Dictionary<string, ToolParameter>
            {
                ["code"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Python source code to execute",
                    Required = true,
                    MinLength = 1,
                    MaxLength = 50_000
                },
                ["timeoutMs"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Timeout in ms (100..120000). If omitted, uses server config.",
                    Required = false,
                    DefaultValue = _options.TimeoutMs
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var code = (parameters.GetValueOrDefault("code")?.ToString() ?? string.Empty).Replace("\r", "").Trim();
        if (code.Length == 0)
            throw new ArgumentException("code is required");

        if (code.Length > 50_000)
            throw new InvalidOperationException("code too large (max 50000 chars)");

        var timeoutMs = ClampInt(parameters.GetValueOrDefault("timeoutMs"), _options.TimeoutMs, 100, 120_000);
        var maxOutputChars = Math.Clamp(_options.MaxOutputChars, 256, 200_000);

        var sw = Stopwatch.StartNew();
        var tmpDir = Path.Combine(Path.GetTempPath(), $"sra-python-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        var file = Path.Combine(tmpDir, "main.py");
        await File.WriteAllTextAsync(file, code, Encoding.UTF8, cancellationToken);

        var python = ResolvePythonBin();

        var psi = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = tmpDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // -I: isolate from user site-packages; -u: unbuffered output (more predictable streaming).
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(file);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return ToStruct(new
            {
                ok = false,
                error = $"failed to start python: {ex.Message}",
                python,
                timeoutMs,
                maxOutputChars
            });
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        var stdout = new BoundedStringBuilder(maxOutputChars);
        var stderr = new BoundedStringBuilder(maxOutputChars);

        var readStdout = ReadAllAsync(proc.StandardOutput, stdout, cts.Token);
        var readStderr = ReadAllAsync(proc.StandardError, stderr, cts.Token);

        var timedOut = false;
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            TryKill(proc);
        }

        // Drain streams best-effort (avoid deadlocks).
        await Safe(readStdout);
        await Safe(readStderr);

        sw.Stop();

        var exitCode = proc.HasExited ? proc.ExitCode : -1;

        var result = new
        {
            ok = !timedOut && exitCode == 0,
            exitCode,
            timedOut,
            timeoutMs,
            durationMs = (long)sw.Elapsed.TotalMilliseconds,
            stdout = stdout.Value,
            stderr = stderr.Value,
            truncated = new { stdout = stdout.Truncated, stderr = stderr.Truncated }
        };

        // Cleanup best-effort
        try
        {
            Directory.Delete(tmpDir, recursive: true);
        }
        catch
        {
            // ignore
        }

        return ToStruct(result);
    }

    private static string ResolvePythonBin()
    {
        // Allow override for environments where python3 isn't on PATH.
        var env = (Environment.GetEnvironmentVariable("SRA_PYTHON_BIN") ?? string.Empty).Trim();
        if (env.Length > 0)
            return env;
        return "python3";
    }

    private static void TryKill(Process p)
    {
        try
        {
            if (!p.HasExited)
                p.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
    }

    private static async Task ReadAllAsync(StreamReader reader, BoundedStringBuilder sink, CancellationToken ct)
    {
        var buf = new char[2048];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var n = await reader.ReadAsync(buf.AsMemory(0, buf.Length), ct);
            if (n <= 0) break;
            sink.Append(buf.AsSpan(0, n));
        }
    }

    private static async Task Safe(Task t)
    {
        try
        {
            await t;
        }
        catch
        {
            // ignore
        }
    }

    private static int ClampInt(object? v, int fallback, int min, int max)
    {
        if (!TryParseInt(v, out var i))
            i = fallback;
        return Math.Clamp(i, min, max);
    }

    private static bool TryParseInt(object? v, out int i)
    {
        i = 0;
        if (v == null) return false;
        if (v is int ii) { i = ii; return true; }
        if (v is long ll) { i = (int)Math.Clamp(ll, int.MinValue, int.MaxValue); return true; }
        return int.TryParse(v.ToString(), out i);
    }

    private static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        return JsonParser.Default.Parse<Struct>(json);
    }

    private sealed class BoundedStringBuilder
    {
        private readonly int _maxChars;
        private readonly StringBuilder _sb;

        public bool Truncated { get; private set; }

        public BoundedStringBuilder(int maxChars)
        {
            _maxChars = Math.Clamp(maxChars, 256, 200_000);
            _sb = new StringBuilder(capacity: Math.Min(_maxChars, 4096));
        }

        public string Value => _sb.ToString();

        public void Append(ReadOnlySpan<char> span)
        {
            if (span.Length == 0) return;

            var remaining = _maxChars - _sb.Length;
            if (remaining <= 0)
            {
                Truncated = true;
                return;
            }

            if (span.Length <= remaining)
            {
                _sb.Append(span);
                return;
            }

            _sb.Append(span[..remaining]);
            Truncated = true;
        }
    }
}

internal sealed class PythonExecToolOptions
{
    public int TimeoutMs { get; init; } = 15_000;
    public int MaxOutputChars { get; init; } = 8_000;
}


