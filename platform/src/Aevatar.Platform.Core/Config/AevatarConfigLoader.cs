using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Platform.Core.Config;

// ============================================================
//  AevatarConfigLoader
//
//  Purpose:
//  - Load ~/.aevatar/config.yaml + secrets.yaml with safe defaults.
//  - Support env overrides (OpenCode-style: override config dir/path).
//  - Never log or serialize secrets into events.
//
//  Env (Platform):
//  - AEVATAR_CONFIG_DIR: override config directory (default ~/.aevatar)
//  - AEVATAR_CONFIG: override config.yaml path
//  - AEVATAR_SECRETS: override secrets.yaml path
// ============================================================
public sealed class AevatarConfigLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public AevatarEffectiveConfig Load()
    {
        var dir = ResolveConfigDirectory();

        var configPath = ResolveConfigPath(dir);
        var secretsPath = ResolveSecretsPath(dir);

        var config = LoadYamlOrDefault<AevatarConfig>(configPath) ?? new AevatarConfig();
        var secrets = LoadYamlOrDefault<SecretsYamlDto>(secretsPath);

        var mappedSecrets = MapSecrets(secrets);

        ApplyEnvOverrides(config);

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

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".aevatar");
    }

    private static string ResolveConfigPath(string configDir)
    {
        var fromEnv = (Environment.GetEnvironmentVariable("AEVATAR_CONFIG") ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        return Path.Combine(configDir, "config.yaml");
    }

    private static string ResolveSecretsPath(string configDir)
    {
        var fromEnv = (Environment.GetEnvironmentVariable("AEVATAR_SECRETS") ?? string.Empty).Trim();
        if (fromEnv.Length > 0)
            return ExpandHome(fromEnv);

        return Path.Combine(configDir, "secrets.yaml");
    }

    private static T? LoadYamlOrDefault<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
                return default;

            var raw = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw))
                return default;

            return Yaml.Deserialize<T>(raw);
        }
        catch
        {
            // Best-effort: config parse failure should not crash bootstrap.
            // Caller may surface a warning later via UI/logging (without secrets).
            return default;
        }
    }

    private static AevatarSecrets MapSecrets(SecretsYamlDto? dto)
    {
        var s = new AevatarSecrets();
        if (dto == null)
            return s;

        if (dto.Providers != null)
        {
            foreach (var (providerName, provider) in dto.Providers)
            {
                var k = (providerName ?? string.Empty).Trim();
                if (k.Length == 0) continue;
                var apiKey = (provider?.ApiKey ?? string.Empty).Trim();
                if (apiKey.Length == 0) continue;
                s.Providers.ApiKeys[k] = apiKey;
            }
        }

        if (dto.Mcp != null)
        {
            foreach (var (name, cred) in dto.Mcp)
            {
                var k = (name ?? string.Empty).Trim();
                if (k.Length == 0) continue;
                var v = (cred ?? string.Empty).Trim();
                if (v.Length == 0) continue;
                s.Mcp.Credentials[k] = v;
            }
        }

        return s;
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

        var model = (Environment.GetEnvironmentVariable("AEVATAR_MODEL") ?? string.Empty).Trim();
        if (model.Length > 0)
            config.Models.Default = model;
    }

    private static string ExpandHome(string path)
    {
        var p = path.Replace('\\', '/').Trim();
        if (!p.StartsWith("~/", StringComparison.Ordinal))
            return path;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, p[2..]);
    }

    // ------------------------------------------------------------
    // YAML DTOs (match secrets.yaml shape in PRD, but tolerant)
    // ------------------------------------------------------------
    private sealed class SecretsYamlDto
    {
        public Dictionary<string, ProviderSecretDto>? Providers { get; set; }

        // mcp: { github: {token} } OR mcp: { github: token }
        public Dictionary<string, string>? Mcp { get; set; }
    }

    private sealed class ProviderSecretDto
    {
        public string? ApiKey { get; set; }
    }
}


