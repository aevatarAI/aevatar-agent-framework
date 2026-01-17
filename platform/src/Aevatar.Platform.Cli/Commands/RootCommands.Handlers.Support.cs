using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Platform.Core.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Platform.Cli.Commands;

public static partial class RootCommands
{
    private sealed class McpServersYaml
    {
        public Dictionary<string, McpServerYaml>? Servers { get; set; }
    }

    private sealed class McpServerYaml
    {
        public string? Url { get; set; }
        public string? Command { get; set; }
        public string? Transport { get; set; }
    }

    private static partial class Handlers
    {
        private static readonly IDeserializer YamlReader = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        private static readonly ISerializer YamlWriter = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
            .Build();

        public static async Task EditConfigAsync(InvocationContext ctx)
        {
            var effective = LoadEffectiveConfig(ctx);
            if (!File.Exists(effective.ConfigPath))
                await InitConfigAsync(ctx, overwrite: false);

            await OpenEditorAsync(effective.ConfigPath, effective);
        }

        public static async Task InitConfigAsync(InvocationContext ctx, bool overwrite)
        {
            var effective = LoadEffectiveConfig(ctx);
            Directory.CreateDirectory(effective.ConfigDirectory);
            var secretsOptions = new AevatarUserSecretsOptions
            {
                SecretsDirectory = effective.ConfigDirectory,
                SecretsPath = effective.SecretsPath
            };
            secretsOptions.EnsureDirectoryInitialized();
            var configPath = effective.ConfigPath;

            if (!File.Exists(configPath) || overwrite)
            {
                var config = new AevatarConfig();
                var payload = new Dictionary<string, object?>
                {
                    ["Aevatar"] = config
                };
                var json = JsonSerializer.Serialize(payload, JsonIndented);
                await File.WriteAllTextAsync(configPath, json);
            }

            var mcpPath = Path.Combine(effective.ConfigDirectory, "mcp", "servers.yaml");
            if (!File.Exists(mcpPath) || overwrite)
            {
                var servers = new McpServersYaml { Servers = new Dictionary<string, McpServerYaml>() };
                var yaml = YamlWriter.Serialize(servers);
                await File.WriteAllTextAsync(mcpPath, yaml);
            }

            ctx.Console.WriteLine($"Initialized config at {effective.ConfigDirectory}");
        }

        public static async Task CreateAgentRoleAsync(
            InvocationContext ctx,
            string role,
            string? provider,
            string? model,
            bool overwrite,
            bool editAfter)
        {
            var effective = LoadEffectiveConfig(ctx);
            var dir = Path.Combine(effective.ConfigDirectory, "agents");
            Directory.CreateDirectory(dir);

            var name = (role ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                ctx.Console.Error.WriteLine("Agent role name is required.");
                return;
            }

            var path = Path.Combine(dir, $"{name}.yaml");
            if (File.Exists(path) && !overwrite)
            {
                ctx.Console.Error.WriteLine($"Agent role already exists: {path}");
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine($"id: \"{name}\"");
            builder.AppendLine($"name: \"{name} Agent\"");
            builder.AppendLine("version: \"1.0\"");
            builder.AppendLine("persona:");
            builder.AppendLine("  role: \"Software Engineer\"");
            builder.AppendLine("  expertise: [\"coding\"]");
            builder.AppendLine("  style: \"clear\"");
            if (!string.IsNullOrWhiteSpace(provider))
                builder.AppendLine($"provider: \"{provider.Trim()}\"");
            if (!string.IsNullOrWhiteSpace(model))
                builder.AppendLine($"model: \"{model.Trim()}\"");
            builder.AppendLine("tools: []");
            builder.AppendLine("skills: []");
            builder.AppendLine("system_prompt: |");
            builder.AppendLine("  You are a helpful coding assistant.");

            await File.WriteAllTextAsync(path, builder.ToString());
            ctx.Console.WriteLine($"Created agent role: {path}");

            if (editAfter)
                await OpenEditorAsync(path, effective);
        }

        public static async Task SyncWorkflowsAsync(InvocationContext ctx, string? sourceDir)
        {
            var effective = LoadEffectiveConfig(ctx);
            var targetDir = EnsureWorkflowsDirectory(effective.ConfigDirectory);
            var resolvedSource = RepoPathResolver.ResolveWorkflowsSourceDirectory(sourceDir);
            if (!DirectorySync.TryResolvePaths(resolvedSource, targetDir, out var sourceFull, out var error))
            {
                ctx.Console.Error.WriteLine(error switch
                {
                    DirectorySync.ResolveError.SourceNotFound =>
                        "Workflow source directory not found. Use --source to specify.",
                    DirectorySync.ResolveError.SameDirectory =>
                        "Source and target workflows directories are the same.",
                    _ => "Workflow source directory could not be resolved."
                });
                ctx.ExitCode = 1;
                return;
            }

            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
            var result = DirectorySync.SyncFiles(
                sourceFull,
                targetDir,
                timestamp,
                file => IsWorkflowFile(Path.GetExtension(file)));
            ReportWorkflowSync(ctx, targetDir, result);
        }

        public static async Task MigrateConfigAsync(InvocationContext ctx, bool dryRun)
        {
            var effective = LoadEffectiveConfig(ctx);
            var configPath = effective.ConfigPath;
            var legacyPath = Path.Combine(effective.ConfigDirectory, "config.yaml");
            var useYaml = false;

            if (!File.Exists(configPath))
            {
                if (File.Exists(legacyPath))
                {
                    configPath = legacyPath;
                    useYaml = true;
                }
                else
                {
                    ctx.Console.Error.WriteLine("config.json not found.");
                    return;
                }
            }

            var raw = await File.ReadAllTextAsync(configPath);
            if (string.IsNullOrWhiteSpace(raw))
            {
                ctx.Console.Error.WriteLine(useYaml ? "config.yaml is empty." : "config.json is empty.");
                return;
            }

            Dictionary<string, object?>? mapped;
            if (useYaml)
            {
                var root = YamlReader.Deserialize<object>(raw);
                mapped = ConfigMapNormalizer.Normalize(root);
            }
            else
            {
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    mapped = ConfigMapNormalizer.Normalize(doc.RootElement);
                }
                catch
                {
                    mapped = null;
                }
            }

            if (mapped == null)
            {
                ctx.Console.Error.WriteLine(useYaml ? "config.yaml format invalid." : "config.json format invalid.");
                return;
            }

            if (!TryMigrateModelsInRoot(mapped, out var message))
            {
                ctx.Console.WriteLine(message);
                return;
            }

            var serialized = useYaml
                ? SerializeAevatarConfigJson(mapped)
                : JsonSerializer.Serialize(mapped, JsonIndented);
            if (dryRun)
            {
                ctx.Console.WriteLine("Migration preview:");
                ctx.Console.WriteLine(serialized);
                return;
            }

            if (useYaml)
            {
                await File.WriteAllTextAsync(effective.ConfigPath, serialized);
                ctx.Console.WriteLine("config.json created from config.yaml.");
                return;
            }

            await File.WriteAllTextAsync(configPath, serialized);
            ctx.Console.WriteLine("config.json migrated.");
        }

        public static Task ValidateConfigAsync(InvocationContext ctx)
        {
            var effective = LoadEffectiveConfig(ctx);
            var errors = new List<string>();

            var defaultProvider = (effective.Config.Models.DefaultProvider ?? string.Empty).Trim();
            var defaultModel = (effective.Config.Models.DefaultModel ?? string.Empty).Trim();
            if (defaultProvider.Length == 0)
                errors.Add("models.default_provider is empty.");
            if (defaultModel.Length == 0)
                errors.Add("models.default_model is empty.");
            if (defaultProvider.Length > 0 &&
                !effective.Config.Models.Providers.ContainsKey(defaultProvider))
                errors.Add("models.default_provider not found in models.providers.");

            if (errors.Count == 0)
            {
                ctx.Console.WriteLine("OK");
                return Task.CompletedTask;
            }

            foreach (var err in errors)
                ctx.Console.Error.WriteLine($"ERROR: {err}");

            ctx.ExitCode = 1;
            return Task.CompletedTask;
        }

        public static async Task ListModelsAsync(
            InvocationContext ctx,
            string? providerFilter,
            bool verbose,
            bool refresh)
        {
            var effective = LoadEffectiveConfig(ctx);
            if (refresh)
                effective = new AevatarConfigLoader().Load();

            var config = effective.Config;
            var providers = config.Models.Providers;
            if (providers.Count == 0)
            {
                ctx.Console.WriteLine("(no providers)");
                return;
            }

            var list = providers
                .Where(p => string.IsNullOrWhiteSpace(providerFilter) ||
                            p.Key.Equals(providerFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (list.Count == 0)
            {
                ctx.Console.WriteLine("(no providers)");
                return;
            }

            foreach (var (name, provider) in list)
            {
                if (verbose)
                {
                    var endpoint = provider.Endpoint ?? string.Empty;
                    var model = provider.DefaultModel ?? string.Empty;
                    ctx.Console.WriteLine($"{name}\t{model}\t{endpoint}");
                }
                else
                {
                    var model = provider.DefaultModel ?? string.Empty;
                    ctx.Console.WriteLine($"{name}\t{model}");
                }
            }
        }

        public static async Task ListMcpServersAsync(InvocationContext ctx)
        {
            var effective = LoadEffectiveConfig(ctx);
            var path = GetMcpServersPath(effective.ConfigDirectory);
            if (!File.Exists(path))
            {
                ctx.Console.WriteLine("(no mcp servers)");
                return;
            }

            var servers = LoadYaml<McpServersYaml>(path) ?? new McpServersYaml();
            if (servers.Servers == null || servers.Servers.Count == 0)
            {
                ctx.Console.WriteLine("(no mcp servers)");
                return;
            }

            foreach (var (name, server) in servers.Servers.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
            {
                var transport = server?.Transport ?? string.Empty;
                var url = server?.Url ?? string.Empty;
                var command = server?.Command ?? string.Empty;
                ctx.Console.WriteLine($"{name}\t{transport}\t{url}\t{command}");
            }
        }

        public static async Task AddMcpServerAsync(
            InvocationContext ctx,
            string name,
            string? url,
            string? command,
            string? transport)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                ctx.Console.Error.WriteLine("MCP server name is required.");
                return;
            }

            var hasUrl = !string.IsNullOrWhiteSpace(url);
            var hasCommand = !string.IsNullOrWhiteSpace(command);
            if (!hasUrl && !hasCommand)
            {
                ctx.Console.Error.WriteLine("Provide --url or --command.");
                return;
            }

            var effective = LoadEffectiveConfig(ctx);
            var path = GetMcpServersPath(effective.ConfigDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var servers = LoadYaml<McpServersYaml>(path) ?? new McpServersYaml();
            servers.Servers ??= new Dictionary<string, McpServerYaml>(StringComparer.OrdinalIgnoreCase);

            var resolvedTransport = (transport ?? string.Empty).Trim();
            if (resolvedTransport.Length == 0)
                resolvedTransport = hasUrl ? "http" : "stdio";
            resolvedTransport = resolvedTransport.ToLowerInvariant();
            if (resolvedTransport is not ("http" or "stdio"))
            {
                ctx.Console.Error.WriteLine("Transport must be http or stdio.");
                return;
            }

            servers.Servers[key] = new McpServerYaml
            {
                Url = hasUrl ? url!.Trim() : null,
                Command = hasCommand ? command!.Trim() : null,
                Transport = resolvedTransport
            };

            await SaveYamlAsync(path, servers);
            ctx.Console.WriteLine($"Saved MCP server '{key}'");
        }

        public static async Task SaveMcpCredentialAsync(
            InvocationContext ctx,
            string? name,
            string? token,
            string? fromEnv,
            bool fromStdin)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                ctx.Console.Error.WriteLine("MCP server name is required.");
                return;
            }

            var value = await ResolveSecretValueAsync(token, fromEnv, fromStdin);
            if (value == null)
            {
                ctx.Console.Error.WriteLine("Credential is required.");
                return;
            }

            var effective = LoadEffectiveConfig(ctx);
            var store = OpenSecretsStore(effective);
            store.Set(SecretsKeyRules.BuildMcpTokenKey(key), value);
            ctx.Console.WriteLine($"Saved MCP credential for '{key}'");
        }

        public static async Task RemoveMcpCredentialAsync(InvocationContext ctx, string? name)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                ctx.Console.Error.WriteLine("MCP server name is required.");
                return;
            }

            var effective = LoadEffectiveConfig(ctx);
            var store = OpenSecretsStore(effective);
            var removed = store.Remove(SecretsKeyRules.BuildMcpTokenKey(key)) ||
                          store.Remove(SecretsKeyRules.BuildMcpLegacyKey(key));
            if (!removed)
            {
                ctx.Console.WriteLine("NOT_FOUND");
                return;
            }

            ctx.Console.WriteLine("OK");
        }

        public static async Task DebugMcpServerAsync(InvocationContext ctx, string? name)
        {
            var key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                ctx.Console.Error.WriteLine("MCP server name is required.");
                return;
            }

            var effective = LoadEffectiveConfig(ctx);
            var servers = LoadYaml<McpServersYaml>(GetMcpServersPath(effective.ConfigDirectory));
            var server = servers?.Servers != null && servers.Servers.TryGetValue(key, out var s) ? s : null;
            if (server == null)
            {
                ctx.Console.Error.WriteLine("MCP server not found.");
                return;
            }

            var store = OpenSecretsStore(effective);
            var hasCredential = SecretsKeyRules.HasMcpCredential(store.GetAll(), key);

            ctx.Console.WriteLine($"name: {key}");
            ctx.Console.WriteLine($"transport: {server.Transport}");
            ctx.Console.WriteLine($"url: {server.Url}");
            ctx.Console.WriteLine($"command: {server.Command}");
            ctx.Console.WriteLine($"credential: {(hasCredential ? "present" : "missing")}");
        }

        public static Task ListMcpAuthAsync(InvocationContext ctx)
        {
            var effective = LoadEffectiveConfig(ctx);
            var servers = LoadYaml<McpServersYaml>(GetMcpServersPath(effective.ConfigDirectory));
            if (servers?.Servers == null || servers.Servers.Count == 0)
            {
                ctx.Console.WriteLine("(no mcp servers)");
                return Task.CompletedTask;
            }

            var store = OpenSecretsStore(effective);
            foreach (var name in servers.Servers.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var hasCredential = SecretsKeyRules.HasMcpCredential(store.GetAll(), name);
                ctx.Console.WriteLine($"{name}\t{(hasCredential ? "present" : "missing")}");
            }

            return Task.CompletedTask;
        }

        public static async Task SaveProviderCredentialAsync(
            InvocationContext ctx,
            string? provider,
            string? apiKey,
            string? fromEnv,
            bool fromStdin)
        {
            var key = (provider ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                ctx.Console.Error.WriteLine("Provider name is required.");
                return;
            }

            var value = await ResolveSecretValueAsync(apiKey, fromEnv, fromStdin);
            if (value == null)
            {
                ctx.Console.Error.WriteLine("API key is required.");
                return;
            }

            var effective = LoadEffectiveConfig(ctx);
            var store = OpenSecretsStore(effective);
            store.Set(SecretsKeyRules.BuildProviderApiKeyKey(key), value);
            ctx.Console.WriteLine($"Saved provider credential for '{key}'");
        }

        public static Task ListProviderCredentialsAsync(InvocationContext ctx)
        {
            var effective = LoadEffectiveConfig(ctx);
            var store = OpenSecretsStore(effective);
            var providers = SecretsKeyRules.ListProvidersWithApiKey(store.GetAll());
            if (providers.Count == 0)
            {
                ctx.Console.WriteLine("(no providers)");
                return Task.CompletedTask;
            }

            foreach (var name in providers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                ctx.Console.WriteLine(name);

            return Task.CompletedTask;
        }

        public static async Task RemoveProviderCredentialAsync(InvocationContext ctx, string? provider)
        {
            var key = (provider ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                ctx.Console.Error.WriteLine("Provider name is required.");
                return;
            }

            var effective = LoadEffectiveConfig(ctx);
            var store = OpenSecretsStore(effective);
            if (!store.Remove(SecretsKeyRules.BuildProviderApiKeyKey(key)))
            {
                ctx.Console.WriteLine("NOT_FOUND");
                return;
            }

            ctx.Console.WriteLine("OK");
        }


        public static Task RunUpgradeAsync(InvocationContext ctx, string? method)
        {
            var key = (method ?? string.Empty).Trim().ToLowerInvariant();
            var msg = key switch
            {
                "brew" => "Upgrade: brew update && brew upgrade aevatar",
                "dotnet" => "Upgrade: dotnet tool update -g aevatar.platform",
                "manual" => "Upgrade: download latest release and replace binary",
                _ => "Upgrade methods: --method brew | dotnet | manual"
            };
            ctx.Console.WriteLine(msg);
            return Task.CompletedTask;
        }

        public static Task RunUninstallAsync(
            InvocationContext ctx,
            bool keepConfig,
            bool keepData,
            bool dryRun,
            bool force)
        {
            var effective = LoadEffectiveConfig(ctx);
            var targets = BuildUninstallTargets(effective.ConfigDirectory, keepConfig, keepData);
            if (targets.Count == 0)
            {
                ctx.Console.WriteLine("Nothing to remove.");
                return Task.CompletedTask;
            }

            foreach (var item in targets)
                ctx.Console.WriteLine(item);

            if (dryRun || !force)
            {
                ctx.Console.WriteLine("Dry run only. Use --force to remove.");
                return Task.CompletedTask;
            }

            foreach (var item in targets)
                DeletePath(item);

            ctx.Console.WriteLine("OK");
            return Task.CompletedTask;
        }

        public static async Task InstallGithubWorkflowAsync(InvocationContext ctx, string? path, bool overwrite)
        {
            var target = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Directory.GetCurrentDirectory(), ".github", "workflows", "aevatar-platform.yml")
                : Path.GetFullPath(path);

            var dir = Path.GetDirectoryName(target);
            if (string.IsNullOrWhiteSpace(dir))
            {
                ctx.Console.Error.WriteLine("Invalid workflow path.");
                return;
            }

            Directory.CreateDirectory(dir);
            if (File.Exists(target) && !overwrite)
            {
                ctx.Console.Error.WriteLine($"Workflow already exists: {target}");
                return;
            }

            var content = BuildGithubWorkflowTemplate();
            await File.WriteAllTextAsync(target, content);
            ctx.Console.WriteLine($"Installed workflow: {target}");
        }

        public static async Task RunGithubAsync(InvocationContext ctx, string? eventPath, string? token)
        {
            var path = eventPath ?? Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ctx.Console.Error.WriteLine("GitHub event payload not found.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(token))
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", token);

            var payload = await File.ReadAllTextAsync(path);
            var eventName = Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? "github_event";
            var repo = TryExtractRepository(payload) ?? "unknown_repo";

            var prompt = $"Handle GitHub event '{eventName}' for {repo}.";
            var runOptions = BuildRunOptions(ctx.ParseResult, prompt);
            var withFile = runOptions with { Files = new[] { path } };

            await RunOnceAsync(ctx, withFile);
        }

        private static async Task OpenEditorAsync(string path, AevatarEffectiveConfig effective)
        {
            var editor = (Environment.GetEnvironmentVariable("EDITOR") ?? string.Empty).Trim();
            if (editor.Length == 0)
                editor = (effective.Config.Ui.Editor ?? string.Empty).Trim();
            if (editor.Length == 0)
                editor = "vi";

            var psi = new ProcessStartInfo
            {
                FileName = editor,
                Arguments = QuoteArg(path),
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc != null)
                await proc.WaitForExitAsync();
        }

        private static string QuoteArg(string value)
            => value.Contains(' ') ? $"\"{value}\"" : value;

        private static string EnsureWorkflowsDirectory(string configDir)
        {
            var targetDir = Path.Combine(configDir, "workflows");
            Directory.CreateDirectory(targetDir);
            return targetDir;
        }

        private static void ReportWorkflowSync(
            InvocationContext ctx,
            string targetDir,
            DirectorySync.SyncResult result)
        {
            if (result.Copied == 0)
            {
                ctx.Console.WriteLine("No workflow files found to sync.");
                return;
            }

            ctx.Console.WriteLine($"Synced {result.Copied} workflow file(s) to {targetDir}.");
            if (result.Replaced.Count == 0)
                return;

            ctx.Console.WriteLine("Replaced existing workflows (backup created):");
            foreach (var item in result.Replaced)
                ctx.Console.WriteLine($"- {item.FileName} -> {Path.GetFileName(item.LegacyPath)}");
        }

        private static bool IsWorkflowFile(string ext)
            => ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);


        private static string? TryExtractRepository(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("repository", out var repo) &&
                    repo.TryGetProperty("full_name", out var full))
                {
                    return full.GetString();
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string BuildGithubWorkflowTemplate()
        {
            return """
name: Aevatar Platform
on:
  issues:
  pull_request:
  issue_comment:
  pull_request_review_comment:
jobs:
  aevatar:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Run Aevatar Platform
        env:
          AEVATAR_SERVER_AUTH_TOKEN: ${{ secrets.AEVATAR_TOKEN }}
        run: dotnet run --project platform/src/Aevatar.Platform.Cli -- github run --event "${{ github.event_path }}"
""";
        }

        private static List<string> BuildUninstallTargets(string configDir, bool keepConfig, bool keepData)
        {
            var targets = new List<string>();
            var root = Path.GetFullPath(configDir);
            if (!keepConfig)
            {
                targets.Add(Path.Combine(root, "config.json"));
                targets.Add(Path.Combine(root, "secrets.json"));
                targets.Add(Path.Combine(root, "agents"));
                targets.Add(Path.Combine(root, "workflows"));
                targets.Add(Path.Combine(root, "mcp"));
                targets.Add(Path.Combine(root, "logs"));
            }

            if (!keepData)
                targets.Add(Path.Combine(root, "sessions"));

            return targets;
        }

        private static void DeletePath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    return;
                }

                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // best-effort
            }
        }

        private static string GetMcpServersPath(string configDir)
            => Path.Combine(configDir, "mcp", "servers.yaml");

        private static T? LoadYaml<T>(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return default;

                var raw = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(raw))
                    return default;

                return YamlReader.Deserialize<T>(raw);
            }
            catch
            {
                return default;
            }
        }

        private static async Task SaveYamlAsync<T>(string path, T value)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var yaml = YamlWriter.Serialize(value);
            await File.WriteAllTextAsync(path, yaml);
        }

        private static async Task<string?> ResolveSecretValueAsync(string? direct, string? fromEnv, bool fromStdin)
        {
            var value = (direct ?? string.Empty).Trim();
            if (value.Length > 0)
                return value;

            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                var env = Environment.GetEnvironmentVariable(fromEnv.Trim());
                if (!string.IsNullOrWhiteSpace(env))
                    return env.Trim();
            }

            if (fromStdin)
            {
                var stdin = await Console.In.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(stdin))
                    return stdin.Trim();
            }

            return null;
        }

        private static FileAevatarUserSecretsStore OpenSecretsStore(AevatarEffectiveConfig effective)
        {
            var options = new AevatarUserSecretsOptions
            {
                SecretsDirectory = effective.ConfigDirectory,
                SecretsPath = effective.SecretsPath
            };
            options.EnsureDirectoryInitialized();
            return new FileAevatarUserSecretsStore(options);
        }

        private static string SerializeAevatarConfigJson(Dictionary<string, object?> mapped)
        {
            var config = AevatarConfigSerializer.FromMap(mapped);
            var payload = new Dictionary<string, object?>
            {
                ["Aevatar"] = config
            };
            return JsonSerializer.Serialize(payload, JsonIndented);
        }

        private static bool TryMigrateModelsInRoot(Dictionary<string, object?> root, out string message)
        {
            if (TryMigrateModels(root, out message))
                return true;

            if (root.TryGetValue("aevatar", out var aevatarObj))
            {
                var aevatar = ConfigMapNormalizer.Normalize(aevatarObj);
                if (aevatar == null)
                {
                    message = "aevatar section is invalid.";
                    return false;
                }

                if (TryMigrateModels(aevatar, out message))
                {
                    root["aevatar"] = aevatar;
                    return true;
                }
            }

            return false;
        }

        private static bool TryMigrateModels(Dictionary<string, object?> root, out string message)
        {
            message = string.Empty;
            if (!root.TryGetValue("models", out var modelsObj))
            {
                message = "No models section found. Nothing to migrate.";
                return false;
            }

            var models = ConfigMapNormalizer.Normalize(modelsObj);
            if (models == null)
            {
                message = "models section is invalid.";
                return false;
            }

            var defaultValue = GetString(models, "default");
            var defaultProvider = GetString(models, "default_provider");
            var defaultModel = GetString(models, "default_model");

            if (string.IsNullOrWhiteSpace(defaultValue))
            {
                if (string.IsNullOrWhiteSpace(defaultProvider) && string.IsNullOrWhiteSpace(defaultModel))
                    message = "No legacy models.default found. Nothing to migrate.";
                else
                    message = "models.default is empty; nothing to migrate.";
                return false;
            }

            var providerCandidate = string.Empty;
            var modelCandidate = string.Empty;

            var text = defaultValue.Trim();
            var slash = text.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0)
            {
                providerCandidate = text[..slash];
                modelCandidate = text[(slash + 1)..];
            }
            else if (IsKnownProvider(models, text))
            {
                providerCandidate = text;
            }
            else
            {
                modelCandidate = text;
            }

            if (string.IsNullOrWhiteSpace(defaultProvider) && providerCandidate.Length > 0)
                models["default_provider"] = providerCandidate;
            if (string.IsNullOrWhiteSpace(defaultModel) && modelCandidate.Length > 0)
                models["default_model"] = modelCandidate;

            models.Remove("default");
            root["models"] = models;
            message = "models.default migrated to default_provider/default_model.";
            return true;
        }

        private static bool IsKnownProvider(Dictionary<string, object?> models, string name)
        {
            if (!models.TryGetValue("providers", out var providersObj))
                return false;

            var providers = ConfigMapNormalizer.Normalize(providersObj);
            if (providers == null)
                return false;

            return providers.ContainsKey(name);
        }

        private static string GetString(Dictionary<string, object?> map, string key)
        {
            return map.TryGetValue(key, out var value)
                ? (value?.ToString() ?? string.Empty).Trim()
                : string.Empty;
        }

    }
}
