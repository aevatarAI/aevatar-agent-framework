using System.Collections.Generic;
using Aevatar.Agents.Core.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Core.Extensions;

public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// Add Aevatar user-level encrypted secrets as an IConfiguration source (best-effort).
    /// <para/>
    /// Typical use:
    /// <code>
    /// builder.Configuration.AddAevatarUserSecrets();
    /// </code>
    /// </summary>
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
    //  IConfigurationSource + Provider
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
                // IConfiguration keys are case-insensitive by default.
                // Never log values here (may contain secrets).
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

            // Only create watcher once.
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
                // Re-load Data then trigger IConfigurationRoot reload callbacks.
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


