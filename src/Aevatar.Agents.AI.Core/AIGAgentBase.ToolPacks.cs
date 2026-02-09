using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.ToolPacks;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    private IReadOnlyList<IAevatarToolPack> _toolPacks = Array.Empty<IAevatarToolPack>();

    internal void InjectToolPacks(IEnumerable<IAevatarToolPack>? packs)
    {
        if (packs == null)
        {
            _toolPacks = Array.Empty<IAevatarToolPack>();
            return;
        }

        _toolPacks = packs
            .Where(p => p != null)
            .GroupBy(p => p.PackName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    internal async Task RegisterYamlToolPacksAsync(AgentYamlConfig yaml, CancellationToken ct)
    {
        if (yaml?.Tools is not { Count: > 0 })
            return;

        if (_toolPacks.Count == 0)
            return;

        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in yaml.Tools)
        {
            var name = (raw ?? string.Empty).Trim();
            if (name.Length > 0)
                requested.Add(name);
        }

        if (requested.Count == 0)
            return;

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var registered = await GetRegisteredToolsAsync();
            foreach (var tool in registered)
                existing.Add(tool.Name);
        }
        catch
        {
            // Best-effort: missing tool list should not block pack registration.
        }

        var context = new AevatarToolPackContext(
            Configuration: InternalHostConfiguration,
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Logger: Logger ?? NullLogger.Instance);

        foreach (var pack in _toolPacks)
        {
            foreach (var toolName in requested)
            {
                if (existing.Contains(toolName))
                    continue;

                if (!pack.TryCreateTool(toolName, context, out var tool))
                    continue;

                await RegisterToolAsync(tool, Logger, ct);
                existing.Add(tool.Name);
            }
        }
    }
}
