using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages;
using Aevatar.Agents.AI.WithTool.Tools;
using Aevatar.Agents.AI.WithTool.Tools.BuiltIn;
using Aevatar.Agents.AI.WithTool.Tools.CustomTools;
using Aevatar.Agents.AI.WithTool.Tools.CoreTools;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    /// <summary>
    /// Safety switch (default: false):
    /// - When false, tools marked <c>RequiresConfirmation</c> or <c>IsDangerous</c> will not be exposed/executed.
    /// - Explicitly enable in derived agents when you want side-effect tools (HTTP, publish_event, skills_load, etc).
    /// </summary>
    public bool AllowDangerousTools { get; set; }

    /// <summary>
    /// Safety switch (default: true):
    /// - Controls tools marked <c>RequiresInternalAccess</c> (state query, event publishing, skills tools...).
    /// - If you want to hard-disable internal introspection tools, set to false.
    /// </summary>
    public bool AllowInternalTools { get; set; } = true;

    /// <summary>
    /// Optional CQRS query facade (injected by runtime).
    /// <para/>
    /// Used by tools (e.g. <c>search_memory</c>) to query projected state read-model (e.g. Elasticsearch).
    /// </summary>
    protected IStateQueryService? CqrsStateQueryService { get; set; }

    // ============================================================
    //  Tool system (merged from AIGAgentWithToolBase)
    // ============================================================

    private IAevatarToolManager? _toolManager;
    private IReadOnlyList<ToolDefinition> _registeredToolsCache = Array.Empty<ToolDefinition>();

    private IReadOnlyList<AevatarFunctionDefinition> _functionDefinitionsCache =
        Array.Empty<AevatarFunctionDefinition>();

    private readonly SemaphoreSlim _toolInitSemaphore = new(1, 1);
    private bool _toolsInitialized;

    /// <summary>
    /// Gets or sets the tool manager (DI injectable).
    /// </summary>
    protected IAevatarToolManager ToolManager
    {
        get
        {
            EnsureToolManagerInitialized();
            return _toolManager!;
        }
        set
        {
            _toolManager = value ?? throw new ArgumentNullException(nameof(value));
            _toolsInitialized = false;
        }
    }

    private void EnsureToolManagerInitialized()
    {
        if (_toolManager != null)
            return;

        _toolManager = CreateToolManager();
    }

    /// <summary>
    /// Creates the tool manager. Override to customize.
    /// </summary>
    protected virtual IAevatarToolManager CreateToolManager()
    {
        var logger = new LoggerAdapter<AevatarToolManager>(Logger);
        return new AevatarToolManager(logger);
    }

    /// <summary>
    /// Initialize tools once per agent lifetime (idempotent + concurrency-safe).
    /// </summary>
    protected virtual async Task InitializeToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_toolsInitialized)
            return;

        await _toolInitSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_toolsInitialized)
                return;

            EnsureToolManagerInitialized();

            await RegisterToolsAsync(cancellationToken);
            await RefreshToolCachesAsync(cancellationToken);

            _toolsInitialized = true;
        }
        finally
        {
            _toolInitSemaphore.Release();
        }
    }

    /// <summary>
    /// Register tools for this agent. Default registers built-in tools so every AI agent is tool-capable.
    /// Override to add/remove tools (call base to keep defaults).
    /// </summary>
    protected virtual async Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        // Core: state query
        await RegisterToolAsync(new StateQueryTool(), cancellationToken: cancellationToken);

        // Core: event publishing (requires PublishEventCallback)
        await RegisterToolAsync(new EventPublisherTool(), cancellationToken: cancellationToken);

        // Built-in: memory search (uses CQRS read-model + State snapshot)
        await RegisterToolAsync(
            new AevatarMemorySearchTool(
                new LoggerAdapter<AevatarMemorySearchTool>(Logger),
                CqrsStateQueryService,
                MemoryStore,
                MemoryVectorIndex),
            cancellationToken: cancellationToken);

        // Agent Skills (agentskills.io) - disabled by default via EnableAgentSkills
        await RegisterAgentSkillsToolsAsync(cancellationToken);
    }

    /// <summary>
    /// Import a single-file C# "skill" as a tool via <c>dotnet run --file</c>.
    /// </summary>
    public async Task RegisterDotNetFileSkillAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var tool = await DotNetFileSkillTool.LoadFromFileAsync(
            filePath,
            logger: Logger,
            cancellationToken: cancellationToken);

        await RegisterToolAsync(tool, Logger, cancellationToken);
    }

    /// <summary>
    /// Helper to register a tool with current agent context.
    /// </summary>
    protected async Task RegisterToolAsync(
        IAevatarTool tool,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);

        EnsureToolManagerInitialized();

        var context = BuildToolRegistrationContext();
        var toolDefinition = tool.CreateToolDefinition(context, logger ?? Logger);
        await ToolManager.RegisterToolAsync(toolDefinition, cancellationToken);

        await RefreshToolCachesAsync(cancellationToken);
    }

    private ToolContext BuildToolRegistrationContext()
    {
        return new ToolContext
        {
            AgentId = Id.ToString(),
            AgentType = GetType().FullName ?? GetType().Name,
            GetStateCallback = () => GetState(),
            GenerateEmbeddingsAsync = HasEmbeddingGenerator
                ? (inputs, ct) => GenerateEmbeddingsAsync(inputs, cancellationToken: ct)
                : null,
            PublishEventCallback = msg => PublishToolEventAsync(msg, EventDirection.Down, CancellationToken.None),
            PublishEventWithDirectionCallback = (msg, direction, ct) => PublishToolEventAsync(msg, direction, ct),
            GetSessionIdCallback = () => Id.ToString(),
            Logger = Logger
        };
    }

    private ToolExecutionContext BuildToolExecutionContext(string sessionId, CancellationToken cancellationToken)
    {
        return new ToolExecutionContext
        {
            AgentId = Id.ToString(),
            ToolManager = ToolManager,
            PublishEventCallback = msg => PublishToolEventAsync(msg, EventDirection.Down, cancellationToken),
            PublishEventWithDirectionCallback = (msg, direction, ct) => PublishToolEventAsync(msg, direction, ct),
            Logger = Logger,
            GetSessionId = () => sessionId,
            AllowInternalTools = AllowInternalTools,
            AllowDangerousTools = AllowDangerousTools
        };
    }

    private Task<string> PublishToolEventAsync(IMessage message, EventDirection direction, CancellationToken ct)
    {
        // Use dynamic to preserve the runtime message type for metrics/logging (instead of "IMessage").
        return PublishAsync((dynamic)message, direction, ct);
    }

    /// <summary>
    /// Refresh internal tool caches (definitions + function schema) used for LLM tool calling.
    /// <para/>
    /// NOTE: Derived agents may call this after dynamically registering tools (e.g. MCP reconnect).
    /// </summary>
    protected async Task RefreshToolCachesAsync(CancellationToken cancellationToken = default)
    {
        if (_toolManager == null)
        {
            _registeredToolsCache = Array.Empty<ToolDefinition>();
            _functionDefinitionsCache = Array.Empty<AevatarFunctionDefinition>();
            return;
        }

        _registeredToolsCache = await ToolManager.GetAvailableToolsAsync(cancellationToken) ?? [];
        _functionDefinitionsCache = await ToolManager.GenerateFunctionDefinitionsAsync(cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetRegisteredToolsAsync()
    {
        EnsureToolManagerInitialized();
        return await ToolManager.GetAvailableToolsAsync() ?? [];
    }

    protected async Task<bool> HasToolsAsync()
    {
        var tools = await GetRegisteredToolsAsync();
        return tools.Count > 0;
    }

    /// <summary>
    /// Execute a tool by name with parameters.
    /// </summary>
    protected Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        EnsureToolManagerInitialized();
        return ToolManager.ExecuteToolAsync(toolName, parameters, context, cancellationToken);
    }

    private string BuildToolInstructionBlock()
    {
        if (_registeredToolsCache.Count == 0)
            return string.Empty;

        var visibleTools = _registeredToolsCache
            .Where(IsToolAllowedByPolicy)
            .ToList();

        if (visibleTools.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("You can call tools (function calling). Available tools:");
        foreach (var tool in visibleTools)
        {
            sb.AppendLine($"- {tool.Name}: {tool.Description}");
        }

        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- If a tool can answer more accurately/efficiently, call the tool first, then answer.");

        if (visibleTools.Any(t => string.Equals(t.Name, "search_memory", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("- If you need details from earlier conversation, call 'search_memory' before answering.");
        }

        if (visibleTools.Any(t => string.Equals(t.Name, "skills_list", StringComparison.OrdinalIgnoreCase)) &&
            visibleTools.Any(t => string.Equals(t.Name, "skills_load", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine(
                "- If you need a procedural/domain skill, call 'skills_list' then 'skills_load' before acting.");
        }

        return sb.ToString().TrimEnd();
    }

    private void AttachToolsToRequest(AevatarLLMRequest llmRequest)
    {
        if (_functionDefinitionsCache.Count == 0)
            return;

        // Apply runtime policy: keep dangerous tools hidden unless explicitly enabled.
        var allowedNames = _registeredToolsCache
            .Where(IsToolAllowedByPolicy)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowedNames.Count == 0)
        {
            llmRequest.Functions = new List<AevatarFunctionDefinition>();
            return;
        }

        // Optional: apply a runtime allowlist (e.g. from Agent Skills front matter `allowed-tools`).
        // This keeps the model from seeing / calling tools outside the allowed set.
        if (AIGAgentKeys.TryGetToolAllowlist(llmRequest, out var allowlist) && allowlist.Count > 0)
        {
            llmRequest.Functions = _functionDefinitionsCache
                .Where(d => allowlist.Contains(d.Name) && allowedNames.Contains(d.Name))
                .ToList();
            return;
        }

        llmRequest.Functions = _functionDefinitionsCache
            .Where(d => allowedNames.Contains(d.Name))
            .ToList();
    }

    /// <summary>
    /// Logger adapter to convert ILogger to ILogger{T}.
    /// </summary>
    private sealed class LoggerAdapter<T> : ILogger<T>
    {
        private readonly ILogger _inner;

        public LoggerAdapter(ILogger inner) => _inner = inner ?? NullLogger.Instance;

        public IDisposable? BeginScope<TLogState>(TLogState state) where TLogState : notnull
            => _inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        public void Log<TLogState>(
            LogLevel logLevel,
            EventId eventId,
            TLogState state,
            Exception? exception,
            Func<TLogState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}