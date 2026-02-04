using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.AI.Core;

internal sealed partial class AgentSkillsRuntime
{
    internal async Task<IMessage> ExecuteSkillsFilesToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        var name = parameters.GetValueOrDefault("name")?.ToString();
        if (string.IsNullOrWhiteSpace(name))
            return ToStruct(new { success = false, error = "Parameter 'name' is required." });

        var dir = parameters.GetValueOrDefault("dir")?.ToString();
        if (string.IsNullOrWhiteSpace(dir))
            dir = ".";

        var recursive = TryGetBool(parameters.GetValueOrDefault("recursive"), fallback: false);
        var maxEntries = ClampInt(parameters.GetValueOrDefault("max_entries"), fallback: 200, min: 1, max: 2000);

        var match = FindSkillByName(name, cancellationToken);
        if (match == null)
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });

        var skillDir = Path.GetFullPath(match.DirectoryPath);
        var targetDir = ResolvePathUnderRoot(skillDir, dir);
        if (targetDir == null)
            return ToStruct(new { success = false, error = "Invalid dir (path traversal denied)." });

        if (!Directory.Exists(targetDir))
        {
            return ToStruct(new
            {
                success = false,
                error = $"Directory not found: {dir}",
                skill = match.Name
            });
        }

        var entries = new List<object>(capacity: Math.Min(maxEntries, 256));
        var truncated = false;

        IEnumerable<string> all;
        try
        {
            all = Directory.EnumerateFileSystemEntries(
                targetDir,
                "*",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _owner.InternalLogger.LogDebug(ex, "Failed to enumerate skill files (best-effort).");
            return ToStruct(new { success = false, error = ex.Message });
        }

        foreach (var e in all.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entries.Count >= maxEntries)
            {
                truncated = true;
                break;
            }

            var isDir = Directory.Exists(e);
            long? sizeBytes = null;
            if (!isDir)
            {
                try { sizeBytes = new FileInfo(e).Length; }
                catch { sizeBytes = null; }
            }

            var rel = Path.GetRelativePath(skillDir, e);
            entries.Add(new
            {
                path = rel.Replace('\\', '/'),
                type = isDir ? "dir" : "file",
                sizeBytes
            });
        }

        return ToStruct(new
        {
            success = true,
            name = match.Name,
            folderName = match.FolderName,
            dir = dir.Replace('\\', '/'),
            recursive,
            count = entries.Count,
            truncated,
            entries
        });
    }

    internal async Task<IMessage> ExecuteSkillsReadFileToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        int defaultMaxChars,
        CancellationToken cancellationToken)
    {
        var name = parameters.GetValueOrDefault("name")?.ToString();
        var path = parameters.GetValueOrDefault("path")?.ToString();

        if (string.IsNullOrWhiteSpace(name))
            return ToStruct(new { success = false, error = "Parameter 'name' is required." });
        if (string.IsNullOrWhiteSpace(path))
            return ToStruct(new { success = false, error = "Parameter 'path' is required." });

        var maxChars = ClampInt(parameters.GetValueOrDefault("max_chars"), defaultMaxChars, 1_000, 200_000);

        var match = FindSkillByName(name, cancellationToken);
        if (match == null)
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });

        var skillDir = Path.GetFullPath(match.DirectoryPath);
        var file = ResolvePathUnderRoot(skillDir, path);
        if (file == null)
            return ToStruct(new { success = false, error = "Invalid path (path traversal denied)." });

        if (!File.Exists(file))
        {
            return ToStruct(new
            {
                success = false,
                error = $"File not found: {path}",
                skill = match.Name
            });
        }

        string content;
        try
        {
            content = await ReadAllTextWithLimitAsync(file, maxChars, cancellationToken);
        }
        catch (Exception ex)
        {
            return ToStruct(new
            {
                success = false,
                error = ex.Message,
                file = path
            });
        }

        long? sizeBytes = null;
        try { sizeBytes = new FileInfo(file).Length; } catch { /* ignore */ }

        return ToStruct(new
        {
            success = true,
            name = match.Name,
            file = path.Replace('\\', '/'),
            sizeBytes,
            maxChars,
            truncated = content.Length >= maxChars,
            content
        });
    }

    internal async Task<IMessage> ExecuteSkillsRunPythonToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        int defaultMaxOutputChars,
        CancellationToken cancellationToken)
    {
        var name = parameters.GetValueOrDefault("name")?.ToString();
        var scriptRel = parameters.GetValueOrDefault("script")?.ToString();

        if (string.IsNullOrWhiteSpace(name))
            return ToStruct(new { success = false, error = "Parameter 'name' is required." });
        if (string.IsNullOrWhiteSpace(scriptRel))
            return ToStruct(new { success = false, error = "Parameter 'script' is required." });

        var restrictToScriptsDir = TryGetBool(parameters.GetValueOrDefault("restrictToScriptsDir"), fallback: true);
        var timeoutMs = ClampInt(parameters.GetValueOrDefault("timeoutMs"), 120_000, 100, 300_000);
        var maxOutputChars = ClampInt(parameters.GetValueOrDefault("maxOutputChars"), defaultMaxOutputChars, 256, 200_000);
        var pythonBin = parameters.GetValueOrDefault("pythonBin")?.ToString();

        var match = FindSkillByName(name, cancellationToken);
        if (match == null)
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });

        // Normalize to forward slash for checks (paths still resolved via Path APIs below).
        var scriptRelNorm = scriptRel.Trim().Replace('\\', '/');
        if (restrictToScriptsDir &&
            !scriptRelNorm.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scriptRelNorm, "scripts", StringComparison.OrdinalIgnoreCase))
        {
            return ToStruct(new
            {
                success = false,
                error = "Execution denied: script must be under 'scripts/' (set restrictToScriptsDir=false to override).",
                script = scriptRelNorm
            });
        }

        var skillDir = Path.GetFullPath(match.DirectoryPath);
        var scriptFull = ResolvePathUnderRoot(skillDir, scriptRelNorm);
        if (scriptFull == null)
            return ToStruct(new { success = false, error = "Invalid script path (path traversal denied)." });

        if (!File.Exists(scriptFull))
        {
            return ToStruct(new
            {
                success = false,
                error = $"Script not found: {scriptRelNorm}",
                skill = match.Name
            });
        }

        // Parse args
        var args = ParseStringArray(parameters.GetValueOrDefault("args"));
        var stdin = parameters.GetValueOrDefault("stdin")?.ToString() ?? string.Empty;

        var python = ResolvePythonBin(pythonBin);
        var sw = Stopwatch.StartNew();

        var psi = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = skillDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // -I: isolated mode; -u: unbuffered output (better for capturing logs)
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(scriptFull);
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["AEVATAR_SKILL_NAME"] = match.Name;
        psi.Environment["AEVATAR_SKILL_DIR"] = skillDir;
        psi.Environment["AEVATAR_SKILL_SCRIPT"] = scriptRelNorm;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = false };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            return ToStruct(new
            {
                success = false,
                error = $"failed to start python: {ex.Message}",
                python
            });
        }

        // Write stdin (best-effort)
        try
        {
            if (!string.IsNullOrEmpty(stdin))
            {
                await proc.StandardInput.WriteAsync(stdin);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            try { proc.StandardInput.Close(); } catch { /* ignore */ }
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

        // Drain streams concurrently to avoid deadlocks
        var stdoutTask = ReadAllWithLimitAsync(proc.StandardOutput, maxOutputChars, linked.Token);
        var stderrTask = ReadAllWithLimitAsync(proc.StandardError, maxOutputChars, linked.Token);

        var timedOut = false;
        try
        {
            await proc.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            TryKill(proc);
        }

        // Drain streams best-effort
        var stdout = await SafeAwaitAsync(stdoutTask);
        var stderr = await SafeAwaitAsync(stderrTask);

        sw.Stop();
        var exitCode = proc.HasExited ? proc.ExitCode : -1;

        return ToStruct(new
        {
            success = !timedOut && exitCode == 0,
            skill = match.Name,
            script = scriptRelNorm,
            python,
            exitCode,
            timedOut,
            timeoutMs,
            durationMs = (long)sw.Elapsed.TotalMilliseconds,
            stdout,
            stderr
        });
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

    private static async Task<string> ReadAllWithLimitAsync(StreamReader reader, int maxChars, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder(capacity: Math.Min(maxChars, 16 * 1024));
        var buffer = new char[4096];

        // Drain until EOF to avoid child process blocking on a full pipe.
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read <= 0)
                break;

            var remaining = maxChars - sb.Length;
            if (remaining <= 0)
            {
                // Discard remaining output but keep draining.
                continue;
            }

            sb.Append(buffer, 0, Math.Min(read, remaining));
        }

        return sb.ToString();
    }
}


