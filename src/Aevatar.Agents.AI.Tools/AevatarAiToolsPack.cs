using System.Reflection;
using Aevatar.Agents.AI.Core.ToolPacks;
using Aevatar.Agents.AI.Tool.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  AevatarAiToolsPack
//
//  说明：
//  - 通过 DI 注入，供 AIGAgentBase 按 YAML tools 动态注册。
//  - 反射扫描本程序集内的 IAevatarTool 实现，避免手工维护名单。
//  - 仅支持构造函数签名：
//    () | (FileToolOptions) | (CommandToolOptions)
// ============================================================
public sealed class AevatarAiToolsPack : IAevatarToolPack
{
    private readonly IReadOnlyDictionary<string, ToolFactory> _factories;
    private readonly IReadOnlyList<string> _toolNames;

    public AevatarAiToolsPack()
    {
        _factories = BuildFactories();
        _toolNames = _factories.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string PackName => "aevatar.ai.tools";
    public IReadOnlyCollection<string> ToolNames => _toolNames;

    public bool TryCreateTool(string toolName, AevatarToolPackContext context, out IAevatarTool tool)
    {
        tool = null!;

        if (string.IsNullOrWhiteSpace(toolName))
            return false;

        if (!_factories.TryGetValue(toolName.Trim(), out var factory))
            return false;

        var fileOptions = BuildFileOptions(context);
        var commandOptions = BuildCommandOptions(context);

        tool = factory.Create(fileOptions, commandOptions);
        return tool != null;
    }

    private sealed record ToolFactory(Func<FileToolOptions, CommandToolOptions, IAevatarTool> Create);

    private static IReadOnlyDictionary<string, ToolFactory> BuildFactories()
    {
        var dict = new Dictionary<string, ToolFactory>(StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(AevatarAiToolsPack).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract)
                continue;
            if (!typeof(IAevatarTool).IsAssignableFrom(type))
                continue;

            if (!TryCreateFactory(type, out var factory))
                continue;

            try
            {
                var instance = factory.Create(FileToolOptions.Empty, CommandToolOptions.Empty);
                var name = instance?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    dict[name] = factory;
            }
            catch
            {
                // best-effort: skip broken tool types
            }
        }

        return dict;
    }

    private static bool TryCreateFactory(Type type, out ToolFactory factory)
    {
        var emptyCtor = type.GetConstructor(Type.EmptyTypes);
        if (emptyCtor != null)
        {
            factory = new ToolFactory((_, _) => (IAevatarTool)Activator.CreateInstance(type)!);
            return true;
        }

        var fileCtor = type.GetConstructor(new[] { typeof(FileToolOptions) });
        if (fileCtor != null)
        {
            factory = new ToolFactory((file, _) => (IAevatarTool)Activator.CreateInstance(type, file)!);
            return true;
        }

        var cmdCtor = type.GetConstructor(new[] { typeof(CommandToolOptions) });
        if (cmdCtor != null)
        {
            factory = new ToolFactory((_, cmd) => (IAevatarTool)Activator.CreateInstance(type, cmd)!);
            return true;
        }

        factory = null!;
        return false;
    }

    private static FileToolOptions BuildFileOptions(AevatarToolPackContext context)
    {
        var cfg = context.Configuration;
        var working = ReadString(cfg, "Aevatar:AiTools:WorkingDirectory")
                      ?? context.WorkingDirectory
                      ?? Directory.GetCurrentDirectory();

        var readRoots = ReadStringList(cfg, "Aevatar:AiTools:File:ReadRoots");
        var writeRoots = ReadStringList(cfg, "Aevatar:AiTools:File:WriteRoots");
        var writeExtensions = ReadStringList(cfg, "Aevatar:AiTools:File:WriteExtensions");

        var maxReadChars = ReadInt(cfg, "Aevatar:AiTools:File:MaxReadChars", 12000, 256, 200_000);
        var allowOverwrite = ReadBool(cfg, "Aevatar:AiTools:File:AllowOverwrite", false);

        var normalizedReadRoots = NormalizeRoots(readRoots);
        var normalizedWriteRoots = NormalizeRoots(writeRoots);

        if (writeExtensions.Count == 0)
            writeExtensions = FileToolOptions.Empty.WriteExtensions;

        return new FileToolOptions(
            WorkingDirectory: working,
            ReadRoots: normalizedReadRoots,
            WriteRoots: normalizedWriteRoots,
            WriteExtensions: writeExtensions,
            MaxReadChars: maxReadChars,
            AllowOverwrite: allowOverwrite);
    }

    private static CommandToolOptions BuildCommandOptions(AevatarToolPackContext context)
    {
        var cfg = context.Configuration;
        var working = ReadString(cfg, "Aevatar:AiTools:WorkingDirectory")
                      ?? context.WorkingDirectory
                      ?? Directory.GetCurrentDirectory();

        var allowedCommands = ReadStringList(cfg, "Aevatar:AiTools:Command:AllowedCommands");
        var timeoutSeconds = ReadInt(cfg, "Aevatar:AiTools:Command:TimeoutSeconds", 120, 1, 3600);
        var maxOutputChars = ReadInt(cfg, "Aevatar:AiTools:Command:MaxOutputChars", 20000, 256, 200_000);

        return new CommandToolOptions(
            WorkingDirectory: working,
            AllowedCommands: allowedCommands,
            TimeoutSeconds: timeoutSeconds,
            MaxOutputChars: maxOutputChars);
    }

    private static string? ReadString(IConfiguration? cfg, string key)
    {
        if (cfg == null) return null;
        var value = cfg[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> ReadStringList(IConfiguration? cfg, string key)
    {
        if (cfg == null) return Array.Empty<string>();

        var section = cfg.GetSection(key);
        if (!section.Exists())
            return Array.Empty<string>();

        var children = section.GetChildren()
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .ToList();

        if (children.Count > 0)
            return children;

        var raw = section.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static int ReadInt(IConfiguration? cfg, string key, int fallback, int min, int max)
    {
        if (cfg == null)
            return fallback;

        var raw = cfg[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (!int.TryParse(raw, out var value))
            return fallback;

        return Math.Clamp(value, min, max);
    }

    private static bool ReadBool(IConfiguration? cfg, string key, bool fallback)
    {
        if (cfg == null)
            return fallback;

        var raw = cfg[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        return bool.TryParse(raw, out var value) ? value : fallback;
    }

    private static IReadOnlyList<string> NormalizeRoots(IReadOnlyList<string> roots)
    {
        if (roots.Count == 0)
            return Array.Empty<string>();

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        foreach (var raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var expanded = ExpandHome(raw.Trim());
            var full = SafeGetFullPath(expanded);
            if (set.Add(full))
                list.Add(full);
        }

        return list;
    }

    private static string ExpandHome(string path)
    {
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                return Path.Combine(home, path[2..]);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private static string SafeGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
