using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;

/// <summary>
/// Execute Claude Agent SDK via an external headless runner process (Node/Python).
///
/// Core rules:
/// - Always drain stdout/stderr to avoid pipe deadlock
/// - Enforce a hard timeout (separate from caller cancellation)
/// - Best-effort kill entire process tree on timeout/cancel
/// - Never log secrets (runner env can contain API keys)
/// </summary>
internal sealed class ClaudeAgentSdkRunner
{
    private const int MaxLineChars = 64 * 1024; // prevent pathological single-line memory blow-up

    private readonly ClaudeAgentSdkProviderConfig _config;
    private readonly ILogger? _logger;

    public ClaudeAgentSdkRunner(ClaudeAgentSdkProviderConfig config, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    public async Task<(string StdOut, string StdErr, int ExitCode, bool TimedOut)> ExecuteAsync(
        string requestJson,
        CancellationToken cancellationToken)
    {
        var (process, stdoutReader, stderrReader) = StartProcess();

        using (process)
        {
            // Write input JSON then close stdin
            await WriteStdinAsync(process, requestJson, cancellationToken);

            // Drain stdout/stderr concurrently (tail-limited, still drains fully)
            var stdoutTask = ReadAllWithTailLimitAsync(stdoutReader, _config.MaxOutputChars, cancellationToken);
            var stderrTask = ReadAllWithTailLimitAsync(stderrReader, _config.MaxOutputChars, cancellationToken);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_config.TimeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                // Caller cancellation has higher priority than timeout.
                if (cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    throw;
                }

                // Timeout
                TryKill(process);
                var outText = await SafeAwaitAsync(stdoutTask);
                var errText = await SafeAwaitAsync(stderrTask);
                return (outText, errText, -1, true);
            }

            var stdout = await SafeAwaitAsync(stdoutTask);
            var stderr = await SafeAwaitAsync(stderrTask);
            return (stdout, stderr, process.ExitCode, false);
        }
    }

    public async IAsyncEnumerable<string> ExecuteStreamingAsync(
        string requestJson,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (process, stdoutReader, stderrReader) = StartProcess();

        using (process)
        {
            await WriteStdinAsync(process, requestJson, cancellationToken);

            // stderr: capture tail for diagnostics (best-effort), but do not block streaming.
            var stderrTask = ReadAllWithTailLimitAsync(stderrReader, _config.MaxOutputChars, cancellationToken);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_config.TimeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var sawDelta = false;
            string? finalJson = null;
            string? finalContent = null;

            try
            {
                await foreach (var line in ReadLinesAsync(stdoutReader, linked.Token))
                {
                    if (ClaudeAgentSdkProtocol.TryParseStreamLine(line, out var delta))
                    {
                        sawDelta = true;
                        if (!string.IsNullOrEmpty(delta))
                        {
                            yield return delta;
                        }
                        continue;
                    }

                    if (ClaudeAgentSdkProtocol.TryExtractOutputJsonFromLine(line, out var json))
                    {
                        finalJson = json;
                        if (ClaudeAgentSdkProtocol.TryExtractContentFromOutputJson(json, out var c))
                        {
                            finalContent = c;
                        }
                    }
                }

                // If we reached EOF, the process likely exited; still wait best-effort.
                await process.WaitForExitAsync(linked.Token);
            }
            finally
            {
                // On timeout/cancel, ensure we don't leak a runner process.
                if (linked.IsCancellationRequested && !process.HasExited)
                {
                    TryKill(process);
                }

                _ = await SafeAwaitAsync(stderrTask);
            }

            // If no streaming deltas were produced, degrade to a single chunk using final JSON (best-effort).
            if (!sawDelta)
            {
                if (!string.IsNullOrEmpty(finalContent))
                {
                    yield return finalContent;
                    yield break;
                }

                if (!string.IsNullOrEmpty(finalJson) &&
                    ClaudeAgentSdkProtocol.TryExtractContentFromOutputJson(finalJson, out var contentFromJson))
                {
                    yield return contentFromJson;
                }
            }
        }
    }

    private (Process Process, StreamReader StdOut, StreamReader StdErr) StartProcess()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _config.RunnerCommand,
            WorkingDirectory = _config.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in _config.RunnerArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        // Extra env (best-effort). Do NOT log values.
        if (_config.ExtraEnvironment.Count > 0)
        {
            foreach (var (k, v) in _config.ExtraEnvironment)
            {
                if (!string.IsNullOrWhiteSpace(k) && v != null)
                {
                    psi.Environment[k] = v;
                }
            }
        }

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start Claude Agent SDK runner process: {Command}", psi.FileName);
            throw;
        }

        return (process, process.StandardOutput, process.StandardError);
    }

    private static async Task WriteStdinAsync(Process process, string requestJson, CancellationToken cancellationToken)
    {
        if (process.StandardInput.BaseStream.CanWrite)
        {
            await process.StandardInput.WriteAsync(requestJson.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        process.StandardInput.Close();
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to kill Claude Agent SDK runner process (best-effort)");
        }
    }

    private static async Task<string> SafeAwaitAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<string> ReadAllWithTailLimitAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder(capacity: Math.Min(maxChars, 16 * 1024));
        var buffer = new char[4096];

        // IMPORTANT:
        // - We MUST keep draining until EOF.
        // - Keep tail (not head) because the final marker is typically appended at the end.
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0)
                break;

            sb.Append(buffer, 0, read);

            if (sb.Length > maxChars)
            {
                // Remove from the front in one shot (keeps tail).
                sb.Remove(0, sb.Length - maxChars);
            }
        }

        return sb.ToString();
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buf = new char[4096];
        var line = new StringBuilder();

        while (true)
        {
            var read = await reader.ReadAsync(buf.AsMemory(0, buf.Length), cancellationToken);
            if (read <= 0)
                break;

            for (var i = 0; i < read; i++)
            {
                var c = buf[i];
                if (c == '\n')
                {
                    yield return TrimLineEnding(line);
                    line.Clear();
                    continue;
                }

                // Bound per-line memory but keep draining input.
                if (line.Length < MaxLineChars)
                {
                    line.Append(c);
                }
            }
        }

        if (line.Length > 0)
        {
            yield return TrimLineEnding(line);
        }
    }

    private static string TrimLineEnding(StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] == '\r')
        {
            sb.Length -= 1;
        }

        return sb.ToString();
    }
}


