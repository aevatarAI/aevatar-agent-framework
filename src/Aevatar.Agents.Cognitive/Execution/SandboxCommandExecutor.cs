using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  SandboxCommandExecutor
//
//  Responsibility:
//  - Run a verifier command non-interactively with strict timeout + bounded output
//  - Enforce optional allowlist (env/config driven)
//
//  Output (Protobuf-Value friendly Dictionary):
//  {
//    ok: bool,                   // verifier pass == (exit_code==0 && !timed_out)
//    exit_code: int,
//    timed_out: bool,
//    duration_ms: int,
//    stdout: string,
//    stderr: string,
//    truncated: { stdout: bool, stderr: bool },
//    error: string?
//  }
// ============================================================

public sealed class SandboxCommandExecutor
{
    public const string AllowedCommandsEnvVar = "AEVATAR_COGNITIVE_ALLOWED_COMMANDS";

    private const int DefaultTimeoutMs = 120_000;
    private const int DefaultMaxOutputChars = 60_000;

    private static readonly Regex PurePathTemplateRegex = new(@"^\s*\{\{\s*([a-zA-Z_][\w\.]*)\s*\}\}\s*$", RegexOptions.Compiled);

    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;

    public SandboxCommandExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    public PrimitiveResult Execute(StepDefinition step, Dictionary<string, object> variables, string workspaceRoot)
    {
        var commandTemplate = step.Parameters.GetValueOrDefault("command")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(commandTemplate))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["exit_code"] = -1,
                ["timed_out"] = false,
                ["duration_ms"] = 0,
                ["stdout"] = "",
                ["stderr"] = "",
                ["truncated"] = new Dictionary<string, object?> { ["stdout"] = false, ["stderr"] = false },
                ["error"] = "missing_required_param:command"
            });
        }

        var command = _templateEngine.Render(commandTemplate, variables).Replace("\r", "").Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["exit_code"] = -1,
                ["timed_out"] = false,
                ["duration_ms"] = 0,
                ["stdout"] = "",
                ["stderr"] = "",
                ["truncated"] = new Dictionary<string, object?> { ["stdout"] = false, ["stderr"] = false },
                ["error"] = "command_empty"
            }) with
            {
                UserPrompt = "sandbox_command: (empty)"
            };
        }

        // Optional allowlist (host-controlled)
        var allow = ParseAllowedCommands(Environment.GetEnvironmentVariable(AllowedCommandsEnvVar));
        if (allow.Count > 0 && !allow.Contains(NormalizeCommandName(command)))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["exit_code"] = -1,
                ["timed_out"] = false,
                ["duration_ms"] = 0,
                ["stdout"] = "",
                ["stderr"] = "",
                ["truncated"] = new Dictionary<string, object?> { ["stdout"] = false, ["stderr"] = false },
                ["error"] = "command_denied"
            }) with
            {
                UserPrompt = $"sandbox_command: denied '{command}'"
            };
        }

        var timeoutMs = ResolveInt(step.Parameters.GetValueOrDefault("timeout_ms"), variables, DefaultTimeoutMs);
        timeoutMs = Math.Clamp(timeoutMs, 100, 600_000); // [100ms, 10min]

        var maxOutputChars = ResolveInt(step.Parameters.GetValueOrDefault("max_output_chars"), variables, DefaultMaxOutputChars);
        maxOutputChars = Math.Clamp(maxOutputChars, 1_000, 500_000);

        var argsResolved = ResolveValue(step.Parameters.GetValueOrDefault("args"), variables);
        var args = NormalizeArgs(argsResolved);

        var workingDirTemplate = step.Parameters.GetValueOrDefault("working_dir")?.ToString();
        string? workingDir = null;
        if (!string.IsNullOrWhiteSpace(workingDirTemplate))
        {
            var rendered = _templateEngine.Render(workingDirTemplate!, variables).Replace("\r", "").Trim();
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                if (!WorkspacePathGuard.TryResolvePathWithinRoot(workspaceRoot, rendered!, out var fullWd, out var wdError))
                {
                    return PrimitiveResult.Ok(new Dictionary<string, object?>
                    {
                        ["ok"] = false,
                        ["exit_code"] = -1,
                        ["timed_out"] = false,
                        ["duration_ms"] = 0,
                        ["stdout"] = "",
                        ["stderr"] = "",
                        ["truncated"] = new Dictionary<string, object?> { ["stdout"] = false, ["stderr"] = false },
                        ["error"] = Bound(wdError, 400)
                    }) with
                    {
                        UserPrompt = $"sandbox_command: {command}"
                    };
                }

                workingDir = fullWd;
            }
        }

        var startAt = Stopwatch.GetTimestamp();
        var stdoutSb = new StringBuilder(capacity: Math.Min(maxOutputChars, 16_384));
        var stderrSb = new StringBuilder(capacity: Math.Min(maxOutputChars, 16_384));

        var stdoutTruncated = false;
        var stderrTruncated = false;

        var timedOut = false;
        var exitCode = -1;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(workingDir))
                psi.WorkingDirectory = workingDir!;

            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start())
            {
                return PrimitiveResult.Ok(new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["exit_code"] = -1,
                    ["timed_out"] = false,
                    ["duration_ms"] = 0,
                    ["stdout"] = "",
                    ["stderr"] = "",
                    ["truncated"] = new Dictionary<string, object?> { ["stdout"] = false, ["stderr"] = false },
                    ["error"] = "process_start_failed"
                }) with
                {
                    UserPrompt = $"sandbox_command: {command}"
                };
            }

            // Non-interactive: close stdin immediately (EOF)
            try { process.StandardInput.Close(); } catch { /* ignore */ }

            var stdoutTask = ReadStreamBoundedAsync(process.StandardOutput, stdoutSb, maxOutputChars)
                .ContinueWith(t => stdoutTruncated = t.Result.truncated);
            var stderrTask = ReadStreamBoundedAsync(process.StandardError, stderrSb, maxOutputChars)
                .ContinueWith(t => stderrTruncated = t.Result.truncated);

            var waitTask = process.WaitForExitAsync();
            var completed = Task.WhenAny(waitTask, Task.Delay(timeoutMs)).GetAwaiter().GetResult();
            if (completed != waitTask)
            {
                timedOut = true;
                TryKill(process);
                process.WaitForExit(2000);
            }

            exitCode = timedOut ? -1 : process.ExitCode;

            // Ensure readers finished (best-effort)
            try { Task.WaitAll(new[] { stdoutTask, stderrTask }, TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[sandbox_command] execution failed.");
            var durationMsErr = ElapsedMs(startAt);
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["exit_code"] = -1,
                ["timed_out"] = false,
                ["duration_ms"] = durationMsErr,
                ["stdout"] = Bound(stdoutSb.ToString(), maxOutputChars),
                ["stderr"] = Bound(stderrSb.ToString(), maxOutputChars),
                ["truncated"] = new Dictionary<string, object?> { ["stdout"] = stdoutTruncated, ["stderr"] = stderrTruncated },
                ["error"] = Bound(ex.Message, 400)
            }) with
            {
                UserPrompt = $"sandbox_command: {command}"
            };
        }

        var durationMs = ElapsedMs(startAt);
        var stdout = Bound(stdoutSb.ToString(), maxOutputChars);
        var stderr = Bound(stderrSb.ToString(), maxOutputChars);

        var ok = !timedOut && exitCode == 0;
        var result = new Dictionary<string, object?>
        {
            ["ok"] = ok,
            ["exit_code"] = exitCode,
            ["timed_out"] = timedOut,
            ["duration_ms"] = durationMs,
            ["stdout"] = stdout,
            ["stderr"] = stderr,
            ["truncated"] = new Dictionary<string, object?> { ["stdout"] = stdoutTruncated, ["stderr"] = stderrTruncated }
        };

        if (timedOut)
            result["error"] = $"timeout>{timeoutMs}ms";

        return PrimitiveResult.Ok(result) with
        {
            UserPrompt = $"sandbox_command: {command} {string.Join(' ', args)}"
        };
    }

    private static HashSet<string> ParseAllowedCommands(string? raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return set;

        var parts = raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (s.Length == 0) continue;
            set.Add(NormalizeCommandName(s));
        }

        return set;
    }

    private static string NormalizeCommandName(string command)
    {
        var name = command.Trim();
        try { name = Path.GetFileName(name); } catch { /* ignore */ }
        return name.Trim().ToLowerInvariant();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best-effort
        }
    }

    private static int ElapsedMs(long startTimestamp)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
        return (int)(elapsedTicks * 1000 / (double)Stopwatch.Frequency);
    }

    private static async Task<(bool truncated, int totalChars)> ReadStreamBoundedAsync(
        StreamReader reader,
        StringBuilder sb,
        int maxChars)
    {
        var buf = new char[4096];
        var total = 0;
        var truncated = false;

        while (true)
        {
            var n = await reader.ReadAsync(buf, 0, buf.Length);
            if (n <= 0) break;
            total += n;

            if (sb.Length < maxChars)
            {
                var toTake = Math.Min(n, maxChars - sb.Length);
                sb.Append(buf, 0, toTake);
            }
            else
            {
                truncated = true;
            }
        }

        if (total > maxChars) truncated = true;
        return (truncated, total);
    }

    private object? ResolveValue(object? value, Dictionary<string, object> vars)
    {
        if (value is not string template)
            return value;

        var match = PurePathTemplateRegex.Match(template);
        if (match.Success)
        {
            var path = match.Groups[1].Value.Trim();
            return ResolvePathValue(vars, path);
        }

        return _templateEngine.Render(template, vars);
    }

    private static object? ResolvePathValue(Dictionary<string, object> variables, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        if (!variables.TryGetValue(parts[0], out var current) || current == null)
            return null;

        for (var i = 1; i < parts.Length; i++)
        {
            var key = parts[i];
            current = current switch
            {
                IDictionary<string, object> dict => dict.TryGetValue(key, out var v) ? v : null,
                System.Collections.IDictionary nd => nd.Contains(key) ? nd[key] : null,
                _ => null
            };
            if (current == null) return null;
        }

        return current;
    }

    private static List<string> NormalizeArgs(object? args)
    {
        if (args == null) return [];

        if (args is IEnumerable<string> ss)
            return ss.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

        if (args is System.Collections.IEnumerable e)
        {
            var list = new List<string>();
            foreach (var it in e)
            {
                var s = it?.ToString();
                if (string.IsNullOrWhiteSpace(s)) continue;
                list.Add(s.Trim());
            }
            return list;
        }

        var single = args.ToString();
        return string.IsNullOrWhiteSpace(single) ? [] : [single!.Trim()];
    }

    private int ResolveInt(object? value, Dictionary<string, object> vars, int defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => ConvertToInt(_templateEngine.Evaluate(s, vars), defaultValue),
            _ => defaultValue
        };
    }

    private static int ConvertToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    private static string Bound(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "...(truncated)";
    }
}



