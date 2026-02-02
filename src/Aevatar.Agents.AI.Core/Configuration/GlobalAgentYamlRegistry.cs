using Aevatar.Agents.AI.Abstractions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Configuration;

// ============================================================
//  GlobalAgentYamlRegistry
//
//  What:
//  - Discover + load agent YAML configs from:
//      1) repo-local: <repo>/aevatar/agents/{role}.yaml
//      2) global user: ~/.aevatar/agents/{role}.yaml
//
//  Why:
//  - Cross-app convention: role-driven agents can be configured globally and reused
//    by any Aevatar-based application (SRA, CognitiveMesh, Maker, ...).
//
//  Notes:
//  - Best-effort + cached: missing dir/files just mean "no configs".
//  - We intentionally keep the registry "raw config" only; applying config to an agent
//    is done by AgentYamlConfigApplier.
// ============================================================

public sealed class GlobalAgentYamlRegistry
{
    private static readonly TimeSpan ScanMinInterval = TimeSpan.FromSeconds(2);

    private readonly AgentYamlConfigLoader _loader;
    private readonly ILogger<GlobalAgentYamlRegistry> _logger;

    private readonly object _lock = new();
    private DateTimeOffset _lastScanUtc = DateTimeOffset.MinValue;
    private HashSet<string> _cachedRoles = new(StringComparer.OrdinalIgnoreCase);

    public GlobalAgentYamlRegistry(ILogger<GlobalAgentYamlRegistry> logger, AgentYamlConfigLoader? loader = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loader = loader ?? new AgentYamlConfigLoader();
    }

    /// <summary>
    /// Normalize a role key so it can be used as a stable file name and dictionary key.
    /// </summary>
    public static string NormalizeRoleKey(string? role)
    {
        var s = (role ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length == 0) return string.Empty;

        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        return sb.ToString();
    }

    public IReadOnlySet<string> GetKnownRoles()
    {
        EnsureScanned();
        lock (_lock)
            return new HashSet<string>(_cachedRoles, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasRole(string? role)
    {
        var key = NormalizeRoleKey(role);
        if (key.Length == 0) return false;
        EnsureScanned();
        lock (_lock)
            return _cachedRoles.Contains(key);
    }

    public AgentYamlConfig? TryLoad(string? role)
    {
        var key = NormalizeRoleKey(role);
        if (key.Length == 0) return null;

        var local = ResolveLocalConfigPath(key);
        if (!string.IsNullOrWhiteSpace(local))
        {
            var localCfg = _loader.TryLoadFromFile(local);
            if (localCfg != null)
                return localCfg;
        }

        var path = AgentYamlConfigLoader.GetConfigFilePath(key);
        return _loader.TryLoadFromFile(path);
    }

    public string? TryGetProvider(string? role)
    {
        var cfg = TryLoad(role);
        if (cfg == null) return null;

        var p = (cfg.Provider ?? string.Empty).Trim();
        if (p.Length == 0 || string.Equals(p, "default", StringComparison.OrdinalIgnoreCase))
            return null;
        return p;
    }

    private void EnsureScanned()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            if (_lastScanUtc != DateTimeOffset.MinValue && now - _lastScanUtc < ScanMinInterval)
                return;

            _cachedRoles = ScanRolesBestEffort();
            _lastScanUtc = now;
        }
    }

    private HashSet<string> ScanRolesBestEffort()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var dir in ResolveScanDirectories())
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (var f in Directory.EnumerateFiles(dir, "*.yaml", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    var key = NormalizeRoleKey(name);
                    if (key.Length > 0)
                        set.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to scan agent YAML directories (best-effort).");
        }

        return set;
    }

    private static IReadOnlyList<string> ResolveScanDirectories()
    {
        var list = new List<string>();
        var local = ResolveLocalConfigDirectory();
        if (!string.IsNullOrWhiteSpace(local))
            list.Add(local!);

        var global = AgentYamlConfigLoader.GetDefaultConfigDirectory();
        if (!string.IsNullOrWhiteSpace(global))
            list.Add(global);

        return list;
    }

    private static string? ResolveLocalConfigPath(string roleKey)
    {
        var dir = ResolveLocalConfigDirectory();
        if (string.IsNullOrWhiteSpace(dir))
            return null;

        return Path.Combine(dir, $"{roleKey}.yaml");
    }

    private static string? ResolveLocalConfigDirectory()
    {
        var root = TryFindRepoRoot();
        if (string.IsNullOrWhiteSpace(root))
            return null;

        return Path.Combine(root, "aevatar", "agents");
    }

    private static string? TryFindRepoRoot()
    {
        var roots = new[]
        {
            new DirectoryInfo(Directory.GetCurrentDirectory()),
            new DirectoryInfo(AppContext.BaseDirectory)
        };

        foreach (var start in roots)
        {
            var dir = start;
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
            {
                dir = dir.Parent;
            }

            if (dir != null)
                return dir.FullName;
        }

        return null;
    }
}


