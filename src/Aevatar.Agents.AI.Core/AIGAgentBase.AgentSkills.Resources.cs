using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ============================================================
    //  Agent Skills: resource discovery + script execution
    //
    //  AgentSkills spec reminder:
    //  - A skill folder may contain:
    //    SKILL.md (required)
    //    scripts/ (optional executable code)
    //    references/ (optional docs)
    //    assets/ (optional templates/resources)
    //
    //  Our goal:
    //  - Let the model *discover* files under a skill folder
    //  - Let the model *read* text resources
    //  - Let the model *execute* scripts (dangerous, policy-gated)
    // ============================================================

    private async Task RegisterAgentSkillsResourceToolsAsync(CancellationToken cancellationToken = default)
    {
        // Disabled -> don't expose tools to the model.
        if (!EnableAgentSkills)
            return;

        EnsureToolManagerInitialized();

        // skills_files
        var filesTool = new ToolDefinition
        {
            Name = "skills_files",
            Description =
                "List files/directories under a specific Agent Skill folder (e.g., scripts/, references/, assets/).",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "resources", "discovery" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["name"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Skill name (from SKILL.md front matter 'name') or folder name"
                    },
                    ["dir"] = new()
                    {
                        Type = "string",
                        Description = "Relative directory inside the skill folder to list (default: '.')",
                        Required = false,
                        DefaultValue = "."
                    },
                    ["recursive"] = new()
                    {
                        Type = "boolean",
                        Description = "If true, list recursively (default: false)",
                        Required = false,
                        DefaultValue = false
                    },
                    ["max_entries"] = new()
                    {
                        Type = "integer",
                        Description = "Max entries to return (default: 200; range 1..2000)",
                        Required = false,
                        DefaultValue = 200
                    }
                },
                Required = new[] { "name" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsFilesToolAsync(parameters, executionContext, ct)
        };

        // skills_read_file
        var readFileTool = new ToolDefinition
        {
            Name = "skills_read_file",
            Description = "Read a text file inside an Agent Skill folder (e.g., scripts/*.py, references/*.md).",
            Category = ToolCategory.Core,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "filesystem", "resources", "read" },
            RequiresInternalAccess = true,
            IsDangerous = false,
            CanBeOverridden = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["name"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Skill name (from SKILL.md front matter 'name') or folder name"
                    },
                    ["path"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Relative file path inside the skill folder"
                    },
                    ["max_chars"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max characters to return (default: uses host tool output budget)",
                        DefaultValue = HookOptions.MaxToolOutputChars
                    }
                },
                Required = new[] { "name", "path" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsReadFileToolAsync(parameters, executionContext, ct)
        };

        // skills_run_python (dangerous)
        var runPythonTool = new ToolDefinition
        {
            Name = "skills_run_python",
            Description =
                "Execute a Python script inside an Agent Skill folder (dangerous). Captures stdout/stderr.",
            Category = ToolCategory.Utility,
            Version = "1.0.0",
            Tags = new List<string> { "skills", "agent-skills", "scripts", "python", "execute" },
            RequiresInternalAccess = true,
            IsDangerous = true,
            CanBeOverridden = true,
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["name"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Skill name (from SKILL.md front matter 'name') or folder name"
                    },
                    ["script"] = new()
                    {
                        Type = "string",
                        Required = true,
                        Description = "Relative script path inside the skill folder (recommended under 'scripts/')"
                    },
                    ["args"] = new()
                    {
                        Type = "array",
                        Required = false,
                        Description = "Optional CLI args (JSON array of strings)."
                    },
                    ["stdin"] = new()
                    {
                        Type = "string",
                        Required = false,
                        Description = "Optional stdin text to pass to the script"
                    },
                    ["timeoutMs"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Timeout in ms (100..300000). Default: 120000.",
                        DefaultValue = 120_000
                    },
                    ["maxOutputChars"] = new()
                    {
                        Type = "integer",
                        Required = false,
                        Description = "Max stdout/stderr chars to keep (256..200000). Default: 16000.",
                        DefaultValue = 16_000
                    },
                    ["pythonBin"] = new()
                    {
                        Type = "string",
                        Required = false,
                        Description = "Optional python executable path (overrides AEVATAR_PYTHON_BIN)."
                    },
                    ["restrictToScriptsDir"] = new()
                    {
                        Type = "boolean",
                        Required = false,
                        Description = "If true, only allow executing files under 'scripts/' (default: true).",
                        DefaultValue = true
                    }
                },
                Required = new[] { "name", "script" }
            },
            ExecuteAsync = async (parameters, executionContext, ct) =>
                await ExecuteSkillsRunPythonToolAsync(parameters, executionContext, ct)
        };

        await ToolManager.RegisterToolAsync(filesTool, cancellationToken);
        await ToolManager.RegisterToolAsync(readFileTool, cancellationToken);
        await ToolManager.RegisterToolAsync(runPythonTool, cancellationToken);
    }

    private async Task<IMessage> ExecuteSkillsFilesToolAsync(
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
        {
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });
        }

        var skillDir = Path.GetFullPath(match.DirectoryPath);
        var targetDir = ResolvePathUnderRoot(skillDir, dir);
        if (targetDir == null)
        {
            return ToStruct(new { success = false, error = "Invalid dir (path traversal denied)." });
        }

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
            Logger.LogDebug(ex, "Failed to enumerate skill files (best-effort).");
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
                try
                {
                    sizeBytes = new FileInfo(e).Length;
                }
                catch
                {
                    sizeBytes = null;
                }
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

    private async Task<IMessage> ExecuteSkillsReadFileToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        var name = parameters.GetValueOrDefault("name")?.ToString();
        var path = parameters.GetValueOrDefault("path")?.ToString();

        if (string.IsNullOrWhiteSpace(name))
            return ToStruct(new { success = false, error = "Parameter 'name' is required." });
        if (string.IsNullOrWhiteSpace(path))
            return ToStruct(new { success = false, error = "Parameter 'path' is required." });

        var maxChars = ClampInt(parameters.GetValueOrDefault("max_chars"), HookOptions.MaxToolOutputChars, 1_000, 200_000);

        var match = FindSkillByName(name, cancellationToken);
        if (match == null)
        {
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });
        }

        var skillDir = Path.GetFullPath(match.DirectoryPath);
        var file = ResolvePathUnderRoot(skillDir, path);
        if (file == null)
        {
            return ToStruct(new { success = false, error = "Invalid path (path traversal denied)." });
        }

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

    private async Task<IMessage> ExecuteSkillsRunPythonToolAsync(
        Dictionary<string, object> parameters,
        ToolExecutionContext? executionContext,
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
        var maxOutputChars = ClampInt(parameters.GetValueOrDefault("maxOutputChars"), HookOptions.MaxToolOutputChars, 256, 200_000);
        var pythonBin = parameters.GetValueOrDefault("pythonBin")?.ToString();

        var match = FindSkillByName(name, cancellationToken);
        if (match == null)
        {
            return ToStruct(new { success = false, error = $"Skill '{name}' not found." });
        }

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
        {
            return ToStruct(new { success = false, error = "Invalid script path (path traversal denied)." });
        }

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
                python,
                script = scriptRelNorm
            });
        }

        // Write stdin then close.
        if (!string.IsNullOrEmpty(stdin))
        {
            await proc.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
        }

        await proc.StandardInput.FlushAsync(cancellationToken);
        proc.StandardInput.Close();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        var stdoutTask = ReadAllWithLimitAsync(proc.StandardOutput, maxOutputChars, cts.Token);
        var stderrTask = ReadAllWithLimitAsync(proc.StandardError, maxOutputChars, cts.Token);

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

    private AgentSkillDescriptor? FindSkillByName(string name, CancellationToken cancellationToken)
    {
        var roots = GetEffectiveAgentSkillsRoots();
        var skills = DiscoverAgentSkills(roots, cancellationToken);
        return skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? skills.FirstOrDefault(s => s.FolderName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolvePathUnderRoot(string rootDirFull, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var root = EnsureTrailingSeparator(Path.GetFullPath(rootDirFull));
        var combined = Path.GetFullPath(Path.Combine(root, relativePath));

        return combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var sep = Path.DirectorySeparatorChar;
        if (!path.EndsWith(sep))
            return path + sep;
        return path;
    }

    private static Struct ToStruct(object obj)
    {
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        return JsonParser.Default.Parse<Struct>(json);
    }

    private static bool TryGetBool(object? v, bool fallback)
    {
        if (v is bool b) return b;
        if (v is string s && bool.TryParse(s, out var parsed)) return parsed;
        if (v is JsonElement je && je.ValueKind is JsonValueKind.True or JsonValueKind.False) return je.GetBoolean();
        return fallback;
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
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n)) { i = n; return true; }
            if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var s)) { i = s; return true; }
            return false;
        }
        return int.TryParse(v.ToString(), out i);
    }

    private static List<string> ParseStringArray(object? v)
    {
        var list = new List<string>();
        if (v == null)
            return list;

        if (v is string s)
        {
            // Best-effort: split by whitespace.
            foreach (var part in s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(part);
            return list;
        }

        if (v is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in je.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var str = el.GetString();
                    if (!string.IsNullOrWhiteSpace(str))
                        list.Add(str.Trim());
                }
                else
                {
                    var raw = el.ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                        list.Add(raw.Trim());
                }
            }
        }

        return list;
    }

    private static string ResolvePythonBin(string? pythonBinOverride)
    {
        if (!string.IsNullOrWhiteSpace(pythonBinOverride))
            return pythonBinOverride.Trim();

        var env = (Environment.GetEnvironmentVariable("AEVATAR_PYTHON_BIN") ?? string.Empty).Trim();
        if (env.Length > 0)
            return env;

        // Compatibility with existing SRA tool env var.
        env = (Environment.GetEnvironmentVariable("SRA_PYTHON_BIN") ?? string.Empty).Trim();
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
        var sb = new StringBuilder(capacity: Math.Min(maxChars, 16 * 1024));
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


