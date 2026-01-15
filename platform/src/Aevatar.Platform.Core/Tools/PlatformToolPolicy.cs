using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Platform.Core.Config;

namespace Aevatar.Platform.Core.Tools;

// ============================================================
//  PlatformToolPolicy
//
//  目标：
//  - 统一工具安全策略（dangerous/internal/allowlist/timeout）
//  - 提供路径/命令 allowlist 校验（供 shell/filesystem 工具调用）
// ============================================================
public sealed class PlatformToolPolicy
{
    private static readonly char[] ShellMetaChars = [';', '&', '|', '>', '<', '\n', '\r'];

    private static readonly IReadOnlyDictionary<string, ToolPolicyPreset> Presets =
        new Dictionary<string, ToolPolicyPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["coding_default"] = new ToolPolicyPreset(
                Name: "coding_default",
                AllowDangerousTools: true,
                AllowInternalTools: true,
                EnforceAllowlist: false,
                AllowedTools: Array.Empty<string>()),
            ["vibe_default"] = new ToolPolicyPreset(
                Name: "vibe_default",
                AllowDangerousTools: false,
                AllowInternalTools: true,
                EnforceAllowlist: false,
                AllowedTools: Array.Empty<string>()),
            ["writing_default"] = new ToolPolicyPreset(
                Name: "writing_default",
                AllowDangerousTools: false,
                AllowInternalTools: true,
                EnforceAllowlist: false,
                AllowedTools: Array.Empty<string>())
        };

    private readonly HashSet<string> _allowedTools;
    private readonly HashSet<string> _allowedCommands;
    private readonly List<string> _allowedPathRoots;

    private PlatformToolPolicy(
        string preset,
        bool allowDangerousTools,
        bool allowInternalTools,
        bool enforceAllowlist,
        HashSet<string> allowedTools,
        HashSet<string> allowedCommands,
        List<string> allowedPathRoots,
        int shellTimeoutSeconds)
    {
        Preset = preset;
        AllowDangerousTools = allowDangerousTools;
        AllowInternalTools = allowInternalTools;
        EnforceAllowlist = enforceAllowlist;
        _allowedTools = allowedTools;
        _allowedCommands = allowedCommands;
        _allowedPathRoots = allowedPathRoots;
        ShellTimeoutSeconds = shellTimeoutSeconds;
    }

    public string Preset { get; }

    public bool AllowDangerousTools { get; }

    public bool AllowInternalTools { get; }

    public bool EnforceAllowlist { get; }

    public int ShellTimeoutSeconds { get; }

    public IReadOnlyCollection<string> AllowedTools => _allowedTools;

    public IReadOnlyCollection<string> AllowedCommands => _allowedCommands;

    public IReadOnlyCollection<string> AllowedPathRoots => _allowedPathRoots;

    public static PlatformToolPolicy Create(AevatarConfig config, string? preset)
    {
        var selected = ResolvePreset(preset);
        var tools = NormalizeTokens(selected.AllowedTools);
        var commands = NormalizeTokens(config.Tools.Shell.AllowedCommands);
        var paths = NormalizePaths(config.Tools.FileSystem.AllowedPaths);
        var timeout = NormalizeTimeoutSeconds(config.Tools.Shell.TimeoutSeconds);

        return new PlatformToolPolicy(
            preset: selected.Name,
            allowDangerousTools: selected.AllowDangerousTools,
            allowInternalTools: selected.AllowInternalTools,
            enforceAllowlist: selected.EnforceAllowlist,
            allowedTools: tools,
            allowedCommands: commands,
            allowedPathRoots: paths,
            shellTimeoutSeconds: timeout);
    }

    public void ApplyToAgent(AIGAgentBase agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        agent.AllowDangerousTools = AllowDangerousTools;
        agent.AllowInternalTools = AllowInternalTools;

        if (EnforceAllowlist)
        {
            agent.SetFixedToolAllowlist(_allowedTools.Count == 0 ? null : _allowedTools);
        }
    }

    public ToolPolicyDecision EvaluateTool(ToolDefinition tool)
    {
        if (tool == null)
            return ToolPolicyDecision.Deny("tool_null");

        if (string.IsNullOrWhiteSpace(tool.Name))
            return ToolPolicyDecision.Deny("tool_name_empty");

        if (EnforceAllowlist && _allowedTools.Count > 0 && !_allowedTools.Contains(tool.Name))
            return ToolPolicyDecision.Deny("tool_not_in_allowlist");

        if (!AllowInternalTools && tool.RequiresInternalAccess)
            return ToolPolicyDecision.Deny("internal_tools_disabled");

        if (!AllowDangerousTools && (tool.IsDangerous || tool.RequiresConfirmation))
            return ToolPolicyDecision.Deny("dangerous_tools_disabled");

        return ToolPolicyDecision.Allow();
    }

    public IReadOnlyCollection<string> FilterAllowlist(IEnumerable<string> toolNames)
    {
        if (!EnforceAllowlist || _allowedTools.Count == 0)
            return NormalizeTokens(toolNames);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in toolNames ?? Array.Empty<string>())
        {
            var token = (name ?? string.Empty).Trim();
            if (token.Length == 0)
                continue;

            if (_allowedTools.Contains(token))
                result.Add(token);
        }

        return result;
    }

    public bool TryValidatePath(string path, out string? fullPath, out string reason)
    {
        fullPath = null;
        reason = string.Empty;

        if (_allowedPathRoots.Count == 0)
        {
            reason = "path_allowlist_empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "path_empty";
            return false;
        }

        var normalized = TryNormalizePath(path, out var error);
        if (normalized == null)
        {
            reason = $"path_invalid:{error}";
            return false;
        }

        if (!IsPathAllowed(normalized))
        {
            reason = "path_not_allowed";
            return false;
        }

        fullPath = normalized;
        return true;
    }

    public bool TryValidateCommand(string command, out string reason)
    {
        reason = string.Empty;

        if (_allowedCommands.Count == 0)
        {
            reason = "command_allowlist_empty";
            return false;
        }

        var trimmed = (command ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            reason = "command_empty";
            return false;
        }

        if (ContainsShellMeta(trimmed))
        {
            reason = "command_contains_shell_meta";
            return false;
        }

        var token = ExtractFirstToken(trimmed);
        if (string.IsNullOrWhiteSpace(token))
        {
            reason = "command_token_empty";
            return false;
        }

        var name = Path.GetFileName(token);
        if (!_allowedCommands.Contains(name))
        {
            reason = "command_not_allowed";
            return false;
        }

        return true;
    }

    public bool TryGetToolTimeout(string toolName, out TimeSpan timeout)
    {
        timeout = default;
        if (ShellTimeoutSeconds <= 0)
            return false;

        if (!IsShellToolName(toolName))
            return false;

        timeout = TimeSpan.FromSeconds(ShellTimeoutSeconds);
        return true;
    }

    private bool IsPathAllowed(string fullPath)
    {
        foreach (var root in _allowedPathRoots)
        {
            if (IsWithinRoot(root, fullPath))
                return true;
        }

        return false;
    }

    private static ToolPolicyPreset ResolvePreset(string? preset)
    {
        var key = (preset ?? string.Empty).Trim();
        if (key.Length == 0)
            key = "coding_default";

        return Presets.TryGetValue(key, out var selected)
            ? selected
            : Presets["coding_default"];
    }

    private static HashSet<string> NormalizeTokens(IEnumerable<string>? values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values == null)
            return set;

        foreach (var item in values)
        {
            var token = (item ?? string.Empty).Trim();
            if (token.Length > 0)
                set.Add(token);
        }

        return set;
    }

    private static List<string> NormalizePaths(IEnumerable<string>? values)
    {
        var list = new List<string>();
        if (values == null)
            return list;

        foreach (var item in values)
        {
            var normalized = TryNormalizePath(item ?? string.Empty, out _);
            if (normalized == null)
                continue;

            var exists = false;
            foreach (var existing in list)
            {
                if (string.Equals(existing, normalized, StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                list.Add(normalized);
        }

        return list;
    }

    private static string? TryNormalizePath(string path, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "empty";
            return null;
        }

        try
        {
            var expanded = ExpandHome(path.Trim());
            return Path.GetFullPath(expanded);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static string ExpandHome(string path)
    {
        if (!path.StartsWith("~", StringComparison.Ordinal))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.Length == 1)
            return home;

        if (path[1] == Path.DirectorySeparatorChar || path[1] == Path.AltDirectorySeparatorChar)
            return Path.Combine(home, path[2..]);

        return path;
    }

    private static bool IsWithinRoot(string root, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fullPath))
            return false;

        var basePath = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = basePath + Path.DirectorySeparatorChar;

        return string.Equals(fullPath, basePath, StringComparison.Ordinal)
               || fullPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string? ExtractFirstToken(string command)
    {
        var text = command.Trim();
        if (text.Length == 0)
            return null;

        if (text[0] is '"' or '\'')
        {
            var quote = text[0];
            var end = text.IndexOf(quote, 1);
            return end > 1 ? text[1..end] : null;
        }

        var index = 0;
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index == 0 ? null : text[..index];
    }

    private static bool ContainsShellMeta(string command)
        => command.IndexOfAny(ShellMetaChars) >= 0;

    private static bool IsShellToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        return string.Equals(toolName, "shell", StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, "bash", StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, "terminal", StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, "run_shell", StringComparison.OrdinalIgnoreCase);
    }

    private static int NormalizeTimeoutSeconds(int value)
        => Math.Clamp(value, 0, 3600);

    private sealed record ToolPolicyPreset(
        string Name,
        bool AllowDangerousTools,
        bool AllowInternalTools,
        bool EnforceAllowlist,
        IReadOnlyCollection<string> AllowedTools);
}

public sealed record ToolPolicyDecision(bool Allowed, string Reason)
{
    public static ToolPolicyDecision Allow() => new(true, string.Empty);

    public static ToolPolicyDecision Deny(string reason)
        => new(false, reason ?? string.Empty);
}


