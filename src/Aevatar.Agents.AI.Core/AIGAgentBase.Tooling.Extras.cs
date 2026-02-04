using System.Text.Json;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.ToolPacks;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.AI.Tool.MCP;
using Aevatar.Agents.AI.Tool.MCP.Abstractions;
using Aevatar.Agents.AI.Tool.MCP.Configuration;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.AI.Tool.Tools;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn.WebSearch;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core;

public abstract partial class AIGAgentBase
{
    // ---- Tool policy (runtime switches + YAML policy) ------------------------

    private ToolPolicyRuntime? _toolPolicyRuntime;
    private ToolPolicyRuntime ToolPolicy => _toolPolicyRuntime ??= new ToolPolicyRuntime(this);

    protected internal sealed record YamlToolPolicy(
        IReadOnlySet<string> Allowlist,
        IReadOnlySet<string> DangerousToolNames,
        bool EnableDangerousTools);

    private static readonly IReadOnlySet<string> DefaultYamlDangerousToolNames =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "python_exec", "skills_run_python" };

    private static readonly IReadOnlyCollection<string> DefaultYamlSkillToolNames =
    [
        "find_helpful_skills",
        "find_helpful_alls",
        "skills_load",
        "read_skill_document",
        "skills_files",
        "skills_read_file",
        "skills_run_python"
    ];

    protected virtual IReadOnlyCollection<string> GetYamlSkillToolNames() => DefaultYamlSkillToolNames;
    internal IReadOnlyCollection<string> InternalGetYamlSkillToolNames() => GetYamlSkillToolNames();

    protected virtual IReadOnlyCollection<string> GetYamlDefaultSkillRoots()
        => new[] { AgentYamlConfigLoader.GetSkillsDirectory() };

    internal IReadOnlyCollection<string> InternalGetYamlDefaultSkillRoots() => GetYamlDefaultSkillRoots();

    protected virtual IReadOnlySet<string> GetYamlDangerousToolNames() => DefaultYamlDangerousToolNames;
    internal IReadOnlySet<string> InternalGetYamlDangerousToolNames() => GetYamlDangerousToolNames();

    protected virtual YamlToolPolicy BuildYamlToolPolicy(AgentYamlConfig yaml) => ToolPolicy.BuildYamlToolPolicy(yaml);
    internal YamlToolPolicy InternalBuildYamlToolPolicy(AgentYamlConfig yaml) => BuildYamlToolPolicy(yaml);

    protected virtual void LogYamlToolPolicyDecision(AgentYamlConfig yaml, YamlToolPolicy policy)
        => ToolPolicy.LogYamlToolPolicyDecision(yaml, policy);

    internal void InternalLogYamlToolPolicyDecision(AgentYamlConfig yaml, YamlToolPolicy policy)
        => LogYamlToolPolicyDecision(yaml, policy);

    protected virtual bool ShouldEnableDangerousToolsFromYaml(
        AgentYamlConfig yaml,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> dangerousToolNames)
        => allowlist is { Count: > 0 } && allowlist.Any(dangerousToolNames.Contains);

    internal bool InternalShouldEnableDangerousToolsFromYaml(
        AgentYamlConfig yaml,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> dangerousToolNames)
        => ShouldEnableDangerousToolsFromYaml(yaml, allowlist, dangerousToolNames);

    protected virtual IReadOnlySet<string> BuildToolAllowlistFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames)
        => ToolPolicy.BuildToolAllowlistFromYaml(yaml, skillToolNames);

    internal IReadOnlySet<string> InternalBuildToolAllowlistFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames)
        => BuildToolAllowlistFromYaml(yaml, skillToolNames);

    protected virtual IReadOnlyCollection<string> GetSkillToolsAutoIncludedFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames)
        => yaml?.Skills is { Count: > 0 } && skillToolNames != null ? skillToolNames : Array.Empty<string>();

    private async Task<ToolExecutionResult> ExecuteAllowedToolAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
        => await Tooling.ExecuteAllowedToolAsync(toolName, args, executionContext, llmRequest, cancellationToken);

    private bool IsToolAllowedByPolicy(ToolDefinition tool) => ToolPolicy.IsToolAllowedByPolicy(tool);
    private string BuildToolPolicyDenyReason(ToolDefinition tool) => ToolPolicy.BuildToolPolicyDenyReason(tool);

    private void TryApplyToolAllowlistFromSkillsLoadResult(
        AevatarLLMRequest llmRequest,
        string toolName,
        string? toolResultJson)
        => ToolPolicy.TryApplyToolAllowlistFromSkillsLoadResult(llmRequest, toolName, toolResultJson);

    ILogger IToolPolicyHost.Logger => Logger;
    bool IToolPolicyHost.AllowInternalTools => AllowInternalTools;
    bool IToolPolicyHost.AllowDangerousTools => AllowDangerousTools;
    IReadOnlyCollection<string> IToolPolicyHost.GetYamlSkillToolNames() => GetYamlSkillToolNames();
    IReadOnlyCollection<string> IToolPolicyHost.GetYamlDefaultSkillRoots() => GetYamlDefaultSkillRoots();
    IReadOnlySet<string> IToolPolicyHost.GetYamlDangerousToolNames() => GetYamlDangerousToolNames();

    bool IToolPolicyHost.ShouldEnableDangerousToolsFromYaml(
        AgentYamlConfig yaml,
        IReadOnlySet<string> allowlist,
        IReadOnlySet<string> dangerousToolNames)
        => ShouldEnableDangerousToolsFromYaml(yaml, allowlist, dangerousToolNames);

    IReadOnlyCollection<string> IToolPolicyHost.GetSkillToolsAutoIncludedFromYaml(
        AgentYamlConfig yaml,
        IReadOnlyCollection<string> skillToolNames)
        => GetSkillToolsAutoIncludedFromYaml(yaml, skillToolNames);

    // ---- Agent Skills -------------------------------------------------------

    private AgentSkillsRuntime? _agentSkillsRuntime;
    private AgentSkillsRuntime AgentSkillsRuntime => _agentSkillsRuntime ??= new AgentSkillsRuntime(this);

    public bool EnableAgentSkills { get; set; } = true;
    public bool AgentSkillsAutoRegisterDotNetFileTools { get; set; } = true;

    public void AddAgentSkillsRoot(string rootDirectory) => AgentSkillsRuntime.AddRoot(rootDirectory);

    public async Task ConfigureAgentSkillsAsync(
        IEnumerable<string> roots,
        bool enable = true,
        CancellationToken cancellationToken = default)
    {
        AgentSkillsRuntime.ReplaceRoots(roots);
        EnableAgentSkills = enable;

        await InitializeToolsAsync(cancellationToken);
        await RegisterAgentSkillsToolsAsync(cancellationToken);
    }

    private IReadOnlyList<string> GetEffectiveAgentSkillsRoots() => AgentSkillsRuntime.GetEffectiveRoots();

    protected async Task RegisterAgentSkillsToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!EnableAgentSkills)
            return;

        EnsureToolManagerInitialized();

        await AgentSkillsRuntime.RegisterToolsAsync(
            enabled: EnableAgentSkills,
            agentType: GetType().Name,
            toolManager: ToolManager,
            logger: Logger,
            defaultMaxToolOutputChars: HookOptions.MaxToolOutputChars,
            defaultRegisterToolsOnLoad: AgentSkillsAutoRegisterDotNetFileTools,
            cancellationToken: cancellationToken);

        await RefreshToolCachesAsync(cancellationToken);
    }

    // ---- MCP ---------------------------------------------------------------

    public bool EnableMcpServers { get; set; } = true;
    public bool McpRetryOnEachChat { get; set; } = true;
    public TimeSpan McpRetryMinInterval { get; set; } = TimeSpan.FromSeconds(30);

    protected IConfiguration? HostConfiguration { get; set; }
    internal IConfiguration? InternalHostConfiguration => HostConfiguration;

    private McpRuntime? _mcpRuntime;
    private McpRuntime McpRuntime => _mcpRuntime ??= new McpRuntime(
        this,
        new DefaultMcpClientFactory(),
        new SystemMcpClock());

    protected virtual Task<bool> RegisterMcpServersFromConfigurationBestEffortAsync(
        bool isRetry,
        CancellationToken cancellationToken = default)
        => McpRuntime.RegisterFromConfigurationBestEffortAsync(isRetry, cancellationToken);

    protected Task TryReconnectMcpOnChatAsync(CancellationToken ct) => McpRuntime.TryReconnectOnChatAsync(ct);

    public Task<(bool Ok, string? Error)> ReconnectMcpAsync(CancellationToken ct = default)
        => McpRuntime.ReconnectAsync(ct);

    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        await McpRuntime.DisposeBestEffortAsync();
        await base.OnDeactivateAsync(ct);
    }

    ILogger IMcpRuntimeHost.Logger => Logger;
    bool IMcpRuntimeHost.EnableMcpServers => EnableMcpServers;
    bool IMcpRuntimeHost.McpRetryOnEachChat => McpRetryOnEachChat;
    TimeSpan IMcpRuntimeHost.McpRetryMinInterval => McpRetryMinInterval;
    IConfiguration? IMcpRuntimeHost.HostConfiguration => HostConfiguration;
    IAevatarToolManager IMcpRuntimeHost.ToolManager => ToolManager;
    Task IMcpRuntimeHost.InitializeToolsAsync(CancellationToken ct) => InitializeToolsAsync(ct);
    Task IMcpRuntimeHost.RefreshToolCachesAsync(CancellationToken ct) => RefreshToolCachesAsync(ct);

    Task IMcpRuntimeHost.RegisterMcpToolsAsync(
        string serverKey,
        IMCPClient mcpClient,
        string toolNamePrefix,
        MCPServerConfig config,
        CancellationToken ct)
        => ToolManager.RegisterMCPServerWithPrefixAsync(
            serverUrl: serverKey,
            mcpClient: mcpClient,
            toolNamePrefix: toolNamePrefix,
            config: config,
            logger: Logger,
            cancellationToken: ct);

    // ---- Tool packs (YAML) -------------------------------------------------

    private IReadOnlyList<IAevatarToolPack> _toolPacks = [];

    internal void InjectToolPacks(IEnumerable<IAevatarToolPack>? packs)
    {
        _toolPacks = packs?
            .Where(p => p != null)
            .GroupBy(p => p.PackName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList()
            ?? [];
    }

    internal async Task RegisterYamlToolPacksAsync(AgentYamlConfig yaml, CancellationToken ct)
    {
        if (yaml?.Tools is not { Count: > 0 } || _toolPacks.Count == 0)
            return;

        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in yaml.Tools)
        {
            var name = (raw ?? string.Empty).Trim();
            if (name.Length > 0)
                requested.Add(name);
        }
        if (requested.Count == 0)
            return;

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var registered = await GetRegisteredToolsAsync();
            foreach (var tool in registered)
                existing.Add(tool.Name);
        }
        catch
        {
            // best-effort
        }

        var context = new AevatarToolPackContext(
            Configuration: InternalHostConfiguration,
            WorkingDirectory: Directory.GetCurrentDirectory(),
            Logger: Logger ?? NullLogger.Instance);

        foreach (var pack in _toolPacks)
        {
            foreach (var toolName in requested)
            {
                if (existing.Contains(toolName))
                    continue;

                if (!pack.TryCreateTool(toolName, context, out var tool))
                    continue;

                await RegisterToolAsync(tool, Logger, ct);
                existing.Add(tool.Name);
            }
        }
    }

    // ---- Tool evolution (opt-in) -------------------------------------------

    private ToolEvolutionOptions _toolEvolutionOptions = new();
    private ToolMetricsStore? _toolMetricsStore;
    private ToolEvolutionRegistry? _toolEvolutionRegistry;

    public ToolEvolutionOptions ToolEvolutionOptions
    {
        get => _toolEvolutionOptions;
        set
        {
            _toolEvolutionOptions = value ?? new ToolEvolutionOptions();
            HookRuntime.Invalidate();
        }
    }

    internal void InjectToolEvolutionOptions(ToolEvolutionOptions? options)
    {
        if (options == null)
            return;

        ToolEvolutionOptions = options;
        _toolEvolutionRegistry = null;
        _toolMetricsStore = null;
        HookRuntime.Invalidate();

        if (!ToolEvolutionOptions.Enabled)
            return;

        EnsureToolManagerInitialized();
        if (ToolManager is AevatarToolManager manager)
        {
            var registry = CreateToolEvolutionRegistry(manager);
            manager.EvolutionRegistry = registry;
            _toolEvolutionRegistry = registry;
        }
    }

    protected ToolMetricsStore ToolMetricsStore => _toolMetricsStore ??= new ToolMetricsStore();
    protected ToolEvolutionRegistry? ToolEvolutionRegistry => _toolEvolutionRegistry;

    private ToolEvolutionRegistry? CreateToolEvolutionRegistry(IAevatarToolManager toolManager)
        => !ToolEvolutionOptions.Enabled
            ? null
            : new ToolEvolutionRegistry(toolManager, ToolMetricsStore, ToolEvolutionOptions, Logger);

    private async Task AppendToolFeedbackMemoryAsync(ToolExecutionFeedback feedback, CancellationToken ct)
    {
        if (MemoryStore == null)
            return;

        try
        {
            var scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = Id.ToString() };
            var memoryId = BuildMemoryId(scope);
            var entry = new MemoryEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                MemoryId = memoryId,
                Scope = scope,
                AgentId = Id.ToString(),
                Role = "tool",
                Content = JsonSerializer.Serialize(new
                {
                    tool_name = feedback.ToolName,
                    tool_version = feedback.ToolVersion,
                    tool_call_id = feedback.ToolCallId,
                    success = feedback.Success,
                    error_message = feedback.ErrorMessage,
                    duration_ms = feedback.DurationMs
                }),
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            entry.Tags["tool_name"] = feedback.ToolName ?? string.Empty;
            entry.Tags["tool_version"] = feedback.ToolVersion ?? string.Empty;
            entry.Tags["success"] = feedback.Success ? "true" : "false";
            if (!string.IsNullOrWhiteSpace(feedback.ErrorCode))
                entry.Tags["error_code"] = feedback.ErrorCode;

            await MemoryStore.AppendAsync(entry, ct);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "AppendToolFeedbackMemoryAsync failed (best-effort).");
        }
    }

    // ---- Web search (tool) -------------------------------------------------

    protected IHttpClientFactory? HttpClientFactory { get; set; }
    protected IAevatarWebSearchProvider? WebSearchProvider { get; set; }

    protected virtual async Task<bool> RegisterWebSearchToolBestEffortAsync(CancellationToken cancellationToken = default)
    {
        var provider = WebSearchProvider
                       ?? WebSearchProviderFactory.TryCreateFromConfigurationBestEffort(HostConfiguration, HttpClientFactory);
        if (provider == null)
            return false;

        await RegisterToolAsync(
            new WebSearchTool(provider, new LoggerAdapter<WebSearchTool>(Logger)),
            cancellationToken: cancellationToken);

        return true;
    }
}

