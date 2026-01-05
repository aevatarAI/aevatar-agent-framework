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
    private const string ToolAllowlistContextKey = "aevatar.allowed_tools";
    private const string ToolAllowlistSourceSkillContextKey = "aevatar.allowed_tools.source_skill";

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
    private IReadOnlyList<AevatarFunctionDefinition> _functionDefinitionsCache = Array.Empty<AevatarFunctionDefinition>();

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
            sb.AppendLine("- If you need a procedural/domain skill, call 'skills_list' then 'skills_load' before acting.");
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
        if (TryGetToolAllowlist(llmRequest, out var allowlist) && allowlist.Count > 0)
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

    private async Task<(AevatarLLMResponse FinalResponse, ToolCallInfo? ToolCall)> ExecuteToolCallLoopAsync(
        ChatRequest request,
        AevatarLLMRequest llmRequest,
        AevatarLLMResponse initialResponse,
        CancellationToken cancellationToken)
    {
        const int MaxRounds = 8;
        const int MaxSameCallAttempts = 2;

        var current = initialResponse;
        ToolCallInfo? lastToolCall = null;
        var toolCallAttempts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var round = 0; round < MaxRounds && current.AevatarFunctionCall != null; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var functionCall = current.AevatarFunctionCall;
            if (functionCall == null)
            {
                break;
            }

            await InitializeToolsAsync(cancellationToken);

            var args = ParseToolArguments(functionCall.Arguments);
            var callId = EnsureToolCallId(functionCall);
            var signature = BuildToolCallSignature(functionCall.Name, args);
            toolCallAttempts.TryGetValue(signature, out var attempts);
            attempts++;
            toolCallAttempts[signature] = attempts;

            // If the model keeps requesting the exact same tool call, force it to answer without tools.
            if (attempts > MaxSameCallAttempts)
            {
                Logger.LogWarning(
                    "Detected repeated tool call (attempt {Attempt}/{MaxAttempt}) for {ToolName}. Forcing final answer. RequestId={RequestId}",
                    attempts, MaxSameCallAttempts, functionCall.Name, request.RequestId);

                current = await ForceModelToAnswerWithoutToolsAsync(
                    llmRequest,
                    reason: $"Repeated tool call '{functionCall.Name}' with identical arguments.",
                    cancellationToken);
                break;
            }
            var executionContext = BuildToolExecutionContext(request.RequestId, cancellationToken);

            // Hard guard: if a tool allowlist is active, deny executing tools outside it.
            // This is used by Agent Skills `allowed-tools` to constrain what the model can do.
            var toolResult = await ExecuteAllowedToolAsync(
                functionCall.Name,
                args,
                executionContext,
                llmRequest,
                cancellationToken);

            lastToolCall = new ToolCallInfo
            {
                ToolName = functionCall.Name,
                Result = toolResult.Content ?? string.Empty
            };

            string? reasoningContent = null;
            if (round == 0 && initialResponse.Metadata != null && 
                initialResponse.Metadata.TryGetValue("reasoning_content", out var rObj) && 
                rObj is string rStr)
            {
                reasoningContent = rStr;
            }

            foreach (var (k, v) in args)
            {
                lastToolCall.Arguments[k] = v?.ToString() ?? string.Empty;
            }

            var toolCallMsg = CreateToolCallMessage(functionCall, reasoningContent);
            var toolResultMsg = CreateToolResultMessage(functionCall.Name, callId, toolResult);

            llmRequest.Messages.Add(toolCallMsg);
            llmRequest.Messages.Add(toolResultMsg);

            // Optional: persist tool transcript into State.History (only when history switch is on).
            if (EnableChatHistoryInState)
            {
                AddMessageToHistory(toolCallMsg);
                AddMessageToHistory(toolResultMsg);
            }

            // Publish tool execution response (useful for UI/telemetry)
            await PublishAsync(new ToolExecutionResponseEvent
            {
                RequestId = request.RequestId,
                ToolName = functionCall.Name,
                Success = toolResult.IsSuccess,
                Result = toolResult.Content ?? string.Empty,
                Error = toolResult.ErrorMessage ?? string.Empty,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, ct: cancellationToken);

            // If skills_load was executed, it may update the tool allowlist dynamically.
            TryApplyToolAllowlistFromSkillsLoadResult(llmRequest, functionCall.Name, toolResult.Content);

            // Tool set might have changed (e.g., skills_load imported new tools) - refresh function defs.
            await RefreshToolCachesAsync(cancellationToken);
            AttachToolsToRequest(llmRequest);

            // Call LLM again with tool result appended
            current = await LLMProvider.GenerateAsync(llmRequest, cancellationToken);
        }

        if (current.AevatarFunctionCall != null)
        {
            Logger.LogWarning(
                "Tool call loop exceeded max rounds ({MaxRounds}). Forcing final answer without tools. RequestId={RequestId}",
                MaxRounds, request.RequestId);

            current = await ForceModelToAnswerWithoutToolsAsync(
                llmRequest,
                reason: $"Exceeded max tool call rounds ({MaxRounds}).",
                cancellationToken);

            // If provider still returns a function call even when no tools are attached, fall back to a safe text reply.
            if (current.AevatarFunctionCall != null)
            {
                Logger.LogWarning(
                    "Provider still returned a tool call after tools were detached. Returning fallback content. RequestId={RequestId}",
                    request.RequestId);

                current = new AevatarLLMResponse
                {
                    Content =
                        "工具调用陷入循环，已强制停止工具调用并返回兜底回复。请尝试换个问法、减少需要工具的操作，或检查工具/权限配置。",
                    ModelName = initialResponse.ModelName,
                    AevatarStopReason = AevatarStopReason.Complete,
                    Usage = initialResponse.Usage
                };
            }
        }

        return (current, lastToolCall);
    }

    private async Task<AevatarLLMResponse> ForceModelToAnswerWithoutToolsAsync(
        AevatarLLMRequest llmRequest,
        string reason,
        CancellationToken cancellationToken)
    {
        llmRequest.Messages.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.System,
            Content =
                "Tool loop guard triggered: " + reason + "\n" +
                "You MUST stop calling tools now. Use the existing tool results already provided in the conversation and answer the user directly.\n" +
                "If you cannot answer, explain what information is missing and ask a single clarifying question.",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        llmRequest.Functions = new List<AevatarFunctionDefinition>();
        return await LLMProvider.GenerateAsync(llmRequest, cancellationToken);
    }

    private static string EnsureToolCallId(AevatarFunctionCall functionCall)
    {
        if (!string.IsNullOrWhiteSpace(functionCall.CallId))
            return functionCall.CallId;

        functionCall.CallId = Guid.NewGuid().ToString("N");
        return functionCall.CallId;
    }

    private static string BuildToolCallSignature(string toolName, Dictionary<string, object> args)
    {
        var sb = new StringBuilder(toolName.Trim().ToLowerInvariant());
        foreach (var (k, v) in args.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('|')
              .Append(k.Trim().ToLowerInvariant())
              .Append('=')
              .Append(v?.ToString() ?? "null");
        }

        return sb.ToString();
    }

    private async Task<ToolExecutionResult> ExecuteAllowedToolAsync(
        string toolName,
        Dictionary<string, object> args,
        ToolExecutionContext executionContext,
        AevatarLLMRequest llmRequest,
        CancellationToken cancellationToken)
    {
        // Enforce policy again at execution time (defense in depth).
        var toolDef = _registeredToolsCache.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        if (toolDef != null && !IsToolAllowedByPolicy(toolDef))
        {
            var content = JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool execution denied by agent policy.",
                tool = toolName,
                deniedReason = BuildToolPolicyDenyReason(toolDef)
            });

            return new ToolExecutionResult
            {
                ToolName = toolName,
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is denied by agent policy.",
                Content = content,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        if (TryGetToolAllowlist(llmRequest, out var allowlist) &&
            allowlist.Count > 0 &&
            !allowlist.Contains(toolName))
        {
            // Deny execution (return a tool result the model can read)
            var content = JsonSerializer.Serialize(new
            {
                success = false,
                error = "Tool is not allowed by current allowlist.",
                tool = toolName,
                allowedTools = allowlist.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
            });

            return new ToolExecutionResult
            {
                ToolName = toolName,
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is not allowed by current allowlist.",
                Content = content,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
        }

        return await ExecuteToolAsync(toolName, args, executionContext, cancellationToken);
    }

    private bool IsToolAllowedByPolicy(ToolDefinition tool)
    {
        if (!AllowInternalTools && tool.RequiresInternalAccess)
            return false;

        if (!AllowDangerousTools && (tool.IsDangerous || tool.RequiresConfirmation))
            return false;

        return true;
    }

    private string BuildToolPolicyDenyReason(ToolDefinition tool)
    {
        if (!AllowInternalTools && tool.RequiresInternalAccess)
            return "RequiresInternalAccess is disabled (AllowInternalTools=false).";

        if (!AllowDangerousTools && (tool.IsDangerous || tool.RequiresConfirmation))
            return "Dangerous/confirmation tools are disabled (AllowDangerousTools=false).";

        return "Denied by policy.";
    }

    private static bool TryGetToolAllowlist(AevatarLLMRequest llmRequest, out HashSet<string> allowlist)
    {
        allowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (llmRequest.Context == null)
            return false;

        if (!llmRequest.Context.TryGetValue(ToolAllowlistContextKey, out var v) || v == null)
            return false;

        switch (v)
        {
            case HashSet<string> s:
                allowlist = s;
                return true;
            case string[] arr:
                allowlist = new HashSet<string>(arr.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                return true;
            case List<string> list:
                allowlist = new HashSet<string>(list.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
                return true;
            case string single when !string.IsNullOrWhiteSpace(single):
                allowlist = new HashSet<string>([single.Trim()], StringComparer.OrdinalIgnoreCase);
                return true;
            default:
                return false;
        }
    }

    private void TryApplyToolAllowlistFromSkillsLoadResult(
        AevatarLLMRequest llmRequest,
        string toolName,
        string? toolResultJson)
    {
        if (!string.Equals(toolName, "skills_load", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(toolResultJson))
        {
            // No payload -> clear allowlist (best-effort).
            llmRequest.Context?.Remove(ToolAllowlistContextKey);
            llmRequest.Context?.Remove(ToolAllowlistSourceSkillContextKey);
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(toolResultJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                // failed load -> do not change allowlist
                return;
            }

            if (!root.TryGetProperty("allowedTools", out var allowedEl) || allowedEl.ValueKind != JsonValueKind.Array)
            {
                // No allowlist -> clear
                llmRequest.Context?.Remove(ToolAllowlistContextKey);
                llmRequest.Context?.Remove(ToolAllowlistSourceSkillContextKey);
                return;
            }

            var list = new List<string>();
            foreach (var el in allowedEl.EnumerateArray())
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s.Trim());
            }

            if (list.Count == 0)
            {
                llmRequest.Context?.Remove(ToolAllowlistContextKey);
                llmRequest.Context?.Remove(ToolAllowlistSourceSkillContextKey);
                return;
            }

            llmRequest.Context ??= new Dictionary<string, object>();
            llmRequest.Context[ToolAllowlistContextKey] = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                var skillName = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(skillName))
                {
                    llmRequest.Context[ToolAllowlistSourceSkillContextKey] = skillName!;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to parse skills_load result for tool allowlist (best-effort).");
        }
    }

    private static Dictionary<string, object> ParseToolArguments(string argumentsJson)
    {
        return ToolArgumentsJson.Parse(argumentsJson);
    }

    private static AevatarChatMessage CreateToolCallMessage(AevatarFunctionCall functionCall, string? reasoningContent = null)
    {
        var callId = string.IsNullOrWhiteSpace(functionCall.CallId)
            ? Guid.NewGuid().ToString("N")
            : functionCall.CallId;

        var msg = new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = $"Calling tool {functionCall.Name} with arguments: {functionCall.Arguments}",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolCalls =
            {
                new ToolCall
                {
                    Id = callId,
                    ToolName = functionCall.Name,
                    Arguments = functionCall.Arguments
                }
            }
        };

        if (!string.IsNullOrEmpty(reasoningContent))
        {
            msg.Metadata.Add("reasoning_content", reasoningContent);
        }

        return msg;
    }

    private static AevatarChatMessage CreateToolResultMessage(string toolName, string toolCallId, ToolExecutionResult result)
    {
        var callId = !string.IsNullOrWhiteSpace(toolCallId)
            ? toolCallId
            : result.ToolCallId;

        return new AevatarChatMessage
        {
            Role = AevatarChatRole.Tool,
            Content = result.Content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolResult = new ToolExecutionResult
            {
                ToolCallId = callId,
                ToolName = toolName,
                Content = result.Content,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                Timestamp = result.Timestamp,
                Duration = result.Duration
            }
        };
    }

    [EventHandler]
    protected virtual async Task HandleToolExecutionRequestEvent(ToolExecutionRequestEvent evt)
    {
        await InitializeToolsAsync();

        var parameters = ParseToolArguments(evt.Arguments);
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

