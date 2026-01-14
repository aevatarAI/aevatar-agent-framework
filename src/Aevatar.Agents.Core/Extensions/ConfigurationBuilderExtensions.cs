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
        return builder;
    }

    /// <summary>
    /// Add Aevatar user-level encrypted secrets as an IConfiguration source (best-effort).
    /// <para/>
    /// Typical use:
    /// <code>
    /// builder.Configuration.AddAevatarUserSecrets();
    /// </code>
    /// </summary>
    [Obsolete("Use AddAevatarUserConfig() instead, which loads both config.json and secrets.json")]
    public static IConfigurationBuilder AddAevatarUserSecrets(
        this IConfigurationBuilder builder,
        Action<AevatarUserSecretsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new AevatarUserSecretsOptions();
        configure?.Invoke(options);

        builder.Add(new AevatarUserSecretsConfigurationSource(options));
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
            try
            {
                _watcher?.Dispose();
                _watcher = null;
            }
            catch
            {
                // ignore
            }
        }

        private void TryEnableReloadOnChange()
        {
            if (!_options.ReloadOnChange)
                return;

            if (_watcher != null)
                return;

            string path;
            try
            {
                path = _options.ResolveConfigPath();
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

                _watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };

                _watcher.Changed += (_, _) => ReloadBestEffort();
                _watcher.Created += (_, _) => ReloadBestEffort();
                _watcher.Renamed += (_, _) => ReloadBestEffort();
                _watcher.Deleted += (_, _) => ReloadBestEffort();
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // best-effort only
            }
        }

        private void ReloadBestEffort()
        {
            try
            {
                Load();
                OnReload();
            }
            catch
            {
                // ignore
            }
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
            try
            {
                _watcher?.Dispose();
                _watcher = null;
            }
            catch
            {
                // ignore
            }
        }

        private void TryEnableReloadOnChange()
        {
            if (!_options.ReloadOnChange)
                return;

            if (_watcher != null)
                return;

            string path;
            try
            {
                path = _options.ResolveSecretsPath();
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

                _watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };

                _watcher.Changed += (_, _) => ReloadBestEffort();
                _watcher.Created += (_, _) => ReloadBestEffort();
                _watcher.Renamed += (_, _) => ReloadBestEffort();
                _watcher.Deleted += (_, _) => ReloadBestEffort();
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // best-effort only
            }
        }

        private void ReloadBestEffort()
        {
            try
            {
                Load();
                OnReload();
            }
            catch
            {
                // ignore
            }
        }
    }
}


