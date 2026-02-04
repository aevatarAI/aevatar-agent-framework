using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.AI.Core.Hooks.BuiltIn;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.AI.Tool.Tools;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Aevatar.Agents.AI.Tool.Tools.CoreTools;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    // Safety: side-effect tools (dangerous/confirmation).
    public bool AllowDangerousTools { get; set; } = true;

    // Safety: internal tools (state introspection, event publishing, skills tools...).
    public bool AllowInternalTools { get; set; } = true;

    // Optional read-model query service for tools (e.g. search_memory).
    protected IStateQueryService? CqrsStateQueryService { get; set; }

    private ToolingRuntime? _toolingRuntime;
    private ToolingRuntime Tooling => _toolingRuntime ??= new ToolingRuntime(this, this);

    private IReadOnlyList<ToolDefinition> RegisteredToolsCache => Tooling.RegisteredToolsCache;
    private IReadOnlyList<AevatarFunctionDefinition> FunctionDefinitionsCache => Tooling.FunctionDefinitionsCache;

    private AIGAgentToolsRuntime? _agentToolsRuntime;
    private AIGAgentToolsRuntime AgentTools => _agentToolsRuntime ??= new AIGAgentToolsRuntime(this);

    protected IAevatarToolManager ToolManager
    {
        get => Tooling.ToolManager;
        set => Tooling.ToolManager = value ?? throw new ArgumentNullException(nameof(value));
    }

    private void EnsureToolManagerInitialized() => Tooling.EnsureToolManagerInitialized();

    protected virtual IAevatarToolManager CreateToolManager()
    {
        var logger = new LoggerAdapter<AevatarToolManager>(Logger);
        var manager = new AevatarToolManager(logger);

        var registry = CreateToolEvolutionRegistry(manager);
        if (registry != null)
        {
            manager.EvolutionRegistry = registry;
            _toolEvolutionRegistry = registry;
        }

        return manager;
    }

    /// <summary>
    /// Initialize tools once per agent lifetime (idempotent + concurrency-safe).
    /// </summary>
    protected virtual async Task InitializeToolsAsync(CancellationToken cancellationToken = default)
    {
        await Tooling.InitializeToolsAsync(cancellationToken);
    }

    /// <summary>
    /// Register tools for this agent. Default registers built-in tools so every AI agent is tool-capable.
    /// Override to add/remove tools (call base to keep defaults).
    /// </summary>
    protected virtual async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        var contributors = new IAIGAgentToolContributor[]
        {
            new CoreToolsContributor(new CoreToolsContributorHost(this)),
            new MemorySearchToolsContributor(new MemorySearchToolsContributorHost(this)),

            new WebSearchToolsContributor(new WebSearchToolsContributorHost(this)),
            new AgentSkillsToolsContributor(new AgentSkillsToolsContributorHost(this)),
            new McpToolsContributor(new McpToolsContributorHost(this))
        };

        // Core stage: fail-fast (tools are expected to be available for normal operation).
        foreach (var c in contributors.Where(x => x.Stage == ToolContributionStage.Core))
        {
            await c.ContributeAsync(cancellationToken);
        }

        // Best-effort stage: run in parallel, do not block agent startup.
        var bestEffort = contributors
            .Where(x => x.Stage == ToolContributionStage.BestEffort)
            .Select(async c =>
            {
                try
                {
                    await c.ContributeAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Tool contributor '{Name}' failed (best-effort).", c.GetType().Name);
                }
            })
            .ToArray();

        await Task.WhenAll(bestEffort);
    }

    private sealed class CoreToolsContributorHost(AIGAgentBase owner) : ICoreToolsContributorHost
    {
        private readonly AIGAgentBase _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        public async Task RegisterToolAsync(IAevatarTool tool, CancellationToken ct)
            => await _owner.RegisterToolDefinitionNoRefreshAsync(tool, _owner.Logger, ct);
    }

    private sealed class MemorySearchToolsContributorHost(AIGAgentBase owner) : IMemorySearchToolsContributorHost
    {
        private readonly AIGAgentBase _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        public ILogger Logger => _owner.Logger;
        public IStateQueryService? CqrsStateQueryService => _owner.CqrsStateQueryService;
        public IMemoryStore? MemoryStore => _owner.MemoryStore;
        public IMemoryVectorIndex? MemoryVectorIndex => _owner.MemoryVectorIndex;

        public async Task RegisterToolAsync(IAevatarTool tool, CancellationToken ct)
            => await _owner.RegisterToolDefinitionNoRefreshAsync(tool, _owner.Logger, ct);
    }

    private sealed class WebSearchToolsContributorHost(AIGAgentBase owner) : IWebSearchToolsContributorHost
    {
        private readonly AIGAgentBase _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        public Task<bool> RegisterWebSearchToolBestEffortAsync(CancellationToken ct)
            => _owner.RegisterWebSearchToolBestEffortAsync(ct);
    }

    private sealed class AgentSkillsToolsContributorHost(AIGAgentBase owner) : IAgentSkillsToolsContributorHost
    {
        private readonly AIGAgentBase _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        public Task RegisterAgentSkillsToolsAsync(CancellationToken ct)
            => _owner.RegisterAgentSkillsToolsAsync(ct);
    }

    private sealed class McpToolsContributorHost(AIGAgentBase owner) : IMcpToolsContributorHost
    {
        private readonly AIGAgentBase _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        public Task<bool> RegisterMcpServersFromConfigurationBestEffortAsync(bool isRetry, CancellationToken ct)
            => _owner.RegisterMcpServersFromConfigurationBestEffortAsync(isRetry, ct);
    }

    public async Task RegisterDotNetFileSkillAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await AgentTools.RegisterDotNetFileSkillAsync(filePath, cancellationToken);
    }

    public async Task RegisterPythonFileSkillAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await AgentTools.RegisterPythonFileSkillAsync(filePath, cancellationToken);
    }

    public async Task RegisterFileSkillsFromDirectoryAsync(
        string directoryPath,
        bool includeDotNet = true,
        bool includePython = true,
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        int maxFilesPerType = 64,
        bool requireManifestMarker = true,
        CancellationToken cancellationToken = default)
    {
        EnsureToolManagerInitialized();
        await AgentTools.RegisterFileSkillsFromDirectoryAsync(
            directoryPath,
            includeDotNet,
            includePython,
            searchOption,
            maxFilesPerType,
            requireManifestMarker,
            cancellationToken);
    }

    protected async Task RegisterToolAsync(
        IAevatarTool tool,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        await RegisterToolDefinitionAsync(tool, logger, cancellationToken);
    }

    protected async Task<ToolDefinition> RegisterToolDefinitionAsync(
        IAevatarTool tool,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        EnsureToolManagerInitialized();
        return await AgentTools.RegisterToolDefinitionAsync(
            tool,
            logger ?? Logger,
            refreshToolCaches: true,
            cancellationToken);
    }

    private async Task<ToolDefinition> RegisterToolDefinitionNoRefreshAsync(
        IAevatarTool tool,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        EnsureToolManagerInitialized();
        return await AgentTools.RegisterToolDefinitionAsync(
            tool,
            logger ?? Logger,
            refreshToolCaches: false,
            cancellationToken);
    }

    private ToolContext BuildToolRegistrationContext() => AgentTools.BuildToolRegistrationContext();

    private ToolExecutionContext BuildToolExecutionContext(string sessionId, CancellationToken cancellationToken)
        => AgentTools.BuildToolExecutionContext(sessionId, cancellationToken);

    private Task<string> PublishToolEventAsync(IMessage message, EventDirection direction, CancellationToken ct)
        // Use dynamic to preserve the runtime message type for metrics/logging (instead of "IMessage").
        => PublishAsync((dynamic)message, direction, ct);

    protected async Task RefreshToolCachesAsync(CancellationToken cancellationToken = default)
    {
        await Tooling.RefreshToolCachesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetRegisteredToolsAsync()
    {
        return await Tooling.GetRegisteredToolsAsync();
    }

    protected async Task<bool> HasToolsAsync()
    {
        var tools = await GetRegisteredToolsAsync();
        return tools.Count > 0;
    }

    protected async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        EnsureToolManagerInitialized();
        return await AgentTools.ExecuteToolAsync(toolName, parameters, context, cancellationToken);
    }

    private async Task<(AevatarLLMResponse FinalResponse, ToolCallInfo? ToolCall)> ExecuteToolCallLoopAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        AevatarLLMResponse initialResponse,
        CancellationToken cancellationToken)
    {
        return await Tooling.ExecuteToolCallLoopAsync(request, llmRequest, initialResponse, cancellationToken);
    }

    [EventHandler]
    protected virtual async Task HandleToolExecutionRequestEvent(ToolExecutionRequestEvent evt)
    {
        await InitializeToolsAsync();

        var parameters = Tooling.ParseToolArguments(evt.Arguments);
        var executionContext = BuildToolExecutionContext(evt.RequestId, CancellationToken.None);
        var result = await ExecuteToolAsync(evt.ToolName, parameters, executionContext, CancellationToken.None);

        await PublishAsync(new ToolExecutionResponseEvent
        {
            RequestId = evt.RequestId,
            ToolName = evt.ToolName,
            Success = result.IsSuccess,
            Result = result.Content ?? string.Empty,
            Error = result.ErrorMessage ?? string.Empty,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    public async Task<ToolExecutionResult> ExecuteToolForWorkflowAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureToolManagerInitialized();
        return await AgentTools.ExecuteToolForWorkflowAsync(toolName, parameters, sessionId, cancellationToken);
    }

    private string BuildToolInstructionBlock() => AgentTools.BuildToolInstructionBlock();

    private void AttachToolsToRequest(AevatarLLMRequest llmRequest) => AgentTools.AttachToolsToRequest(llmRequest);

    // ---- Hook / harness runtime (best-effort) --------------------------------

    private AIGAgentHookRuntime? _hookRuntime;
    private AIGAgentHookRuntime HookRuntime => _hookRuntime ??= new AIGAgentHookRuntime(this);

    protected IEnumerable<IAevatarAgentHook> AdditionalHooks { get; set; } = Array.Empty<IAevatarAgentHook>();
    protected AevatarAgentHookOptions HookOptions { get; set; } = new();

    protected virtual IEnumerable<IAevatarAgentHook> CreateBuiltInHooks()
    {
        var hooks = new List<IAevatarAgentHook>
        {
            new ExecutionTraceProgressHook((evt, ct) => PublishAsync(evt, EventDirection.Down, ct), Logger),
            new ToolOutputTruncationHook(),
            new ContextBudgetMonitorHook(Logger)
        };

        if (ToolEvolutionOptions.Enabled && ToolEvolutionOptions.EnableFeedbackHooks)
        {
            hooks.Add(new ToolExecutionHistoryHook(
                ToolEvolutionOptions,
                publishEvent: (evt, ct) => PublishAsync(evt, EventDirection.Down, ct),
                appendMemory: AppendToolFeedbackMemoryAsync,
                logger: Logger));

            hooks.Add(new ToolMetricsHook(
                ToolEvolutionOptions,
                ToolMetricsStore,
                publishSnapshot: (snapshot, ct) => PublishAsync(snapshot, EventDirection.Down, ct),
                logger: Logger));
        }

        return hooks;
    }

    internal void InjectHookOptions(AevatarAgentHookOptions? options)
    {
        if (options == null) return;
        HookOptions = options;
        HookRuntime.Invalidate();
    }

    internal void InjectAdditionalHooks(IEnumerable<IAevatarAgentHook>? hooks)
    {
        if (hooks == null) return;
        AdditionalHooks = hooks as IAevatarAgentHook[] ?? hooks.ToArray();
        HookRuntime.Invalidate();
    }

    protected override Task OnEventHandlerStartAsync(
        EventEnvelope envelope,
        EventHandlerMetadata handler,
        object? payload,
        CancellationToken ct)
        => HookRuntime.OnEventHandlerStartAsync(envelope, handler, payload, ct);

    protected override Task OnEventHandlerEndAsync(
        EventEnvelope envelope,
        EventHandlerMetadata handler,
        object? payload,
        TimeSpan duration,
        Exception? exception,
        CancellationToken ct)
        => HookRuntime.OnEventHandlerEndAsync(envelope, handler, payload, duration, exception, ct);

    private Task RunSessionStartHooksAsync(ChatRequest request, bool isStreaming, CancellationToken ct)
        => HookRuntime.RunSessionStartHooksAsync(request, isStreaming, ct);

    private Task RunStopHooksAsync(
        ChatRequest request,
        bool isStreaming,
        AevatarAgentHookStopStatus status,
        TimeSpan duration,
        Exception? exception)
        => HookRuntime.RunStopHooksAsync(request, isStreaming, status, duration, exception);

    private Task RunSessionEndHooksAsync(
        ChatRequest request,
        bool isStreaming,
        AevatarAgentHookStopStatus status,
        TimeSpan duration,
        Exception? exception)
        => HookRuntime.RunSessionEndHooksAsync(request, isStreaming, status, duration, exception);

    protected Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(
        string requestId,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
        => HookRuntime.GenerateLLMWithHooksAsync(requestId, llmRequest, cancellationToken);

    protected IAsyncEnumerable<AevatarLLMToken> GenerateLLMStreamWithHooksAsync(
        string requestId,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
        => HookRuntime.GenerateLLMStreamWithHooksAsync(requestId, llmRequest, ct);

    private Task<AevatarLLMResponse> GenerateLLMWithHooksAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
        => HookRuntime.GenerateLLMWithHooksAsync(request, llmRequest, ct);

    private Task<ToolExecutionResult> ExecuteAllowedToolWithHooksAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
        => HookRuntime.ExecuteAllowedToolWithHooksAsync(toolName, args, executionContext, llmRequest, ct);

    private static string ResolveEventRequestId(EventEnvelope envelope)
        => !string.IsNullOrWhiteSpace(envelope.Id)
            ? envelope.Id
            : !string.IsNullOrWhiteSpace(envelope.CorrelationId)
                ? envelope.CorrelationId
                : Guid.NewGuid().ToString("N");

    private static string? ResolveEventType(EventEnvelope envelope, object? payload)
    {
        if (payload is IMessage message)
        {
            var descriptorName = message.Descriptor?.FullName;
            if (!string.IsNullOrWhiteSpace(descriptorName))
                return descriptorName;
        }

        var typeUrl = envelope.Payload?.TypeUrl;
        if (string.IsNullOrWhiteSpace(typeUrl))
            return null;

        return typeUrl.Split('/').LastOrDefault() ?? typeUrl;
    }

    ILogger IAIGAgentHookRuntimeHost.Logger => Logger;
    string IAIGAgentHookRuntimeHost.AgentId => Id.ToString();
    string IAIGAgentHookRuntimeHost.AgentType => GetType().FullName ?? GetType().Name;
    bool IAIGAgentHookRuntimeHost.AllowInternalTools => AllowInternalTools;
    bool IAIGAgentHookRuntimeHost.AllowDangerousTools => AllowDangerousTools;
    IAevatarLLMProvider IAIGAgentHookRuntimeHost.LLMProvider => LLMProvider;
    IEnumerable<IAevatarAgentHook> IAIGAgentHookRuntimeHost.CreateBuiltInHooks() => CreateBuiltInHooks();
    IEnumerable<IAevatarAgentHook> IAIGAgentHookRuntimeHost.AdditionalHooks => AdditionalHooks;
    AevatarAgentHookOptions IAIGAgentHookRuntimeHost.HookOptions => HookOptions;
    void IAIGAgentHookRuntimeHost.AttachToolsToRequest(AevatarLLMRequest llmRequest) => AttachToolsToRequest(llmRequest);

    Task<ToolExecutionResult> IAIGAgentHookRuntimeHost.ExecuteAllowedToolAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
        => ExecuteAllowedToolAsync(toolName, args, executionContext, llmRequest, ct);

    Task IAIGAgentHookRuntimeHost.PublishAsync(IMessage message, EventDirection direction, CancellationToken ct)
        => PublishAsync((dynamic)message, direction, ct);

    string? IAIGAgentHookRuntimeHost.ResolveEventType(EventEnvelope envelope, object? payload)
        => ResolveEventType(envelope, payload);

    string IAIGAgentHookRuntimeHost.ResolveEventRequestId(EventEnvelope envelope)
        => ResolveEventRequestId(envelope);

    IAevatarToolManager IToolingInitHost.CreateToolManager() => CreateToolManager();
    Task IToolingInitHost.RegisterToolsAsync(CancellationToken cancellationToken) => RegisterToolsAsync(cancellationToken);

    ILogger IToolingLoopHost.Logger => Logger;
    IAevatarLLMProvider IToolingLoopHost.LLMProvider => LLMProvider;
    bool IToolingLoopHost.EnableChatHistoryInState => EnableChatHistoryInState;

    ToolExecutionContext IToolingLoopHost.BuildToolExecutionContext(string sessionId, CancellationToken cancellationToken)
        => BuildToolExecutionContext(sessionId, cancellationToken);

    Task<ToolExecutionResult> IToolingLoopHost.ExecuteAllowedToolWithHooksAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
        => ExecuteAllowedToolWithHooksAsync(toolName, args, executionContext, llmRequest, cancellationToken);

    Task<ToolExecutionResult> IToolingLoopHost.ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext executionContext,
        CancellationToken cancellationToken)
        => ExecuteToolAsync(toolName, parameters, executionContext, cancellationToken);

    Task IToolingLoopHost.PublishAsync(IMessage message, EventDirection direction, CancellationToken ct)
        => PublishAsync((dynamic)message, direction, ct);

    void IToolingLoopHost.AttachToolsToRequest(AevatarLLMRequest llmRequest) => AttachToolsToRequest(llmRequest);

    void IToolingLoopHost.TryApplyToolAllowlistFromSkillsLoadResult(
        AevatarLLMRequest llmRequest,
        string toolName,
        string? toolResultJson)
        => TryApplyToolAllowlistFromSkillsLoadResult(llmRequest, toolName, toolResultJson);

    bool IToolingLoopHost.IsToolAllowedByPolicy(ToolDefinition tool) => IsToolAllowedByPolicy(tool);
    string IToolingLoopHost.BuildToolPolicyDenyReason(ToolDefinition tool) => BuildToolPolicyDenyReason(tool);

    void IToolingLoopHost.AddMessageToHistory(AevatarChatMessage message) => AddMessageToHistory(message);

    Task<AevatarLLMResponse> IToolingLoopHost.GenerateLLMWithHooksAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
        => GenerateLLMWithHooksAsync(request, llmRequest, cancellationToken);

    string IAIGAgentToolsRuntimeHost.AgentId => Id.ToString();
    string IAIGAgentToolsRuntimeHost.AgentType => GetType().FullName ?? GetType().Name;
    ILogger IAIGAgentToolsRuntimeHost.Logger => Logger;
    bool IAIGAgentToolsRuntimeHost.AllowInternalTools => AllowInternalTools;
    bool IAIGAgentToolsRuntimeHost.AllowDangerousTools => AllowDangerousTools;
    IAevatarToolManager IAIGAgentToolsRuntimeHost.ToolManager => ToolManager;
    IReadOnlyList<ToolDefinition> IAIGAgentToolsRuntimeHost.RegisteredToolsCache => RegisteredToolsCache;
    IReadOnlyList<AevatarFunctionDefinition> IAIGAgentToolsRuntimeHost.FunctionDefinitionsCache => FunctionDefinitionsCache;
    bool IAIGAgentToolsRuntimeHost.HasEmbeddingGenerator => HasEmbeddingGenerator;

    async Task<IReadOnlyList<Embedding<float>>> IAIGAgentToolsRuntimeHost.GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken ct)
        => await GenerateEmbeddingsAsync(inputs, cancellationToken: ct);

    IMessage IAIGAgentToolsRuntimeHost.GetState() => GetState();

    Task IAIGAgentToolsRuntimeHost.PublishAsync(IMessage message, EventDirection direction, CancellationToken ct)
        => PublishAsync((dynamic)message, direction, ct);

    Task<string> IAIGAgentToolsRuntimeHost.PublishToolEventAsync(IMessage message, EventDirection direction, CancellationToken ct)
        => PublishToolEventAsync(message, direction, ct);

    bool IAIGAgentToolsRuntimeHost.IsToolAllowedByPolicy(ToolDefinition tool) => IsToolAllowedByPolicy(tool);
    Task IAIGAgentToolsRuntimeHost.InitializeToolsAsync(CancellationToken ct) => InitializeToolsAsync(ct);
    Task IAIGAgentToolsRuntimeHost.RefreshToolCachesAsync(CancellationToken ct) => RefreshToolCachesAsync(ct);

    Task<ToolExecutionResult> IAIGAgentToolsRuntimeHost.ExecuteAllowedToolWithHooksAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken ct)
        => ExecuteAllowedToolWithHooksAsync(toolName, args, executionContext, llmRequest, ct);
}