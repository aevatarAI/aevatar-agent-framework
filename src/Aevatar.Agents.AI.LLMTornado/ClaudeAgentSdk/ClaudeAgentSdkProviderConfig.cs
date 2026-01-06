using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;

namespace Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;

/// <summary>
/// Claude Agent SDK provider config (parsed from <see cref="LLMProviderConfig.ProviderSpecificSettings" />).
///
/// 设计目标：
/// - 输入容忍：支持 string/int/bool/JsonElement/list/dictionary 等常见绑定形态
/// - 安全默认值：不隐式放大权限；不记录 secrets
/// - 失败可自助修复：缺失关键字段时给出明确错误
/// </summary>
public sealed class ClaudeAgentSdkProviderConfig
{
    // Keep defaults conservative but practical.
    private const int DefaultMaxOutputChars = 256 * 1024; // 256 KB
    private const int MinTimeoutMs = 1_000;
    private const int MaxTimeoutMs = 60 * 60 * 1_000; // 1 hour
    private const int MinMaxOutputChars = 4 * 1024;
    private const int MaxMaxOutputChars = 4 * 1024 * 1024; // 4 MB

    public required string RunnerCommand { get; init; }
    public required IReadOnlyList<string> RunnerArgs { get; init; }

    public required string WorkingDirectory { get; init; }
    public required string ProjectRoot { get; init; }

    public IReadOnlyList<string> Plugins { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SettingSources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();
    public string? PermissionMode { get; init; }

    public IReadOnlyDictionary<string, string> ExtraEnvironment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public int TimeoutMs { get; init; }
    public int MaxOutputChars { get; init; }

    public static ClaudeAgentSdkProviderConfig From(LLMProviderConfig providerConfig)
    {
        ArgumentNullException.ThrowIfNull(providerConfig);

        // ------------------------------------------------------------
        // Normalize settings with case-insensitive keys
        // ------------------------------------------------------------
        var settings = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (providerConfig.ProviderSpecificSettings is { Count: > 0 })
        {
            foreach (var (k, v) in providerConfig.ProviderSpecificSettings)
            {
                if (!string.IsNullOrWhiteSpace(k))
                {
                    settings[k.Trim()] = v;
                }
            }
        }

        // ------------------------------------------------------------
        // Required: runner command + args
        // ------------------------------------------------------------
        var runnerCommand = GetString(settings,
            "runnerCommand", "runner_command",
            "command", "cmd");

        var runnerArgs = GetStringList(settings,
            "runnerArgs", "runner_args",
            "args", "arguments");

        if (string.IsNullOrWhiteSpace(runnerCommand) || runnerArgs.Count == 0)
        {
            var name = string.IsNullOrWhiteSpace(providerConfig.Name) ? "(unnamed)" : providerConfig.Name;
            throw new InvalidOperationException(
                $"Provider '{name}' (ProviderType=claude_agent_sdk) requires ProviderSpecificSettings.runnerCommand and runnerArgs. " +
                "Example: runnerCommand='node', runnerArgs=['/abs/claude_agent_sdk_runner.mjs'].");
        }

        // ------------------------------------------------------------
        // Optional: workingDirectory / projectRoot
        // - workingDirectory controls process cwd
        // - projectRoot is forwarded to Claude Agent SDK as project setting source (loads .claude/*)
        // ------------------------------------------------------------
        var workingDirectory = GetString(settings,
            "workingDirectory", "working_directory",
            "workdir", "cwd");

        var projectRoot = GetString(settings,
            "projectRoot", "project_root");

        // Default: prefer projectRoot as cwd; otherwise current directory.
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            workingDirectory = !string.IsNullOrWhiteSpace(projectRoot)
                ? projectRoot
                : Environment.CurrentDirectory;
        }

        // Default: projectRoot falls back to workingDirectory.
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            projectRoot = workingDirectory;
        }

        workingDirectory = NormalizePathOrThrow(workingDirectory, "workingDirectory");
        projectRoot = NormalizePathOrThrow(projectRoot, "projectRoot");

        // ------------------------------------------------------------
        // Optional: plugins / settingSources / permissions
        // ------------------------------------------------------------
        var plugins = GetStringList(settings, "plugins", "pluginPaths", "plugin_paths");
        var settingSources = GetStringList(settings, "settingSources", "setting_sources");
        var allowedTools = GetStringList(settings, "allowedTools", "allowed_tools");
        var permissionMode = GetString(settings, "permissionMode", "permission_mode");

        // ------------------------------------------------------------
        // Optional: extra env
        // NOTE: keep best-effort; do not validate or log values (may contain secrets).
        // ------------------------------------------------------------
        var extraEnv = GetStringDictionary(settings, "env", "environment", "extraEnv", "extra_env");

        // ------------------------------------------------------------
        // Timeout / output caps
        // ------------------------------------------------------------
        var timeoutMs = GetInt(settings, "timeoutMs", "timeout_ms", "timeoutMilliseconds", "timeout_milliseconds")
                        ?? providerConfig.TimeoutMilliseconds;
        timeoutMs = Math.Clamp(timeoutMs, MinTimeoutMs, MaxTimeoutMs);

        var maxOutputChars = GetInt(settings, "maxOutputChars", "max_output_chars")
                             ?? DefaultMaxOutputChars;
        maxOutputChars = Math.Clamp(maxOutputChars, MinMaxOutputChars, MaxMaxOutputChars);

        return new ClaudeAgentSdkProviderConfig
        {
            RunnerCommand = runnerCommand.Trim(),
            RunnerArgs = runnerArgs,
            WorkingDirectory = workingDirectory,
            ProjectRoot = projectRoot,
            Plugins = plugins,
            SettingSources = settingSources,
            AllowedTools = allowedTools,
            PermissionMode = string.IsNullOrWhiteSpace(permissionMode) ? null : permissionMode.Trim(),
            ExtraEnvironment = extraEnv,
            TimeoutMs = timeoutMs,
            MaxOutputChars = maxOutputChars
        };
    }

    private static string NormalizePathOrThrow(string input, string fieldName)
    {
        try
        {
            return Path.GetFullPath(input.Trim());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid path for '{fieldName}': '{input}'", ex);
        }
    }

    // ============================================================
    //  Settings readers (best-effort, tolerate binder shapes)
    // ============================================================

    private static string? GetString(Dictionary<string, object> settings, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!settings.TryGetValue(k, out var raw))
                continue;

            var s = AsString(raw);
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        return null;
    }

    private static int? GetInt(Dictionary<string, object> settings, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!settings.TryGetValue(k, out var raw))
                continue;

            if (TryAsInt(raw, out var v))
            {
                return v;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetStringList(Dictionary<string, object> settings, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!settings.TryGetValue(k, out var raw))
                continue;

            var list = AsStringList(raw);
            if (list.Count > 0)
            {
                return list;
            }
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyDictionary<string, string> GetStringDictionary(
        Dictionary<string, object> settings,
        params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!settings.TryGetValue(k, out var raw))
                continue;

            var dict = AsStringDictionary(raw);
            if (dict.Count > 0)
            {
                return dict;
            }
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string? AsString(object? raw)
    {
        if (raw == null)
            return null;

        return raw switch
        {
            string s => s,
            JsonElement el => el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            },
            _ => raw.ToString()
        };
    }

    private static bool TryAsInt(object? raw, out int value)
    {
        value = default;
        if (raw == null)
            return false;

        switch (raw)
        {
            case int i:
                value = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                value = (int)l;
                return true;
            case JsonElement el when el.ValueKind == JsonValueKind.Number:
                return el.TryGetInt32(out value) || (el.TryGetInt64(out var ll) &&
                                                     ll is >= int.MinValue and <= int.MaxValue &&
                                                     (value = (int)ll) >= int.MinValue);
            default:
            {
                var s = AsString(raw);
                return int.TryParse(s, out value);
            }
        }
    }

    private static IReadOnlyList<string> AsStringList(object? raw)
    {
        if (raw == null)
            return Array.Empty<string>();

        // 1) JsonElement array
        if (raw is JsonElement el && el.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in el.EnumerateArray())
            {
                var s = AsString(item);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s.Trim());
                }
            }
            return list;
        }

        // 2) string[] / List<string> / IEnumerable<string>
        if (raw is IEnumerable<string> stringEnumerable)
        {
            var list = stringEnumerable
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToList();
            return list.Count > 0 ? list : Array.Empty<string>();
        }

        // 3) IEnumerable<object>
        if (raw is System.Collections.IEnumerable objEnumerable && raw is not string)
        {
            var list = new List<string>();
            foreach (var item in objEnumerable)
            {
                var s = AsString(item);
                if (!string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s.Trim());
                }
            }
            return list.Count > 0 ? list : Array.Empty<string>();
        }

        // 4) single string -> one item (do not split; avoid quoting pitfalls)
        var single = AsString(raw);
        if (!string.IsNullOrWhiteSpace(single))
        {
            return new[] { single.Trim() };
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyDictionary<string, string> AsStringDictionary(object? raw)
    {
        if (raw == null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1) JsonElement object
        if (raw is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in el.EnumerateObject())
            {
                var v = AsString(prop.Value);
                if (!string.IsNullOrWhiteSpace(prop.Name) && v != null)
                {
                    dict[prop.Name.Trim()] = v;
                }
            }
            return dict;
        }

        // 2) Dictionary<string, string>
        if (raw is IDictionary<string, string> ss)
        {
            return new Dictionary<string, string>(ss, StringComparer.OrdinalIgnoreCase);
        }

        // 3) Dictionary<string, object>
        if (raw is IDictionary<string, object> so)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in so)
            {
                if (string.IsNullOrWhiteSpace(k))
                    continue;

                var s = AsString(v);
                if (s != null)
                {
                    dict[k.Trim()] = s;
                }
            }
            return dict;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}


