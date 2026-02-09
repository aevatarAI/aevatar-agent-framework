using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.Core.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Extensions;

public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// Add Aevatar user-level configuration from ~/.aevatar/ directory.
    /// <para/>
    /// Loads both config.json (plaintext, lower priority) and secrets.json (encrypted, higher priority).
    /// <para/>
    /// Typical use:
    /// <code>
    /// builder.Configuration
    ///     .AddAevatarUserConfig()  // ~/.aevatar/config.json + secrets.json
    ///     .AddJsonFile("appsettings.json", optional: true)
    ///     .AddJsonFile("appsettings.secrets.json", optional: true);
    /// </code>
    /// </summary>
    public static IConfigurationBuilder AddAevatarUserConfig(
        this IConfigurationBuilder builder,
        Action<AevatarUserSecretsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new AevatarUserSecretsOptions();
        configure?.Invoke(options);

        builder.Add(new AevatarUserConfigConfigurationSource(options));
        builder.Add(new AevatarUserSecretsConfigurationSource(options));

        // Best-effort: load ~/.aevatar/mcp.json (Cursor-style MCP config)
        try
        {
            var configPath = options.ResolveConfigPath();
            var dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var mcpPath = Path.Combine(dir, "mcp.json");
                builder.AddJsonFile(mcpPath, optional: true, reloadOnChange: true);
            }
        }
        catch
        {
            // best-effort only
        }

        return builder;
    }

    /// <summary>
    /// Register a user-level secrets store for runtime write operations (e.g., web API or CLI).
    /// </summary>
    public static IServiceCollection AddAevatarUserSecretsStore(
        this IServiceCollection services,
        Action<AevatarUserSecretsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AevatarUserSecretsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IAevatarUserSecretsStore, FileAevatarUserSecretsStore>();
        return services;
    }

    // ============================================================
    //  共享 helper | shared helper
    // ============================================================

    private static void TryEnableReloadOnChangeCore(
        AevatarUserSecretsOptions options,
        ref FileSystemWatcher? watcher,
        Func<string> resolvePath,
        Action onReload)
    {
        if (!options.ReloadOnChange)
            return;

        if (watcher != null)
            return;

        string path;
        try
        {
            path = resolvePath();
        }
        catch
        {
            return;
        }

        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(file))
            return;

        try
        {
            if (!Directory.Exists(dir))
                return;

            watcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };

            watcher.Changed += (_, _) => onReload();
            watcher.Created += (_, _) => onReload();
            watcher.Renamed += (_, _) => onReload();
            watcher.Deleted += (_, _) => onReload();
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // best-effort only
        }
    }

    private static void ReloadBestEffortCore(Action loadAction, Action onReload)
    {
        try
        {
            loadAction();
            onReload();
        }
        catch
        {
            // ignore
        }
    }

    private static void DisposeWatcher(ref FileSystemWatcher? watcher)
    {
        try
        {
            watcher?.Dispose();
            watcher = null;
        }
        catch
        {
            // ignore
        }
    }

    // ============================================================
    //  config.json (plaintext, lower priority)
    // ============================================================

    private sealed class AevatarUserConfigConfigurationSource : IConfigurationSource
    {
        private readonly AevatarUserSecretsOptions _options;

        public AevatarUserConfigConfigurationSource(AevatarUserSecretsOptions options)
        {
            _options = options ?? new AevatarUserSecretsOptions();
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new AevatarUserConfigConfigurationProvider(_options);
        }
    }

    private sealed class AevatarUserConfigConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private readonly AevatarUserSecretsOptions _options;
        private FileSystemWatcher? _watcher;

        public AevatarUserConfigConfigurationProvider(AevatarUserSecretsOptions options)
        {
            _options = options ?? new AevatarUserSecretsOptions();
        }

        public override void Load()
        {
            var configPath = _options.ResolveConfigPath();
            if (!File.Exists(configPath))
            {
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            try
            {
                var json = File.ReadAllText(configPath);
                var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                using var doc = JsonDocument.Parse(json);
                FlattenJson(doc.RootElement, "", data);
                Data = data;
            }
            catch
            {
                Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            }

            TryEnableReloadOnChange();
        }

        private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string?> data)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}:{property.Name}";
                        FlattenJson(property.Value, key, data);
                    }
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        var key = $"{prefix}:{index}";
                        FlattenJson(item, key, data);
                        index++;
                    }
                    break;
                default:
                    data[prefix] = element.ToString();
                    break;
            }
        }

        public void Dispose()
        {
            DisposeWatcher(ref _watcher);
        }

        private void TryEnableReloadOnChange()
        {
            TryEnableReloadOnChangeCore(_options, ref _watcher, _options.ResolveConfigPath, ReloadBestEffort);
        }

        private void ReloadBestEffort()
        {
            ReloadBestEffortCore(Load, OnReload);
        }
    }

    // ============================================================
    //  secrets.json (encrypted, higher priority)
    // ============================================================

    private sealed class AevatarUserSecretsConfigurationSource : IConfigurationSource
    {
        private readonly AevatarUserSecretsOptions _options;

        public AevatarUserSecretsConfigurationSource(AevatarUserSecretsOptions options)
        {
            _options = options ?? new AevatarUserSecretsOptions();
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new AevatarUserSecretsConfigurationProvider(_options);
        }
    }

    private sealed class AevatarUserSecretsConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private readonly AevatarUserSecretsOptions _options;
        private readonly FileAevatarUserSecretsStore _store;
        private FileSystemWatcher? _watcher;

        public AevatarUserSecretsConfigurationProvider(AevatarUserSecretsOptions options)
        {
            _options = options ?? new AevatarUserSecretsOptions();
            _store = new FileAevatarUserSecretsStore(_options);
        }

        public override void Load()
        {
            var all = _store.GetAll();
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in all)
            {
                data[kv.Key] = kv.Value;
            }

            Data = data;

            TryEnableReloadOnChange();
        }

        public void Dispose()
        {
            DisposeWatcher(ref _watcher);
        }

        private void TryEnableReloadOnChange()
        {
            TryEnableReloadOnChangeCore(_options, ref _watcher, _options.ResolveSecretsPath, ReloadBestEffort);
        }

        private void ReloadBestEffort()
        {
            ReloadBestEffortCore(Load, OnReload);
        }
    }
}


