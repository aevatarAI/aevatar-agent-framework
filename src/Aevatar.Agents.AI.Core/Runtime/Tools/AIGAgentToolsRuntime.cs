using System.IO;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.AI.Core.Utils;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core;

internal sealed class AIGAgentToolsRuntime
{
    private readonly IAIGAgentToolsRuntimeHost _host;

    internal AIGAgentToolsRuntime(IAIGAgentToolsRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    internal ToolContext BuildToolRegistrationContext()
    {
        return new ToolContext
        {
            AgentId = _host.AgentId,
            AgentType = _host.AgentType,
            GetStateCallback = _host.GetState,
            GenerateEmbeddingsAsync = _host.HasEmbeddingGenerator
                ? (inputs, ct) => _host.GenerateEmbeddingsAsync(inputs, ct)
                : null,
            PublishEventCallback = msg => _host.PublishToolEventAsync(msg, EventDirection.Down, CancellationToken.None),
            PublishEventWithDirectionCallback = (msg, direction, ct) => _host.PublishToolEventAsync(msg, direction, ct),
            GetSessionIdCallback = () => _host.AgentId,
            Logger = _host.Logger
        };
    }

    internal ToolExecutionContext BuildToolExecutionContext(string sessionId, CancellationToken cancellationToken)
    {
        return new ToolExecutionContext
        {
            AgentId = _host.AgentId,
            ToolManager = _host.ToolManager,
            PublishEventCallback = msg => _host.PublishToolEventAsync(msg, EventDirection.Down, cancellationToken),
            PublishEventWithDirectionCallback = (msg, direction, ct) => _host.PublishToolEventAsync(msg, direction, ct),
            Logger = _host.Logger,
            GetSessionId = () => sessionId,
            AllowInternalTools = _host.AllowInternalTools,
            AllowDangerousTools = _host.AllowDangerousTools
        };
    }

    internal async Task RegisterDotNetFileSkillAsync(string filePath, CancellationToken cancellationToken)
    {
        var tool = await DotNetFileSkillTool.LoadFromFileAsync(
            filePath,
            logger: _host.Logger,
            cancellationToken: cancellationToken);

        await RegisterToolDefinitionAsync(tool, _host.Logger, refreshToolCaches: true, cancellationToken);
    }

    internal async Task RegisterPythonFileSkillAsync(string filePath, CancellationToken cancellationToken)
    {
        var tool = await PythonFileSkillTool.LoadFromFileAsync(
            filePath,
            logger: _host.Logger,
            cancellationToken: cancellationToken);

        await RegisterToolDefinitionAsync(tool, _host.Logger, refreshToolCaches: true, cancellationToken);
    }

    internal async Task RegisterFileSkillsFromDirectoryAsync(
        string directoryPath,
        bool includeDotNet,
        bool includePython,
        SearchOption searchOption,
        int maxFilesPerType,
        bool requireManifestMarker,
        CancellationToken cancellationToken)
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
                _host.Logger.LogDebug(ex, "Failed to enumerate dotnet-file skills under '{Dir}' (best-effort).", fullDir);
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
                    var tool = await DotNetFileSkillTool.LoadFromFileAsync(file, _host.Logger, cancellationToken);
                    var def = tool.CreateToolDefinition(toolContext, _host.Logger);
                    await _host.ToolManager.RegisterToolAsync(def, cancellationToken);
                    registeredAny = true;
                }
                catch (Exception ex)
                {
                    _host.Logger.LogWarning(ex, "Failed to register dotnet-file skill '{File}' (best-effort).", file);
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
                _host.Logger.LogDebug(ex, "Failed to enumerate python-file skills under '{Dir}' (best-effort).", fullDir);
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
                    var tool = await PythonFileSkillTool.LoadFromFileAsync(file, _host.Logger, cancellationToken);
                    var def = tool.CreateToolDefinition(toolContext, _host.Logger);
                    await _host.ToolManager.RegisterToolAsync(def, cancellationToken);
                    registeredAny = true;
                }
                catch (Exception ex)
                {
                    _host.Logger.LogWarning(ex, "Failed to register python-file skill '{File}' (best-effort).", file);
                }
            }
        }

        if (registeredAny)
        {
            await _host.RefreshToolCachesAsync(cancellationToken);
        }
    }

    internal async Task<ToolDefinition> RegisterToolDefinitionAsync(
        IAevatarTool tool,
        ILogger logger,
        bool refreshToolCaches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var context = BuildToolRegistrationContext();
        var toolDefinition = tool.CreateToolDefinition(context, logger);
        await _host.ToolManager.RegisterToolAsync(toolDefinition, cancellationToken);

        if (refreshToolCaches)
        {
            await _host.RefreshToolCachesAsync(cancellationToken);
        }
        return toolDefinition;
    }

    internal async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context,
        CancellationToken cancellationToken)
    {
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
                ExecutionTraceEventFieldValue.FromString(_host.AgentId);
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
                await _host.PublishAsync(evt, EventDirection.Down, ct);
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
        await _host.PublishAsync(new ToolCallStartEvent
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
            result = await _host.ToolManager.ExecuteToolAsync(toolName, parameters, context, cancellationToken);
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
            await _host.PublishAsync(new ToolCallResultEvent
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
        await _host.PublishAsync(new ToolCallResultEvent
        {
            MessageId = msgId,
            ToolCallId = tcId,
            Result = result.Content ?? "",
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage ?? "",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, EventDirection.Down, cancellationToken);

        await _host.PublishAsync(new ToolCallEndEvent
        {
            MessageId = msgId,
            ToolCallId = tcId,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, EventDirection.Down, cancellationToken);

        return result;
    }

    internal async Task<ToolExecutionResult> ExecuteToolForWorkflowAsync(
        string toolName,
        Dictionary<string, object> parameters,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        await _host.InitializeToolsAsync(cancellationToken);

        var sid = string.IsNullOrWhiteSpace(sessionId) ? _host.AgentId : sessionId.Trim();
        var executionContext = BuildToolExecutionContext(sid, cancellationToken);

        var llmRequest = new AevatarLLMRequest
        {
            UserPrompt = string.Empty,
            Messages = new List<AevatarChatMessage>(),
            Settings = new AevatarLLMSettings()
        };

        return await _host.ExecuteAllowedToolWithHooksAsync(
            toolName,
            parameters,
            executionContext,
            llmRequest,
            cancellationToken);
    }

    internal string BuildToolInstructionBlock()
    {
        var tools = _host.RegisteredToolsCache;
        if (tools.Count == 0)
            return string.Empty;

        var visibleTools = tools
            .Where(_host.IsToolAllowedByPolicy)
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

        if (visibleTools.Any(t => string.Equals(t.Name, "find_helpful_skills", StringComparison.OrdinalIgnoreCase)) &&
            visibleTools.Any(t => string.Equals(t.Name, "skills_load", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine(
                "- Skills: call 'find_helpful_skills' with the user task, then 'skills_load' only the top 1-2 candidates. Do NOT load all skills.");
        }

        return sb.ToString().TrimEnd();
    }

    internal void AttachToolsToRequest(AevatarLLMRequest llmRequest)
    {
        var defs = _host.FunctionDefinitionsCache;
        if (defs.Count == 0)
            return;

        // Apply runtime policy: keep dangerous tools hidden unless explicitly enabled.
        var allowedNames = _host.RegisteredToolsCache
            .Where(_host.IsToolAllowedByPolicy)
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
            llmRequest.Functions = defs
                .Where(d => allowlist.Contains(d.Name) && allowedNames.Contains(d.Name))
                .ToList();
            return;
        }

        llmRequest.Functions = defs
            .Where(d => allowedNames.Contains(d.Name))
            .ToList();
    }

    private static string NormalizeProgressMessage(string? msg, int maxChars = 2000)
    {
        var text = (msg ?? string.Empty).Replace("\r", "").Trim();
        if (text.Length == 0)
            return string.Empty;
        return text.Length <= maxChars ? text : text[..maxChars];
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
}

