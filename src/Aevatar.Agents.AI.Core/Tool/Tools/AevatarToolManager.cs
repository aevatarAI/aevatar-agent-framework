using System.Collections.Concurrent;
using System.Diagnostics;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tool.Tools;

/// <summary>
/// Default implementation of the tool manager.
/// <para/>
/// Provides thread-safe tool registration, discovery, execution, and Function Calling support.
/// </summary>
public class AevatarToolManager : IAevatarToolManager
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();
    private readonly ILogger<AevatarToolManager> _logger;
    private Aevatar.Agents.AI.Tool.Evolution.IToolEvolutionRegistry? _evolutionRegistry;

    // ------------------------------------------------------------
    // Protobuf JSON formatting (Any support)
    //
    // WHY:
    // - Many tool results use `google.protobuf.Any` (e.g., AevatarAIToolResult.Data).
    // - JsonFormatter.Default uses TypeRegistry.Empty, which cannot resolve
    //   wrapper types like google.protobuf.StringValue and will throw:
    //   "Type registry has no descriptor for type name 'google.protobuf.StringValue'".
    // - We provide an expanded TypeRegistry + a safe fallback to avoid turning
    //   a successful tool execution into a failed one due to formatting.
    // ------------------------------------------------------------
    private static readonly JsonFormatter ToolResultJsonFormatter = new(
        JsonFormatter.Settings.Default
            .WithFormatDefaultValues(true)
            .WithPreserveProtoFieldNames(true)
            .WithTypeRegistry(TypeRegistry.FromFiles(
                // Well-known types frequently packed into Any (StringValue, BoolValue, etc.)
                WrappersReflection.Descriptor,

                // Core AI messages (ToolExecutionResult, ChatMessage, etc.)
                AiAbstractionsMessagesReflection.Descriptor,

                // Tool messages (AevatarAIToolResult, ToolExecution* events, etc.)
                ToolMessagesReflection.Descriptor)));

    private string FormatToolResultSafe(IMessage result)
    {
        try
        {
            return ToolResultJsonFormatter.Format(result);
        }
        catch (Exception ex)
        {
            // Best-effort: formatting errors must not fail the tool execution.
            _logger.LogDebug(ex, "Failed to format tool result as JSON. Falling back to text format.");
            return result.ToString() ?? string.Empty;
        }
    }

    public AevatarToolManager(
        ILogger<AevatarToolManager> logger,
        Aevatar.Agents.AI.Tool.Evolution.IToolEvolutionRegistry? evolutionRegistry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _evolutionRegistry = evolutionRegistry;
    }

    /// <summary>
    /// Tool evolution registry (optional).
    /// </summary>
    public Aevatar.Agents.AI.Tool.Evolution.IToolEvolutionRegistry? EvolutionRegistry
    {
        get => _evolutionRegistry;
        set => _evolutionRegistry = value;
    }

    /// <inheritdoc/>
    public Task RegisterToolAsync(ToolDefinition tool, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            throw new ArgumentException("Tool name cannot be null or empty", nameof(tool));
        }

        if (!tool.CanBeOverridden && _tools.ContainsKey(tool.Name))
        {
            _logger.LogWarning("Tool {ToolName} already exists and cannot be overridden", tool.Name);
            return Task.CompletedTask;
        }

        _tools[tool.Name] = tool;
        _logger.LogInformation("Registered tool: {ToolName} (Category: {Category}, Version: {Version})",
            tool.Name, tool.Category, tool.Version);

        _evolutionRegistry?.RegisterBaseTool(tool);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        if (_evolutionRegistry != null)
        {
            var active = _evolutionRegistry.GetActiveTools();
            if (active.Count > 0)
                return active.Where(t => t.IsEnabled).ToList();
        }

        return _tools.Values.Where(t => t.IsEnabled).ToList();
    }

    /// <inheritdoc/>
    public async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(parameters);

        var stopwatch = Stopwatch.StartNew();
        var toolCallId = Guid.NewGuid().ToString("N");

        var resolvedTool = _evolutionRegistry?.ResolveToolForExecution(toolName);
        if (resolvedTool == null && !_tools.TryGetValue(toolName, out resolvedTool))
        {
            _logger.LogError("Tool '{ToolName}' not found", toolName);
            var result = BuildFailureResult(
                toolCallId,
                toolName,
                errorMessage: $"Tool '{toolName}' not found",
                stopwatch);

            await TryPublishToolExecutedEventAsync(toolName, parameters, result, context, cancellationToken);
            return result;
        }

        var tool = resolvedTool;
        if (!IsExecutionAllowed(tool, context, out var denyReason))
        {
            _logger.LogWarning("Tool '{ToolName}' execution denied: {Reason}", toolName, denyReason);

            var result = BuildFailureResult(
                toolCallId,
                toolName,
                errorMessage: denyReason,
                stopwatch);

            await TryPublishToolExecutedEventAsync(toolName, parameters, result, context, cancellationToken);
            return result;
        }

        if (!tool.IsEnabled)
        {
            _logger.LogWarning("Tool '{ToolName}' is disabled", toolName);
            var result = BuildFailureResult(
                toolCallId,
                toolName,
                errorMessage: $"Tool '{toolName}' is disabled",
                stopwatch);

            await TryPublishToolExecutedEventAsync(toolName, parameters, result, context, cancellationToken);
            return result;
        }

        if (tool.ExecuteAsync == null)
        {
            _logger.LogError("Tool '{ToolName}' has no execution function", toolName);
            var result = BuildFailureResult(
                toolCallId,
                toolName,
                errorMessage: $"Tool '{toolName}' has no execution function",
                stopwatch);

            await TryPublishToolExecutedEventAsync(toolName, parameters, result, context, cancellationToken);
            return result;
        }

        if (context != null)
        {
            context.Metadata["tool_version"] = tool.Version ?? string.Empty;
            context.Metadata["tool_category"] = tool.Category.ToString();
        }

        _logger.LogDebug("Executing tool: {ToolName} for agent: {AgentId}", toolName, context?.AgentId ?? "unknown");

        try
        {
            // Execute tool
            var result = await tool.ExecuteAsync(parameters, context, cancellationToken);

            _logger.LogDebug("Tool executed successfully: {ToolName}", toolName);

            stopwatch.Stop();

            var execResult = new ToolExecutionResult
            {
                ToolCallId = toolCallId,
                IsSuccess = true,
                Content = FormatToolResultSafe(result),
                ToolName = toolName,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Duration = Duration.FromTimeSpan(stopwatch.Elapsed)
            };

            await TryPublishToolExecutedEventAsync(toolName, parameters, execResult, context, cancellationToken);
            return execResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Tool execution cancelled: {ToolName}", toolName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolName);
            var result = BuildFailureResult(
                toolCallId,
                toolName,
                errorMessage: $"Tool execution failed: {ex.Message}",
                stopwatch,
                content: ex.Message);

            await TryPublishToolExecutedEventAsync(toolName, parameters, result, context, cancellationToken);
            return result;
        }
    }

    private static bool IsExecutionAllowed(ToolDefinition tool, ToolExecutionContext? context, out string reason)
    {
        // ============================================================
        //  Safety policy
        //
        //  Design:
        //  - Keep it explicit: caller must opt-in via ToolExecutionContext flags.
        //  - Defense in depth: AIGAgentBase also filters exposure + execution.
        // ============================================================
        var allowInternal = context?.AllowInternalTools ?? false;
        var allowDangerous = context?.AllowDangerousTools ?? false;

        if (tool.RequiresInternalAccess && !allowInternal)
        {
            reason = "Tool requires internal access and execution is disabled by policy (AllowInternalTools=false).";
            return false;
        }

        if ((tool.IsDangerous || tool.RequiresConfirmation) && !allowDangerous)
        {
            reason =
                "Tool is dangerous or requires confirmation and execution is disabled by policy (AllowDangerousTools=false).";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static ToolExecutionResult BuildFailureResult(
        string toolCallId,
        string toolName,
        string errorMessage,
        Stopwatch stopwatch,
        string? content = null)
    {
        stopwatch.Stop();
        return new ToolExecutionResult
        {
            ToolCallId = toolCallId,
            ToolName = toolName,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            Content = content ?? errorMessage,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Duration = Duration.FromTimeSpan(stopwatch.Elapsed)
        };
    }

    private async Task TryPublishToolExecutedEventAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionResult result,
        ToolExecutionContext? context,
        CancellationToken cancellationToken)
    {
        if (context == null)
            return;

        if (context.PublishEventWithDirectionCallback == null && context.PublishEventCallback == null)
            return;

        try
        {
            var evt = new AevatarToolExecutedEvent
            {
                ToolName = toolName,
                Result = result.Content ?? string.Empty,
                Success = result.IsSuccess,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                AgentId = context.AgentId,
                ExecutionTimeMs = (long)result.Duration.ToTimeSpan().TotalMilliseconds
            };

            foreach (var (k, v) in parameters)
            {
                evt.Parameters[k] = v?.ToString() ?? string.Empty;
            }

            if (context.PublishEventWithDirectionCallback != null)
            {
                await context.PublishEventWithDirectionCallback(evt, EventDirection.Down, cancellationToken);
            }
            else
            {
                await context.PublishEventCallback!(evt);
            }
        }
        catch (Exception ex)
        {
            // Best-effort only: do not fail the tool execution due to telemetry/eventing.
            _logger.LogDebug(ex, "Failed to publish AevatarToolExecutedEvent (best-effort).");
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var tools = await GetAvailableToolsAsync(cancellationToken);
        var definitions = new List<AevatarFunctionDefinition>();

        foreach (var tool in tools)
        {
            if (tool.ExecuteAsync != null)
            {
                var functionDef = new AevatarFunctionDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = ConvertToFunctionParameters(tool.Parameters)
                };

                definitions.Add(functionDef);
            }
        }

        return definitions;
    }

    /// <summary>
    /// Get a single tool by name.
    /// </summary>
    public async Task<ToolDefinition?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        await Task.CompletedTask;
        return _tools.TryGetValue(toolName, out var tool) ? tool : null;
    }

    /// <summary>
    /// Check if a tool exists.
    /// </summary>
    public bool HasTool(string toolName)
    {
        return !string.IsNullOrWhiteSpace(toolName) && _tools.ContainsKey(toolName);
    }

    /// <summary>
    /// Enable a tool.
    /// </summary>
    public async Task<bool> EnableToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (_tools.TryGetValue(toolName, out var tool))
        {
            tool.IsEnabled = true;
            _logger.LogInformation("Enabled tool: {ToolName}", toolName);
            return true;
        }

        _logger.LogWarning("Cannot enable tool '{ToolName}': tool not found", toolName);
        return false;
    }

    /// <summary>
    /// Disable a tool.
    /// </summary>
    public async Task<bool> DisableToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (_tools.TryGetValue(toolName, out var tool))
        {
            tool.IsEnabled = false;
            _logger.LogInformation("Disabled tool: {ToolName}", toolName);
            return true;
        }

        _logger.LogWarning("Cannot disable tool '{ToolName}': tool not found", toolName);
        return false;
    }

    /// <summary>
    /// Convert tool parameters to function parameters.
    /// </summary>
    private Dictionary<string, AevatarParameterDefinition> ConvertToFunctionParameters(ToolParameters parameters)
    {
        var functionParams = new Dictionary<string, AevatarParameterDefinition>();

        if (parameters?.Items == null)
        {
            return functionParams;
        }

        static AevatarParameterDefinition ConvertToolParameter(ToolParameter p, bool required)
        {
            var def = new AevatarParameterDefinition
            {
                Type = ParseTypeString(p.Type),
                Description = p.Description,
                Required = required,
                Default = p.DefaultValue,
                Enum = p.Enum?
                    .Select(e => e?.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList()
            };

            if (p.Items != null)
            {
                // Nested schemas: array-of-X, array-of-array-of-X, etc.
                def.Items = ConvertToolParameter(p.Items, required: false);
            }

            return def;
        }

        foreach (var param in parameters.Items)
        {
            var required = parameters.Required?.Contains(param.Key) ?? false;
            functionParams[param.Key] = ConvertToolParameter(param.Value, required);
        }

        return functionParams;
    }

    /// <summary>
    /// Parse type string.
    /// </summary>
    private static string ParseTypeString(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "string" => "string",
            "integer" or "int" or "int32" => "integer",
            "number" or "float" or "double" or "decimal" => "number",
            "boolean" or "bool" => "boolean",
            "array" => "array",
            "object" => "object",
            _ => "string" // Default
        };
    }
}
