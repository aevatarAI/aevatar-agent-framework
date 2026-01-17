using System.Text.Json;

namespace Aevatar.Platform.Core.Config;

// ============================================================
//  AevatarConfigSerializer
//
//  说明：
//  - 直接从 Map/JsonElement 解析到强类型
// ============================================================
public static class AevatarConfigSerializer
{
    public static AevatarConfig FromJsonElement(JsonElement element)
    {
        var map = ConfigMapNormalizer.Normalize(element);
        return FromMap(map);
    }

    public static AevatarConfig FromMap(Dictionary<string, object?>? map)
    {
        if (map == null)
            return new AevatarConfig();

        if (TryGetMap(map, out var aevatar, ConfigKeyAliases.Root))
            map = aevatar;

        var config = new AevatarConfig
        {
            Version = GetString(map, ConfigKeyAliases.Version) ?? "1.0"
        };

        if (TryGetMap(map, out var models, ConfigKeyAliases.Models))
            ApplyModels(config.Models, models);

        if (TryGetMap(map, out var agents, ConfigKeyAliases.Agents))
            ApplyAgents(config.Agents, agents);

        if (TryGetMap(map, out var tools, ConfigKeyAliases.Tools))
            ApplyTools(config.Tools, tools);

        if (TryGetMap(map, out var ui, ConfigKeyAliases.Ui))
            ApplyUi(config.Ui, ui);

        if (TryGetMap(map, out var logging, ConfigKeyAliases.Logging))
            ApplyLogging(config.Logging, logging);

        return config;
    }

    private static void ApplyModels(ModelsConfig target, Dictionary<string, object?> map)
    {
        var defaultProvider = GetString(map, ConfigKeyAliases.DefaultProvider);
        if (!string.IsNullOrWhiteSpace(defaultProvider))
            target.DefaultProvider = defaultProvider;

        var defaultModel = GetString(map, ConfigKeyAliases.DefaultModel);
        if (!string.IsNullOrWhiteSpace(defaultModel))
            target.DefaultModel = defaultModel;

        if (!TryGetMap(map, out var providers, ConfigKeyAliases.Providers))
            return;

        foreach (var (name, value) in providers)
        {
            var providerName = (name ?? string.Empty).Trim();
            if (providerName.Length == 0)
                continue;

            var providerMap = ConfigMapNormalizer.Normalize(value);
            if (providerMap == null)
            {
                target.Providers.TryAdd(providerName, new ProviderConfig());
                continue;
            }

            if (!target.Providers.TryGetValue(providerName, out var entry))
                entry = new ProviderConfig();

            var model = GetString(providerMap, ConfigKeyAliases.ProviderModel);
            if (!string.IsNullOrWhiteSpace(model))
                entry.DefaultModel = model;

            var endpoint = GetString(providerMap, ConfigKeyAliases.ProviderEndpoint);
            if (!string.IsNullOrWhiteSpace(endpoint))
                entry.Endpoint = endpoint;

            target.Providers[providerName] = entry;
        }
    }

    private static void ApplyAgents(AgentsConfig target, Dictionary<string, object?> map)
    {
        var workflow = GetString(map, ConfigKeyAliases.DefaultWorkflow);
        if (!string.IsNullOrWhiteSpace(workflow))
            target.DefaultWorkflow = workflow;

        var profile = GetString(map, ConfigKeyAliases.DefaultProfile);
        if (!string.IsNullOrWhiteSpace(profile))
            target.DefaultProfile = profile;

        var limit = GetInt(map, ConfigKeyAliases.ParallelLimit);
        if (limit.HasValue)
            target.ParallelLimit = limit.Value;
    }

    private static void ApplyTools(ToolsConfig target, Dictionary<string, object?> map)
    {
        if (TryGetMap(map, out var shell, ConfigKeyAliases.Shell))
        {
            var commands = GetStringList(shell, ConfigKeyAliases.AllowedCommands);
            if (commands.Count > 0)
                target.Shell.AllowedCommands = commands;

            var timeout = GetInt(shell, ConfigKeyAliases.TimeoutSeconds);
            if (timeout.HasValue)
                target.Shell.TimeoutSeconds = timeout.Value;
        }

        if (TryGetMap(map, out var fs, ConfigKeyAliases.FileSystem))
        {
            var paths = GetStringList(fs, ConfigKeyAliases.AllowedPaths);
            if (paths.Count > 0)
                target.FileSystem.AllowedPaths = paths;
        }
    }

    private static void ApplyUi(UiConfig target, Dictionary<string, object?> map)
    {
        var theme = GetString(map, ConfigKeyAliases.Theme);
        if (!string.IsNullOrWhiteSpace(theme))
            target.Theme = theme;

        var editor = GetString(map, ConfigKeyAliases.Editor);
        if (!string.IsNullOrWhiteSpace(editor))
            target.Editor = editor;
    }

    private static void ApplyLogging(LoggingConfig target, Dictionary<string, object?> map)
    {
        var level = GetString(map, ConfigKeyAliases.Level);
        if (!string.IsNullOrWhiteSpace(level))
            target.Level = level;

        var file = GetString(map, ConfigKeyAliases.File);
        if (!string.IsNullOrWhiteSpace(file))
            target.File = file;
    }

    private static bool TryGetMap(
        Dictionary<string, object?> map,
        out Dictionary<string, object?> child,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value))
                continue;

            var normalized = ConfigMapNormalizer.Normalize(value);
            if (normalized == null)
                continue;

            child = normalized;
            return true;
        }

        child = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        return false;
    }

    private static string? GetString(Dictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value))
                continue;

            var text = (value?.ToString() ?? string.Empty).Trim();
            if (text.Length > 0)
                return text;
        }

        return null;
    }

    private static int? GetInt(Dictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value))
                continue;

            if (value is int i)
                return i;
            if (value is long l && l <= int.MaxValue && l >= int.MinValue)
                return (int)l;
            if (value is double d && d <= int.MaxValue && d >= int.MinValue)
                return (int)d;

            var text = (value?.ToString() ?? string.Empty).Trim();
            if (int.TryParse(text, out var parsed))
                return parsed;
        }

        return null;
    }

    private static List<string> GetStringList(Dictionary<string, object?> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!map.TryGetValue(key, out var value))
                continue;

            if (value is IEnumerable<object?> list)
                return list.Select(v => (v?.ToString() ?? string.Empty).Trim())
                           .Where(v => v.Length > 0)
                           .ToList();

            if (value is IEnumerable<string> strings)
                return strings.Select(v => (v ?? string.Empty).Trim())
                              .Where(v => v.Length > 0)
                              .ToList();

            var single = (value?.ToString() ?? string.Empty).Trim();
            if (single.Length > 0)
                return new List<string> { single };
        }

        return new List<string>();
    }
}
