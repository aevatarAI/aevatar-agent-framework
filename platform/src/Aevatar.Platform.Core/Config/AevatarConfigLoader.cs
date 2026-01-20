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

    public static void EnsureBootstrapAssets(AevatarEffectiveConfig effective)
    {
        if (effective == null || string.IsNullOrWhiteSpace(effective.ConfigDirectory))
            return;

        Directory.CreateDirectory(Path.Combine(effective.ConfigDirectory, "tools"));

        // 启动时同步 repo workflows -> ~/.aevatar/workflows (best-effort).
        TrySyncRepoWorkflows(effective.ConfigDirectory);
        TrySyncRepoSkills(effective.ConfigDirectory);

        TryWriteFileIfMissing(
            Path.Combine(effective.ConfigDirectory, "agents", $"{HermesRoleId}.yaml"),
            BuildHermesAgentYaml());

        TryWriteFileIfMissing(
            Path.Combine(effective.ConfigDirectory, "workflows", $"{HermesWorkflowName}.yaml"),
            BuildHermesWorkflowYaml());

        TryWriteFileIfMissing(
            Path.Combine(effective.ConfigDirectory, "workflows", $"{AgentCreatorWorkflowName}.yaml"),
            BuildAgentCreatorWorkflowYaml());
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

    private const string HermesRoleId = "hermes";
    private const string HermesWorkflowName = "agent_router";
    private const string AgentCreatorWorkflowName = "agent_creator";
    private const string RepoWorkflowsRelativePath = "src/Aevatar.Agents.Cognitive/workflows";
    private const string RepoSkillsRelativePath = "src/Aevatar.Agents.Cognitive/skills";
    private const int MaxLegacySuffixAttempts = 1000;

    private static string BuildHermesAgentYaml()
        => """
id: "hermes"
name: "Hermes Workflow Router"
version: "1.0"

persona:
  role: "Workflow router"
  expertise: ["workflow selection", "mesh dsl", "agent role design"]
  style: "concise, decisive"
  traits: ["systematic", "safety-first"]

tools:
  - "file_read"
  - "file_write"
  - "mesh_normalize"
skills: []

system_prompt: |
  You are Hermes, a workflow router agent.
  Your job is to match the user's intent to the most suitable workflow in ~/.aevatar/workflows,
  or choose "direct" when a single agent is enough.
  If no workflow fits, create a new workflow YAML under ~/.aevatar/workflows.
  If the workflow needs new roles, create role YAML files under ~/.aevatar/agents.
  Requirements:
  - Use Cognitive Mesh DSL v0.1 with fields: dsl_version, goal, strategy, budget, nodes, edges, constraints.
  - node.type must be one of: built-in agent types (DivergentAgent, ConvergentAgent, WorkerAgent, CriticAgent, MetaAgent),
    global roles (~/.aevatar/agents/*.yaml), or local roles (./aevatar/agents/*.yaml).
  - Prefer minimal topology (1-3 nodes) unless the task demands collaboration.
  - If single-agent, use "direct" and ensure exactly one agent YAML exists (do not create a workflow file).
  - Ask clarifying questions when requirements are ambiguous.
  - Respond in Chinese by default.
  - Use file_read/file_write to inspect and create workflow/agent files when needed.
  - Use mesh_normalize to validate and normalize workflow DSL before writing.
  - When creating files, report exact paths and a brief summary.
""";

    private static string BuildHermesWorkflowYaml()
        => """
dsl_version: "0.1"
goal:
  name: "agent_router"
  success_metric: "Route to a suitable workflow or direct agent based on user intent."
strategy: "cot"
budget:
  max_steps: 4
  token_limit: 6000
nodes:
  - id: "router"
    type: "hermes"
edges: []
constraints: []
""";

    private static string BuildAgentCreatorWorkflowYaml()
        => """
dsl_version: "0.1"
goal:
  name: "agent_creator"
  success_metric: "Create missing agents/workflows to satisfy the user's intent."
strategy: "cot"
budget:
  max_steps: 6
  token_limit: 8000
nodes:
  - id: "creator"
    type: "hermes"
edges: []
constraints: []
""";

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

    private static void TryWriteFileIfMissing(string path, string content)
    {
        try
        {
            if (File.Exists(path))
                return;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content);
        }
        catch
        {
            // best-effort only
        }
    }

    private static void TrySyncRepoWorkflows(string configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir))
            return;

        try
        {
            var sourceDir = ResolveRepoWorkflowsDirectory();
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                return;

            var targetDir = Path.Combine(configDir, "workflows");
            Directory.CreateDirectory(targetDir);

            if (IsSameDirectory(sourceDir, targetDir))
                return;

            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                if (!IsWorkflowFile(Path.GetExtension(file)))
                    continue;

                var fileName = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                var destPath = Path.Combine(targetDir, fileName);
                if (File.Exists(destPath))
                    MoveToLegacy(destPath, timestamp);

                File.Copy(file, destPath, overwrite: true);
            }
        }
        catch
        {
            // best-effort only
        }
    }

    private static void TrySyncRepoSkills(string configDir)
    {
        if (string.IsNullOrWhiteSpace(configDir))
            return;

        try
        {
            var sourceDir = ResolveRepoSkillsDirectory();
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                return;

            var targetDir = Path.Combine(configDir, "skills");
            Directory.CreateDirectory(targetDir);

            if (IsSameDirectory(sourceDir, targetDir))
                return;

            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
            CopyDirectoryFiles(sourceDir, targetDir, timestamp);
        }
        catch
        {
            // best-effort only
        }
    }

    private static string? ResolveRepoWorkflowsDirectory()
    {
        var current = Directory.GetCurrentDirectory();
        var root = FindRepoRoot(current);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = FindRepoRoot(AppContext.BaseDirectory);
            if (string.IsNullOrWhiteSpace(root))
                return null;
        }

        var candidate = Path.Combine(root, RepoWorkflowsRelativePath);
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveRepoSkillsDirectory()
    {
        var current = Directory.GetCurrentDirectory();
        var root = FindRepoRoot(current);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = FindRepoRoot(AppContext.BaseDirectory);
            if (string.IsNullOrWhiteSpace(root))
                return null;
        }

        var candidate = Path.Combine(root, RepoSkillsRelativePath);
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            return null;

        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return current.FullName;
            current = current.Parent;
        }

        return null;
    }

    private static bool IsWorkflowFile(string ext)
        => ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
           || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
           || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameDirectory(string left, string right)
    {
        var leftFull = Path.GetFullPath(left)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightFull = Path.GetFullPath(right)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(leftFull, rightFull, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectoryFiles(string sourceDir, string targetDir, string timestamp)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            if (string.IsNullOrWhiteSpace(rel))
                continue;

            var destPath = Path.Combine(targetDir, rel);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);

            if (File.Exists(destPath))
                MoveToLegacy(destPath, timestamp);

            File.Copy(file, destPath, overwrite: true);
        }
    }

    private static string MoveToLegacy(string path, string timestamp)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        if (string.IsNullOrWhiteSpace(timestamp))
            return path;
        if (!File.Exists(path))
            return path;

        var legacyPath = BuildLegacyPath(path, timestamp);
        File.Move(path, legacyPath);
        return legacyPath;
    }

    private static string BuildLegacyPath(string path, string timestamp, string tag = "legacy")
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var baseName = $"{name}_{tag}_{timestamp}";
        var legacyPath = Path.Combine(dir, $"{baseName}{ext}");
        if (!File.Exists(legacyPath))
            return legacyPath;

        for (var i = 2; i < MaxLegacySuffixAttempts; i++)
        {
            legacyPath = Path.Combine(dir, $"{baseName}_{i}{ext}");
            if (!File.Exists(legacyPath))
                return legacyPath;
        }

        return Path.Combine(dir, $"{baseName}_{Guid.NewGuid():N}{ext}");
    }
}


