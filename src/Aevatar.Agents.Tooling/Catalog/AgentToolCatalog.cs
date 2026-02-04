using System.Collections.Concurrent;
using System.Reflection;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Aevatar.Agents.Tooling.Models;
using Aevatar.Agents.Tooling.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Tooling.Catalog;

public sealed class AgentToolCatalog
{
    private static readonly MethodInfo? InitializeToolsMethod =
        typeof(AIGAgentBase).GetMethod("InitializeToolsAsync", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly IHostEnvironment _env;
    private readonly IOptionsMonitor<AgentToolingOptions> _options;
    private readonly ILogger<AgentToolCatalog> _logger;
    private readonly ConcurrentDictionary<string, bool> _preparedAgents = new();

    public AgentToolCatalog(
        IHostEnvironment env,
        IOptionsMonitor<AgentToolingOptions> options,
        ILogger<AgentToolCatalog> logger)
    {
        _env = env;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<AgentToolItem>> GetToolsAsync(
        AIGAgentBase agent,
        CancellationToken ct)
    {
        if (agent == null) return Array.Empty<AgentToolItem>();
        await EnsureToolsPreparedAsync(agent, ct);

        var tools = await agent.GetRegisteredToolsAsync();
        return tools
            .Select(MapTool)
            .OrderBy(t => t.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<AgentToolItem>> GetToolsAsync(
        IAIGAgent agent,
        CancellationToken ct)
    {
        if (agent is not AIGAgentBase baseAgent)
            return Array.Empty<AgentToolItem>();
        return await GetToolsAsync(baseAgent, ct);
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
        AIGAgentBase agent,
        string filePath,
        CancellationToken ct)
    {
        if (agent == null) return null;
        var resolved = ResolvePath(filePath);
        if (string.IsNullOrWhiteSpace(resolved))
            return null;

        if (!IsAllowedDotNetFile(resolved))
            return null;

        var tool = await DotNetFileSkillTool.LoadFromFileAsync(resolved, _logger, ct);
        await agent.RegisterDotNetFileSkillAsync(resolved, ct);

        return new DotNetToolFileItem(
            Name: tool.Name,
            Description: tool.Description,
            FilePath: resolved,
            Tags: tool.Tags.ToList(),
            IsDangerous: tool.CreateToolDefinition(new ToolContext()).IsDangerous);
    }

    public async Task<DotNetToolFileItem?> RegisterDotNetFileAsync(
        IAIGAgent agent,
        string filePath,
        CancellationToken ct)
    {
        if (agent is not AIGAgentBase baseAgent)
            return null;
        return await RegisterDotNetFileAsync(baseAgent, filePath, ct);
    }

    private async Task EnsureToolsPreparedAsync(AIGAgentBase agent, CancellationToken ct)
    {
        if (!_preparedAgents.TryAdd(agent.Id, true))
            return;
        if (InitializeToolsMethod == null)
            return;

        try
        {
            var task = InitializeToolsMethod.Invoke(agent, new object[] { ct }) as Task;
            if (task != null)
                await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "InitializeToolsAsync failed for {AgentId}", agent.Id);
        }
    }

    private AgentToolItem MapTool(ToolDefinition tool)
    {
        var source = ResolveSource(tool);
        return new AgentToolItem(
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
            .Select(p => p!)
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

    private string? ResolvePath(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return null;

        var normalized = trimmed.Replace('\\', '/');
        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                return Path.GetFullPath(Path.Combine(home, normalized[2..]));
        }

        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, trimmed));
    }

    private static bool LooksLikeDotNetToolFile(string file)
        => file.EndsWith("Tool.cs", StringComparison.OrdinalIgnoreCase)
           || file.EndsWith("Skill.cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedDotNetFile(string file)
    {
        if (!File.Exists(file))
            return false;
        if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }
}
