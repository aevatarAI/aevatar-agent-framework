using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.AI.Tool.Tools;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Aevatar.Agents.AI.Tool.Tools.CoreTools;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Aevatar.Agents.AI.Core.Messages;

namespace Aevatar.Agents.AI.Core;

// ReSharper disable InconsistentNaming
public abstract partial class AIGAgentBase
{
    /// <summary>
    /// Safety switch (default: true):
    /// - When false, tools marked <c>RequiresConfirmation</c> or <c>IsDangerous</c> will not be exposed/executed.
    /// - Disable in derived agents if you want to prevent side-effect tools (HTTP, publish_event, dotnet-file/python-file tools, etc).
    /// </summary>
    public bool AllowDangerousTools { get; set; } = true;

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
    //  Tool system (now part of AIGAgentBase)
    // ============================================================

    private ToolingRuntime? _toolingRuntime;
    private ToolingRuntime Tooling => _toolingRuntime ??= new ToolingRuntime(ToolingInitHost, ToolingLoopHost);

    private IReadOnlyList<ToolDefinition> RegisteredToolsCache => Tooling.RegisteredToolsCache;
    private IReadOnlyList<AevatarFunctionDefinition> FunctionDefinitionsCache => Tooling.FunctionDefinitionsCache;

    /// <summary>
    /// Gets or sets the tool manager (DI injectable).
    /// </summary>
    protected IAevatarToolManager ToolManager
    {
        get
        {
            return Tooling.ToolManager;
        }
        set
        {
            Tooling.ToolManager = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    private void EnsureToolManagerInitialized()
    {
        Tooling.EnsureToolManagerInitialized();
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
        await Tooling.InitializeToolsAsync(cancellationToken);
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

        // Built-in: web search (third-party provider; best-effort + opt-in via config/DI)
        await RegisterWebSearchToolBestEffortAsync(cancellationToken);

        // Agent Skills (agentskills.io) - gated by EnableAgentSkills (enabled by default in this repo)
        await RegisterAgentSkillsToolsAsync(cancellationToken);

        // MCP servers (Cursor-style config: MCP:mcpServers) - best-effort
        await RegisterMcpServersFromConfigurationBestEffortAsync(isRetry: false, cancellationToken);
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
    /// Import a single-file Python "skill" as a tool via <c>python3 -I -u</c>.
    /// </summary>
    public async Task RegisterPythonFileSkillAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var tool = await PythonFileSkillTool.LoadFromFileAsync(
            filePath,
            logger: Logger,
            cancellationToken: cancellationToken);

        await RegisterToolAsync(tool, Logger, cancellationToken);
    }

    /// <summary>
    /// Auto-register file-based tools from a directory.
    /// <para/>
    /// By default this registers:
    /// - dotnet-file tools: <c>*.cs</c> that contain <c>/*aevatar_tool</c> within the first 16KB
    /// - python-file tools: <c>*.py</c> that contain <c>"""aevatar_tool</c> or <c>'''aevatar_tool</c> within the first 16KB
    /// <para/>
    /// This is intended for demo/dev convenience (so agents don't have to list each tool file manually).
    /// </summary>
    public async Task RegisterFileSkillsFromDirectoryAsync(
        string directoryPath,
        bool includeDotNet = true,
        bool includePython = true,
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        int maxFilesPerType = 64,
        bool requireManifestMarker = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;

        string fullDir;
        try
        {
            fullDir = Path.GetFullPath(directoryPath.Trim());
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(fullDir))
            return;

        EnsureToolManagerInitialized();

        // Build once and reuse (avoid per-file cache refresh cost).
        var toolContext = BuildToolRegistrationContext();
        var registeredAny = false;

        if (includeDotNet)
        {
            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(fullDir, "*.cs", searchOption);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to enumerate dotnet-file skills under '{Dir}' (best-effort).", fullDir);
            }

            foreach (var file in files
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                         .Take(Math.Clamp(maxFilesPerType, 0, 512)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (requireManifestMarker && !LooksLikeAevatarDotNetToolFile(file))
                    continue;

                try
                {
                    var tool = await DotNetFileSkillTool.LoadFromFileAsync(file, Logger, cancellationToken);
                    var def = tool.CreateToolDefinition(toolContext, Logger);
                    await ToolManager.RegisterToolAsync(def, cancellationToken);
                    registeredAny = true;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to register dotnet-file skill '{File}' (best-effort).", file);
                }
            }
        }

        if (includePython)
        {
            IEnumerable<string> files = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(fullDir, "*.py", searchOption);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to enumerate python-file skills under '{Dir}' (best-effort).", fullDir);
            }

            foreach (var file in files
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                         .Take(Math.Clamp(maxFilesPerType, 0, 512)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (requireManifestMarker && !LooksLikeAevatarPythonToolFile(file))
                    continue;

                try
                {
                    var tool = await PythonFileSkillTool.LoadFromFileAsync(file, Logger, cancellationToken);
                    var def = tool.CreateToolDefinition(toolContext, Logger);
                    await ToolManager.RegisterToolAsync(def, cancellationToken);
                    registeredAny = true;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to register python-file skill '{File}' (best-effort).", file);
                }
            }
        }

        if (registeredAny)
        {
            await RefreshToolCachesAsync(cancellationToken);
        }
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

    private static bool LooksLikeAevatarPythonToolFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var max = (int)Math.Min(16 * 1024, fs.Length);
            if (max <= 0) return false;

            var buf = new byte[max];
            var read = fs.Read(buf, 0, max);
            if (read <= 0) return false;

            var head = Encoding.UTF8.GetString(buf, 0, read);
            return head.Contains("\"\"\"aevatar_tool", StringComparison.OrdinalIgnoreCase) ||
                   head.Contains("'''aevatar_tool", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeAevatarDotNetToolFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var max = (int)Math.Min(16 * 1024, fs.Length);
            if (max <= 0) return false;

            var buf = new byte[max];
            var read = fs.Read(buf, 0, max);
            if (read <= 0) return false;

            var head = Encoding.UTF8.GetString(buf, 0, read);
            return head.Contains("/*aevatar_tool", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
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

    /// <summary>
    /// Execute a tool by name with parameters.
    /// </summary>
    protected async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        EnsureToolManagerInitialized();

        cancellationToken.ThrowIfCancellationRequested();

        var msgId = Guid.NewGuid().ToString("N");
        var tcId = context?.ToolCallId;
        if (string.IsNullOrWhiteSpace(tcId))
            tcId = Guid.NewGuid().ToString("N");
        if (context != null)
        {
            context.ToolCallId = tcId;
            context.ToolName ??= toolName;
        }

        var sessionId = context?.GetSessionId?.Invoke() ?? Guid.NewGuid().ToString("N");
        var lastProgressAtMs = 0L;
        string? lastProgressMsg = null;

        async Task EmitTraceProgressAsync(string msg, CancellationToken ct)
        {
            msg = NormalizeProgressMessage(msg);
            if (msg.Length == 0)
                return;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var since = now - lastProgressAtMs;
            if (since < 800 && string.Equals(lastProgressMsg, msg, StringComparison.Ordinal))
                return;
            if (since < 500)
                return;

            lastProgressAtMs = now;
            lastProgressMsg = msg;

            var evt = new ExecutionTraceEvent
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Phase = ExecutionTraceEventPhase.ToolProgress,
                Message = msg,
                NodeId = $"tool:{tcId}"
            };

            evt.Fields[ExecutionTraceEventFields.Status] =
                ExecutionTraceEventFieldValue.FromString(ExecutionTraceEventStatus.Running);
            evt.Fields[ExecutionTraceEventFields.SessionId] =
                ExecutionTraceEventFieldValue.FromString(sessionId);
            evt.Fields[ExecutionTraceEventFields.ExecutionId] =
                ExecutionTraceEventFieldValue.FromString(sessionId);
            evt.Fields[ExecutionTraceEventFields.AgentId] =
                ExecutionTraceEventFieldValue.FromString(Id.ToString());
            evt.Fields[ExecutionTraceEventFields.ToolName] =
                ExecutionTraceEventFieldValue.FromString(toolName);
            evt.Fields[ExecutionTraceEventFields.ToolCallId] =
                ExecutionTraceEventFieldValue.FromString(tcId);
            evt.Fields[ExecutionTraceEventFields.Phase] =
                ExecutionTraceEventFieldValue.FromString(ExecutionTraceEventPhase.ToolProgress);
            evt.Fields[ExecutionTraceEventFields.MessageId] =
                ExecutionTraceEventFieldValue.FromString(sessionId);

            try
            {
                await PublishAsync(evt, EventDirection.Down, ct);
            }
            catch
            {
                // best-effort only
            }
        }

        if (context != null)
        {
            var previous = context.ReportProgressAsync;
            context.ReportProgressAsync = async (msg, ct) =>
            {
                if (previous != null)
                {
                    await previous(msg, ct);
                }

                await EmitTraceProgressAsync(msg, ct);
            };
        }

        // Publish START (Protobuf)
        await PublishAsync(new ToolCallStartEvent
        {
            MessageId = msgId,
            ToolCallId = tcId,
            ToolName = toolName,
            ArgumentsJson = JsonSerializer.Serialize(parameters),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, EventDirection.Down, cancellationToken);

        ToolExecutionResult result;
        try
        {
            result = await ToolManager.ExecuteToolAsync(toolName, parameters, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected: cooperative cancellation. Do NOT publish "error" events on cancel,
            // and do not try to publish extra events using an already-canceled token.
            throw;
        }
        catch (Exception ex)
        {
            // Publish ERROR result
            await PublishAsync(new ToolCallResultEvent
            {
                MessageId = msgId,
                ToolCallId = tcId,
                Result = "",
                IsSuccess = false,
                ErrorMessage = ex.Message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            }, EventDirection.Down, cancellationToken);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Publish SUCCESS result
        await PublishAsync(new ToolCallResultEvent
        {
            MessageId = msgId,
            ToolCallId = tcId,
            Result = result.Content ?? "",
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage ?? "",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, EventDirection.Down, cancellationToken);

        await PublishAsync(new ToolCallEndEvent
        {
            MessageId = msgId,
            ToolCallId = tcId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, EventDirection.Down, cancellationToken);

        return result;
    }

    private static string NormalizeProgressMessage(string? msg, int maxChars = 2000)
    {
        var text = (msg ?? string.Empty).Replace("\r", "").Trim();
        if (text.Length == 0)
            return string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private string BuildToolInstructionBlock()
    {
        if (RegisteredToolsCache.Count == 0)
            return string.Empty;

        var visibleTools = RegisteredToolsCache
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
        if (FunctionDefinitionsCache.Count == 0)
            return;

        // Apply runtime policy: keep dangerous tools hidden unless explicitly enabled.
        var allowedNames = RegisteredToolsCache
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
            llmRequest.Functions = FunctionDefinitionsCache
                .Where(d => allowlist.Contains(d.Name) && allowedNames.Contains(d.Name))
                .ToList();
            return;
        }

        llmRequest.Functions = FunctionDefinitionsCache
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