using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.DependencyInjection;
using Aevatar.Agents.AI.Tools;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Platform.Core.Config;
using Aevatar.Platform.Core.Tools.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Platform.Core.Workflow;

public sealed record RoleAgentRunOptions(
    IReadOnlyList<string>? FixedAllowlist,
    bool AllowInternalTools,
    bool AllowDangerousTools,
    double Temperature,
    int MaxTokens);

public sealed class RoleAgentRunner
{
    private static readonly IReadOnlyList<string> HermesAllowlist = new[] { "file_read", "file_write", "mesh_normalize" };

    // ============================================================
    //  ServiceProvider Cache
    //
    //  说明：
    //  - 按 config/secrets 路径缓存，避免每次构建 DI
    // ============================================================
    private static readonly ConcurrentDictionary<string, Lazy<IServiceProvider>> ServiceProviderCache = new();

    public async Task<string> RunAsync(
        string role,
        string prompt,
        WorkflowRunInput input,
        RoleAgentRunOptions options,
        CancellationToken ct)
    {
        var (agent, error) = await TryCreateAgentAsync(role, input, options, ct);
        if (agent == null)
            return error ?? string.Empty;

        var request = ChatRequest.Create(prompt);
        request.MaxTokens = options.MaxTokens;
        request.Temperature = options.Temperature;
        var response = await agent.ChatAsync(request, ct);
        return response.Content ?? string.Empty;
    }

    public async IAsyncEnumerable<string> RunStreamAsync(
        string role,
        string prompt,
        WorkflowRunInput input,
        RoleAgentRunOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var debugRunId = $"stream_{Guid.NewGuid():N}";
        var debugSessionId = input.WorkflowName ?? "workflow";
        var (agent, error) = await TryCreateAgentAsync(role, input, options, ct);
        if (agent == null)
        {
            if (!string.IsNullOrWhiteSpace(error))
                yield return error!;
            yield break;
        }

        var request = ChatRequest.Create(prompt);
        request.MaxTokens = options.MaxTokens;
        request.Temperature = options.Temperature;

        var supportsStreaming = await agent.SupportsStreamingAsync(ct);
        #region agent log
        DebugLog(
            "RoleAgentRunner.cs:RunStreamAsync",
            "supports_streaming",
            new
            {
                role,
                supportsStreaming,
                provider = input.DefaultProvider ?? string.Empty,
                model = input.DefaultModel ?? string.Empty
            },
            debugSessionId,
            debugRunId,
            "H2");
        #endregion
        if (!supportsStreaming)
        {
            var response = await agent.ChatAsync(request, ct);
            if (!string.IsNullOrEmpty(response.Content))
                yield return response.Content!;
            yield break;
        }

        await foreach (var chunk in agent.ChatStreamAsync(request, ct))
        {
            if (!string.IsNullOrEmpty(chunk))
                yield return chunk;
        }
    }

    public RoleAgentRunOptions BuildHermesOptions()
    {
        return new RoleAgentRunOptions(
            FixedAllowlist: HermesAllowlist,
            AllowInternalTools: true,
            AllowDangerousTools: false,
            Temperature: 0.2,
            MaxTokens: 2048);
    }

    public RoleAgentRunOptions BuildDefaultOptions()
    {
        return new RoleAgentRunOptions(
            FixedAllowlist: null,
            AllowInternalTools: true,
            AllowDangerousTools: false,
            Temperature: 0.7,
            MaxTokens: 2048);
    }

    private async Task<(AIGAgentBase? Agent, string? Error)> TryCreateAgentAsync(
        string role,
        WorkflowRunInput input,
        RoleAgentRunOptions options,
        CancellationToken ct)
    {
        var yaml = TryLoadYamlForRole(role, input);
        var sp = GetOrCreateServiceProvider(input);
        var configuration = sp.GetRequiredService<IConfiguration>();

        var providerName = ResolveProviderName(configuration, input.DefaultProvider, yaml);
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return (null, "LLM provider not configured. Please set ~/.aevatar/secrets.json (LLMProviders).");
        }
        var agentFactory = new AIGAgentFactory(sp);
        var toolsConfig = input.ToolsConfig ?? new ToolsConfig();

        AIGAgentBase agent = CreateAgent(agentFactory, role, input, toolsConfig);
        agent.AllowInternalTools = options.AllowInternalTools;
        agent.AllowDangerousTools = options.AllowDangerousTools;

        if (yaml != null)
        {
            AgentYamlConfigApplier.ApplySystemPrompt(agent, yaml, role);
        }
        else
        {
            agent.SystemPrompt = $"You are the '{role}' role in a workflow.";
        }

        await agent.InitializeAsync(providerName, cfg =>
        {
            if (yaml != null)
                AgentYamlConfigApplier.ApplyModelKnobs(yaml, cfg);

            if (!string.IsNullOrWhiteSpace(input.DefaultModel))
                cfg.Model = input.DefaultModel!.Trim();
        }, ct);

        if (yaml != null)
            await AgentYamlConfigApplier.ApplyToolsAndSkillsAsync(agent, yaml, ct);

        await new PlatformToolPluginLoader().RegisterAsync(agent, input, toolsConfig, ct);

        if (options.FixedAllowlist is { Count: > 0 })
            agent.SetFixedToolAllowlist(options.FixedAllowlist);

        return (agent, null);
    }

    private static AIGAgentBase CreateAgent(
        AIGAgentFactory agentFactory,
        string role,
        WorkflowRunInput input,
        ToolsConfig toolsConfig)
    {
        if (role.Equals("hermes", StringComparison.OrdinalIgnoreCase))
        {
            var hermes = agentFactory.CreateGAgent<HermesAIGAgent>();
            hermes.InitializeRole(role);
            hermes.ApplyToolOptions(BuildHermesToolOptions(input), input.ConfigDirectory);
            return hermes;
        }

        var agent = agentFactory.CreateGAgent<PlatformRoleAIGAgent>();
        agent.InitializeRole(role);
        agent.ConfigureTools(
            BuildDefaultFileToolOptions(input, toolsConfig),
            BuildDefaultCommandToolOptions(input, toolsConfig));
        return agent;
    }

    private static FileToolOptions BuildHermesToolOptions(WorkflowRunInput input)
    {
        var working = string.IsNullOrWhiteSpace(input.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : input.WorkingDirectory;

        var readRoots = new List<string>
        {
            Path.Combine(input.ConfigDirectory, "workflows"),
            Path.Combine(input.ConfigDirectory, "agents"),
            Path.Combine(working, "aevatar", "agents")
        };

        var writeRoots = new List<string>
        {
            Path.Combine(input.ConfigDirectory, "workflows"),
            Path.Combine(input.ConfigDirectory, "agents")
        };

        return new FileToolOptions(
            WorkingDirectory: working,
            ReadRoots: DeduplicateRoots(readRoots),
            WriteRoots: DeduplicateRoots(writeRoots),
            WriteExtensions: new[] { ".yaml", ".yml", ".json" },
            MaxReadChars: 12000,
            AllowOverwrite: false);
    }

    private static FileToolOptions BuildDefaultFileToolOptions(WorkflowRunInput input, ToolsConfig tools)
    {
        var working = string.IsNullOrWhiteSpace(input.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : input.WorkingDirectory;

        var allowed = tools.FileSystem.AllowedPaths;
        var roots = allowed.Count > 0
            ? DeduplicateRoots(allowed)
            : DeduplicateRoots(new[]
            {
                working,
                Path.Combine(input.ConfigDirectory, "agents"),
                Path.Combine(input.ConfigDirectory, "workflows")
            });

        return new FileToolOptions(
            WorkingDirectory: working,
            ReadRoots: roots,
            WriteRoots: roots,
            WriteExtensions: new[] { "*" },
            MaxReadChars: 20000,
            AllowOverwrite: true);
    }

    private static CommandToolOptions BuildDefaultCommandToolOptions(WorkflowRunInput input, ToolsConfig tools)
    {
        var working = string.IsNullOrWhiteSpace(input.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : input.WorkingDirectory;

        var allowed = tools.Shell.AllowedCommands;
        var timeout = tools.Shell.TimeoutSeconds > 0 ? tools.Shell.TimeoutSeconds : 120;

        return new CommandToolOptions(
            WorkingDirectory: working,
            AllowedCommands: allowed.Count > 0 ? allowed : Array.Empty<string>(),
            TimeoutSeconds: timeout,
            MaxOutputChars: 20000);
    }

    private static IReadOnlyList<string> DeduplicateRoots(IEnumerable<string> roots)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var raw in roots)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var full = Path.GetFullPath(raw.Trim());
            if (set.Add(full))
                list.Add(full);
        }

        return list;
    }

    private static IConfiguration BuildUserConfiguration(WorkflowRunInput input)
    {
        return new ConfigurationBuilder()
            .AddAevatarUserConfig(o =>
            {
                o.ConfigPath = input.ConfigPath;
                o.SecretsPath = input.SecretsPath;
                o.SecretsDirectory = input.ConfigDirectory;
                o.ReloadOnChange = false;
            })
            .Build();
    }

    private static IServiceProvider GetOrCreateServiceProvider(WorkflowRunInput input)
    {
        var key = BuildServiceProviderCacheKey(input);
        var lazy = ServiceProviderCache.GetOrAdd(
            key,
            _ => new Lazy<IServiceProvider>(() => BuildServiceProvider(input), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private static IServiceProvider BuildServiceProvider(WorkflowRunInput input)
    {
        var configuration = BuildUserConfiguration(input);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<LLMProvidersConfig>(configuration.GetSection("LLMProviders"));
        services.AddAevatarLLMProviders();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static string BuildServiceProviderCacheKey(WorkflowRunInput input)
    {
        var configPath = NormalizePath(input.ConfigPath);
        var secretsPath = NormalizePath(input.SecretsPath);
        var configDir = NormalizePath(input.ConfigDirectory);
        return $"{configPath}|{secretsPath}|{configDir}";
    }

    private static string NormalizePath(string path)
    {
        var raw = (path ?? string.Empty).Trim();
        if (raw.Length == 0)
            return string.Empty;
        try
        {
            return Path.GetFullPath(raw);
        }
        catch
        {
            return raw;
        }
    }

    private static string? ResolveProviderName(
        IConfiguration configuration,
        string? preferred,
        AgentYamlConfig? yaml)
    {
        var providers = configuration.GetSection("LLMProviders:Providers").GetChildren()
            .Select(c => c.Key)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var yamlProvider = (yaml?.Provider ?? string.Empty).Trim();
        if (yamlProvider.Length > 0 &&
            !yamlProvider.Equals("default", StringComparison.OrdinalIgnoreCase) &&
            providers.Contains(yamlProvider, StringComparer.OrdinalIgnoreCase))
        {
            return yamlProvider;
        }

        var fromPreferred = (preferred ?? string.Empty).Trim();
        if (fromPreferred.Length > 0 &&
            providers.Contains(fromPreferred, StringComparer.OrdinalIgnoreCase))
            return fromPreferred;

        var defaultProvider = (configuration["LLMProviders:Default"] ?? string.Empty).Trim();
        if (defaultProvider.Length > 0 &&
            providers.Contains(defaultProvider, StringComparer.OrdinalIgnoreCase))
            return defaultProvider;

        return providers.Count > 0 ? providers[0] : null;
    }

    private static AgentYamlConfig? TryLoadYamlForRole(string role, WorkflowRunInput input)
    {
        var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
        if (key.Length == 0)
            return null;

        var loader = new AgentYamlConfigLoader();
        var localDir = Path.Combine(input.WorkingDirectory, "aevatar", "agents");
        var globalDir = Path.Combine(input.ConfigDirectory, "agents");

        var localYaml = Path.Combine(localDir, $"{key}.yaml");
        var localYml = Path.Combine(localDir, $"{key}.yml");
        var globalYaml = Path.Combine(globalDir, $"{key}.yaml");
        var globalYml = Path.Combine(globalDir, $"{key}.yml");

        return loader.TryLoadFromFile(localYaml)
               ?? loader.TryLoadFromFile(localYml)
               ?? loader.TryLoadFromFile(globalYaml)
               ?? loader.TryLoadFromFile(globalYml);
    }

    private const string DebugLogPath = "/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log";

    private static void DebugLog(
        string location,
        string message,
        object data,
        string sessionId,
        string runId,
        string hypothesisId)
    {
        try
        {
            var payload = new
            {
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                sessionId,
                runId,
                hypothesisId
            };
            var json = JsonSerializer.Serialize(payload);
            File.AppendAllText(DebugLogPath, json + Environment.NewLine);
        }
        catch
        {
            // best-effort only
        }
    }
}
