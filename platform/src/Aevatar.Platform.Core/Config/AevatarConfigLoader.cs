using System.IO;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Secrets;

namespace Aevatar.Platform.Core.Config;

// ============================================================
//  AevatarConfigLoader
//
//  Purpose:
//  - Load ~/.aevatar/config.json + secrets.json with safe defaults.
//  - Merge LLMProviders from user config/secrets (aevatar-config compatible).
//  - Never log or serialize secrets into events.
//
//  Env (Platform):
//  - AEVATAR_CONFIG_DIR: override config directory (default ~/.aevatar)
//  - AEVATAR_CONFIG: override config.json path
//  - AEVATAR_SECRETS_PATH: override secrets.json path
//  - AEVATAR_SECRETS_DIR: override secrets directory
//  - AEVATAR_PROVIDER: override default provider
// ============================================================
public sealed class AevatarConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AevatarEffectiveConfig Load()
    {
        var dir = ResolveConfigDirectory();

        var configPath = ResolveConfigPath(dir);
        var secretsPath = ResolveSecretsPath(dir);

        var (config, llmFromConfig) = LoadConfigSnapshot(configPath);
        var secretsOptions = BuildSecretsOptions(dir, secretsPath);
        var secretsStore = new FileAevatarUserSecretsStore(secretsOptions);
        var allSecrets = secretsStore.GetAll();
        var mappedSecrets = MapSecrets(allSecrets);
        var llmFromSecrets = ParseLlmProvidersFromSecrets(allSecrets);

        MergeLlmProviders(config, llmFromConfig, overrideExisting: false);
        MergeLlmProviders(config, llmFromSecrets, overrideExisting: true);

        ApplyEnvOverrides(config);
        EnsureDefaultModel(config);

        return new AevatarEffectiveConfig(
            ConfigDirectory: dir,
            ConfigPath: configPath,
            SecretsPath: secretsPath,
            Config: config,
            Secrets: mappedSecrets);
    }

    private static string ResolveConfigDirectory()
    {
        var fromEnv = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_DIR") ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        var secretsDir = (Environment.GetEnvironmentVariable(AevatarAgentsConstants.SecretsDirEnv) ?? string.Empty).Trim();
        if (secretsDir.Length > 0)
            return ExpandHome(secretsDir);

        var secretsPath = (Environment.GetEnvironmentVariable(AevatarAgentsConstants.SecretsPathEnv) ?? string.Empty).Trim();
        if (secretsPath.Length > 0)
        {
            var expanded = ExpandHome(secretsPath);
            var directory = Path.GetDirectoryName(expanded);
            if (!string.IsNullOrWhiteSpace(directory))
                return directory;
        }

        var legacySecrets = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS") ?? string.Empty).Trim();
        if (legacySecrets.Length > 0)
        {
            var expanded = ExpandHome(legacySecrets);
            var directory = Path.GetDirectoryName(expanded);
            if (!string.IsNullOrWhiteSpace(directory))
                return directory;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar");
    }

    private static string ResolveConfigPath(string configDir)
    {
        var fromEnv = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG") ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        var fromEnv2 = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG_PATH") ?? string.Empty).Trim();
        if (fromEnv2.Length > 0)
            return ExpandHome(fromEnv2);

        return Path.Combine(configDir, "config.json");
    }

    private static string ResolveSecretsPath(string configDir)
    {
        var fromEnv = (Environment.GetEnvironmentVariable(AevatarAgentsConstants.SecretsPathEnv) ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        var legacy = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS") ?? string.Empty).Trim();
        if (legacy.Length > 0)
            return ExpandHome(legacy);

        var fromDir = (Environment.GetEnvironmentVariable(AevatarAgentsConstants.SecretsDirEnv) ?? string.Empty).Trim();
        if (fromDir.Length > 0)
            return Path.Combine(ExpandHome(fromDir), "secrets.json");

        return Path.Combine(configDir, "secrets.json");
    }

    private static void ApplyEnvOverrides(AevatarConfig config)
    {
        // Keep overrides minimal for task 3; expand in later tasks for full OpenCode parity.
        var profile = (Environment.GetEnvironmentVariable("AEVATAR_PROFILE") ?? string.Empty).Trim();
        if (profile.Length > 0)
            config.Agents.DefaultProfile = profile;

        var workflow = (Environment.GetEnvironmentVariable("AEVATAR_WORKFLOW") ?? string.Empty).Trim();
        if (workflow.Length > 0)
            config.Agents.DefaultWorkflow = workflow;

        var provider = (Environment.GetEnvironmentVariable("AEVATAR_PROVIDER") ?? string.Empty).Trim();
        if (provider.Length > 0)
            config.Models.DefaultProvider = provider;

        var model = (Environment.GetEnvironmentVariable("AEVATAR_MODEL") ?? string.Empty).Trim();
        if (model.Length > 0)
            config.Models.DefaultModel = model;
    }

    private static void EnsureDefaultModel(AevatarConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.Models.DefaultModel))
            return;

        var provider = (config.Models.DefaultProvider ?? string.Empty).Trim();
        if (provider.Length == 0)
            return;

        if (config.Models.Providers.TryGetValue(provider, out var entry))
        {
            var model = (entry.DefaultModel ?? string.Empty).Trim();
            if (model.Length > 0)
                config.Models.DefaultModel = model;
        }
    }

    private static string ExpandHome(string path)
    {
        var p = path.Replace('\\', '/').Trim();
        if (!p.StartsWith("~/", StringComparison.Ordinal))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, p[2..]);
    }

    private static AevatarUserSecretsOptions BuildSecretsOptions(string configDir, string secretsPath)
    {
        var options = new AevatarUserSecretsOptions
        {
            SecretsDirectory = configDir
        };

        if (!string.IsNullOrWhiteSpace(secretsPath))
            options.SecretsPath = secretsPath;

        return options;
    }

    private static (AevatarConfig Config, LlmProvidersSnapshot LlmProviders) LoadConfigSnapshot(string path)
    {
        try
        {
            if (!File.Exists(path))
                return (new AevatarConfig(), new LlmProvidersSnapshot());

            var raw = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw))
                return (new AevatarConfig(), new LlmProvidersSnapshot());

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var config = ParseAevatarConfig(root) ?? new AevatarConfig();
            var llmProviders = ParseLlmProvidersFromJson(root);
            return (config, llmProviders);
        }
        catch
        {
            return (new AevatarConfig(), new LlmProvidersSnapshot());
        }
    }

    private static AevatarConfig? ParseAevatarConfig(JsonElement root)
    {
        if (TryGetPropertyIgnoreCase(root, "Aevatar", out var aevatar) && aevatar.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<AevatarConfig>(aevatar.GetRawText(), JsonOptions);

        if (root.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<AevatarConfig>(root.GetRawText(), JsonOptions);

        return null;
    }

    private static LlmProvidersSnapshot ParseLlmProvidersFromJson(JsonElement root)
    {
        var snapshot = new LlmProvidersSnapshot();
        if (!TryGetPropertyIgnoreCase(root, "LLMProviders", out var llm) || llm.ValueKind != JsonValueKind.Object)
            return snapshot;

        if (TryGetPropertyIgnoreCase(llm, "Default", out var defaultValue) &&
            defaultValue.ValueKind == JsonValueKind.String)
        {
            snapshot.DefaultProvider = (defaultValue.GetString() ?? string.Empty).Trim();
        }

        if (TryGetPropertyIgnoreCase(llm, "Providers", out var providers) && providers.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in providers.EnumerateObject())
            {
                var name = (entry.Name ?? string.Empty).Trim();
                if (name.Length == 0 || entry.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var provider = new ProviderSnapshot();
                if (TryGetPropertyIgnoreCase(entry.Value, "Model", out var model) && model.ValueKind == JsonValueKind.String)
                    provider.Model = (model.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(provider.Model) &&
                    TryGetPropertyIgnoreCase(entry.Value, "DefaultModel", out var defaultModel) &&
                    defaultModel.ValueKind == JsonValueKind.String)
                {
                    provider.Model = (defaultModel.GetString() ?? string.Empty).Trim();
                }

                if (TryGetPropertyIgnoreCase(entry.Value, "Endpoint", out var endpoint) && endpoint.ValueKind == JsonValueKind.String)
                    provider.Endpoint = (endpoint.GetString() ?? string.Empty).Trim();

                snapshot.Providers[name] = provider;
            }
        }

        return snapshot;
    }

    private static LlmProvidersSnapshot ParseLlmProvidersFromSecrets(IReadOnlyDictionary<string, string> secrets)
    {
        var snapshot = new LlmProvidersSnapshot();
        foreach (var (rawKey, rawValue) in secrets)
        {
            var key = (rawKey ?? string.Empty).Trim();
            var value = (rawValue ?? string.Empty).Trim();
            if (key.Length == 0)
                continue;

            if (key.Equals(SecretsKeyRules.LlmDefaultProviderKey, StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0)
                    snapshot.DefaultProvider = value;
                continue;
            }

            if (!SecretsKeyRules.TryParseProviderFieldKey(key, out var name, out var field))
                continue;

            snapshot.Providers.TryGetValue(name, out var provider);
            provider ??= new ProviderSnapshot();

            if (field.Equals("Model", StringComparison.OrdinalIgnoreCase) ||
                field.Equals("DefaultModel", StringComparison.OrdinalIgnoreCase))
                provider.Model = value;
            if (field.Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                provider.Endpoint = value;

            snapshot.Providers[name] = provider;
        }

        return snapshot;
    }

    private static void MergeLlmProviders(AevatarConfig config, LlmProvidersSnapshot snapshot, bool overrideExisting)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.DefaultProvider) &&
            (overrideExisting || string.IsNullOrWhiteSpace(config.Models.DefaultProvider)))
        {
            config.Models.DefaultProvider = snapshot.DefaultProvider;
        }

        foreach (var (name, provider) in snapshot.Providers)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!config.Models.Providers.TryGetValue(name, out var entry))
                entry = new ProviderConfig();

            if (!string.IsNullOrWhiteSpace(provider.Model) &&
                (overrideExisting || string.IsNullOrWhiteSpace(entry.DefaultModel)))
                entry.DefaultModel = provider.Model;
            if (!string.IsNullOrWhiteSpace(provider.Endpoint) &&
                (overrideExisting || string.IsNullOrWhiteSpace(entry.Endpoint)))
                entry.Endpoint = provider.Endpoint;

            config.Models.Providers[name] = entry;
        }
    }

    private static AevatarSecrets MapSecrets(IReadOnlyDictionary<string, string> secrets)
    {
        var mapped = new AevatarSecrets();
        foreach (var (rawKey, rawValue) in secrets)
        {
            var key = (rawKey ?? string.Empty).Trim();
            var value = (rawValue ?? string.Empty).Trim();
            if (key.Length == 0 || value.Length == 0)
                continue;

            if (SecretsKeyRules.TryParseProviderApiKeyKey(key, out var provider))
            {
                mapped.Providers.ApiKeys[provider] = value;
                continue;
            }

            if (SecretsKeyRules.TryParseMcpTokenKey(key, out var server))
                mapped.Mcp.Credentials[server] = value;
        }

        return mapped;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }

    private sealed class LlmProvidersSnapshot
    {
        public string? DefaultProvider { get; set; }
        public Dictionary<string, ProviderSnapshot> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ProviderSnapshot
    {
        public string? Model { get; set; }
        public string? Endpoint { get; set; }
    }
}


