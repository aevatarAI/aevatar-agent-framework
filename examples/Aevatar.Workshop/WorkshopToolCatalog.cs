using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Microsoft.Extensions.Options;

namespace Aevatar.Workshop;

public sealed class WorkshopToolCatalog
{
    private static readonly MethodInfo? InitializeToolsMethod =
        typeof(AIGAgentBase).GetMethod("InitializeToolsAsync", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly IHostEnvironment _env;
    private readonly IOptionsMonitor<WorkshopOptions> _options;
    private readonly ILogger<WorkshopToolCatalog> _logger;
    private readonly ConcurrentDictionary<string, bool> _preparedSessions = new();

    public WorkshopToolCatalog(
        IHostEnvironment env,
        IOptionsMonitor<WorkshopOptions> options,
        ILogger<WorkshopToolCatalog> logger)
    {
        _env = env;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkshopToolItem>> GetToolsAsync(
        SessionStore.SessionEntry session,
        CancellationToken ct)
    {
        await EnsureToolsPreparedAsync(session, ct);

        var tools = await session.Agent.GetRegisteredToolsAsync();
        return tools
            .Select(MapTool)
            .OrderBy(t => t.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<DotNetToolFileItem>> ListDotNetFilesAsync(CancellationToken ct)
    {
        var result = new List<DotNetToolFileItem>();
        var limit = Math.Clamp(_options.CurrentValue.DotNetToolMaxFiles, 1, 512);
        foreach (var dir in ResolveDotNetDirectories())
        {
            if (!Directory.Exists(dir))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (result.Count >= limit)
                    break;
                if (!LooksLikeDotNetToolFile(file))
                    continue;

                try
                {
                    var tool = await DotNetFileSkillTool.LoadFromFileAsync(file, _logger, ct);
                    result.Add(new DotNetToolFileItem(
                        Name: tool.Name,
                        Description: tool.Description,
                        FilePath: Path.GetFullPath(file),
                        Tags: tool.Tags.ToList(),
                        IsDangerous: tool.CreateToolDefinition(new ToolContext()).IsDangerous));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse dotnet tool file {File}", file);
                }
            }
        }

        return result
            .DistinctBy(x => x.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DotNetToolFileItem?> RegisterDotNetFileAsync(
        SessionStore.SessionEntry session,
        string filePath,
        CancellationToken ct)
    {
        var resolved = ResolvePath(filePath);
        if (string.IsNullOrWhiteSpace(resolved))
            return null;

        if (!IsAllowedDotNetFile(resolved))
            return null;

        var tool = await DotNetFileSkillTool.LoadFromFileAsync(resolved, _logger, ct);
        await session.Agent.RegisterDotNetFileSkillAsync(resolved, ct);

        return new DotNetToolFileItem(
            Name: tool.Name,
            Description: tool.Description,
            FilePath: resolved,
            Tags: tool.Tags.ToList(),
            IsDangerous: tool.CreateToolDefinition(new ToolContext()).IsDangerous);
    }

    private async Task EnsureToolsPreparedAsync(SessionStore.SessionEntry session, CancellationToken ct)
    {
        if (!_preparedSessions.TryAdd(session.SessionId, true))
            return;

        if (InitializeToolsMethod == null)
            return;

        try
        {
            var task = InitializeToolsMethod.Invoke(session.Agent, new object[] { ct }) as Task;
            if (task != null)
                await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "InitializeToolsAsync failed for {AgentId}", session.AgentId);
        }
    }

    private WorkshopToolItem MapTool(ToolDefinition tool)
    {
        var source = ResolveSource(tool);
        return new WorkshopToolItem(
            Name: tool.Name,
            Description: tool.Description,
            Source: source,
            Category: tool.Category.ToString(),
            IsDangerous: tool.IsDangerous,
            RequiresConfirmation: tool.RequiresConfirmation,
            Tags: tool.Tags?.ToList() ?? new List<string>());
    }

    private static string ResolveSource(ToolDefinition tool)
    {
        if (tool.Metadata != null &&
            tool.Metadata.TryGetValue("Source", out var raw) &&
            raw != null &&
            !string.IsNullOrWhiteSpace(raw.ToString()))
        {
            return raw.ToString() ?? "MCP";
        }

        if (tool.Tags.Any(t => string.Equals(t, "dotnet", StringComparison.OrdinalIgnoreCase)))
            return "DotNetFile";

        if (tool.Tags.Any(t => string.Equals(t, "skills", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(t, "agent-skills", StringComparison.OrdinalIgnoreCase)))
            return "Skills";

        return tool.Category == ToolCategory.Core ? "Core" : "Custom";
    }

    private IReadOnlyList<string> ResolveDotNetDirectories()
    {
        var raw = _options.CurrentValue.DotNetToolDirectories ?? new List<string>();
        var list = raw
            .Select(ResolvePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count == 0)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                list.Add(Path.Combine(home, ".aevatar", "tools", "dotnet"));
                list.Add(Path.Combine(home, ".aevatar", "dotnet-tools"));
            }

            list.Add(Path.Combine(
                _env.ContentRootPath,
                "apps",
                "Aevatar.Trading",
                "src",
                "Aevatar.Trade",
                "Tools",
                "DotNetSkills"));
        }

        return list;
    }

    private string ResolvePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var path = raw.Trim();
        if (path.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path.TrimStart('~').TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        if (!Path.IsPathRooted(path))
            path = Path.Combine(_env.ContentRootPath, path);

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool IsAllowedDotNetFile(string filePath)
    {
        var full = ResolvePath(filePath);
        if (string.IsNullOrWhiteSpace(full))
            return false;

        foreach (var dir in ResolveDotNetDirectories())
        {
            if (!string.IsNullOrWhiteSpace(dir) &&
                full.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeDotNetToolFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buf = new byte[16 * 1024];
            var read = fs.Read(buf, 0, buf.Length);
            if (read <= 0) return false;
            var head = System.Text.Encoding.UTF8.GetString(buf, 0, read);
            return head.Contains("/*aevatar_tool", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

public sealed record WorkshopToolItem(
    string Name,
    string Description,
    string Source,
    string Category,
    bool IsDangerous,
    bool RequiresConfirmation,
    IReadOnlyList<string> Tags);

public sealed record DotNetToolFileItem(
    string Name,
    string Description,
    string FilePath,
    IReadOnlyList<string> Tags,
    bool IsDangerous);
