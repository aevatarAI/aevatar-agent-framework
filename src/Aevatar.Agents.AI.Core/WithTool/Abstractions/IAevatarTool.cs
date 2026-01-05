using System;
using System.Collections;
using System.Globalization;
using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// AI tool interface
/// Defines the standard contract for implementing tools
/// </summary>
public interface IAevatarTool
{
    /// <summary>
    /// Tool name (unique identifier)
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Tool description
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Tool category
    /// </summary>
    ToolCategory Category { get; }
    
    /// <summary>
    /// Tool version
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// Tool tags
    /// </summary>
    IList<string> Tags { get; }
    
    /// <summary>
    /// Create tool definition
    /// </summary>
    /// <param name="context">Tool context</param>
    /// <param name="logger">Logger</param>
    /// <returns>Configured tool definition</returns>
    ToolDefinition CreateToolDefinition(ToolContext context, ILogger? logger = null);
    
    /// <summary>
    /// Create parameter definition
    /// </summary>
    /// <returns>Tool parameter definition</returns>
    ToolParameters CreateParameters();
    
    /// <summary>
    /// Execute tool logic
    /// </summary>
    /// <param name="parameters">Execution parameters</param>
    /// <param name="context">Tool context</param>
    /// <param name="logger">Logger</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution result</returns>
    Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate parameters
    /// </summary>
    /// <param name="parameters">Parameters to validate</param>
    /// <returns>Validation result</returns>
    ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters);
}

/// <summary>
/// Tool base class
/// Provides default implementation of IAevatarTool
/// </summary>
public abstract class AevatarToolBase : IAevatarTool
{
    /// <inheritdoc />
    public abstract string Name { get; }
    
    /// <inheritdoc />
    public abstract string Description { get; }
    
    /// <inheritdoc />
    public virtual ToolCategory Category { get; } = ToolCategory.Custom;
    
    /// <inheritdoc />
    public virtual string Version { get; } = "1.0.0";
    
    /// <inheritdoc />
    public virtual IList<string> Tags { get; } = new List<string>();
    
    /// <inheritdoc />
    public virtual ToolDefinition CreateToolDefinition(ToolContext context, ILogger? logger = null)
    {
        return new ToolDefinition
        {
            Name = Name,
            Description = Description,
            Category = Category,
            Version = Version,
            Tags = Tags,
            Parameters = CreateParameters(),
            ExecuteAsync = (parameters, executionContext, ct) =>
                ExecuteAsync(parameters, BuildRuntimeToolContext(context, executionContext, logger), logger, ct),
            RequiresInternalAccess = RequiresInternalAccess(),
            CanBeOverridden = CanBeOverridden(),
            RequiresConfirmation = RequiresConfirmation(),
            IsDangerous = IsDangerous(),
            RateLimit = GetRateLimit(),
            Timeout = GetTimeout()
        };
    }

    // ============================================================
    //  ToolContext composition
    //
    //  WHY:
    //  - ToolDefinition.ExecuteAsync receives a per-call ToolExecutionContext (memory, session, callbacks...).
    //  - IAevatarTool.ExecuteAsync expects ToolContext.
    //  - We merge "registration-time context" (static) + "execution-time context" (dynamic) into a runtime ToolContext.
    //
    //  NOTE:
    //  - This keeps backward compatibility: if executionContext is null, tools behave exactly as before.
    // ============================================================
    private static ToolContext BuildRuntimeToolContext(
        ToolContext baseContext,
        ToolExecutionContext? executionContext,
        ILogger? logger)
    {
        // Clone base context first (avoid tools mutating shared captured instance)
        var merged = new ToolContext
        {
            AgentId = baseContext.AgentId,
            AgentType = baseContext.AgentType,
            IncludeCoreTools = baseContext.IncludeCoreTools,
            Categories = baseContext.Categories,
            GetStateCallback = baseContext.GetStateCallback,
            GenerateEmbeddingsAsync = baseContext.GenerateEmbeddingsAsync,
            PublishEventCallback = baseContext.PublishEventCallback,
            PublishEventWithDirectionCallback = baseContext.PublishEventWithDirectionCallback,
            GetSessionIdCallback = baseContext.GetSessionIdCallback,
            Logger = baseContext.Logger ?? logger,
            Metadata = baseContext.Metadata != null
                ? new Dictionary<string, object>(baseContext.Metadata)
                : new Dictionary<string, object>()
        };

        if (executionContext == null)
            return merged;

        if (!string.IsNullOrWhiteSpace(executionContext.AgentId))
            merged.AgentId = executionContext.AgentId;

        if (executionContext.PublishEventCallback != null)
            merged.PublishEventCallback = executionContext.PublishEventCallback;

        if (executionContext.PublishEventWithDirectionCallback != null)
            merged.PublishEventWithDirectionCallback = executionContext.PublishEventWithDirectionCallback;

        if (executionContext.GetSessionId != null)
            merged.GetSessionIdCallback = () => executionContext.GetSessionId();

        if (executionContext.Logger != null)
            merged.Logger = executionContext.Logger;

        if (executionContext.Metadata is { Count: > 0 })
        {
            foreach (var (k, v) in executionContext.Metadata)
            {
                merged.Metadata[k] = v;
            }
        }

        return merged;
    }
    
    /// <inheritdoc />
    public abstract ToolParameters CreateParameters();
    
    public abstract Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default);
    
    /// <inheritdoc />
    public virtual ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        var result = new ToolParameterValidationResult { IsValid = true };
        var paramDefinitions = CreateParameters();
        
        // Validate required parameters
        foreach (var requiredParam in paramDefinitions.Required)
        {
            if (!parameters.ContainsKey(requiredParam) || parameters[requiredParam] == null)
            {
                result.IsValid = false;
                result.Errors.Add($"Required parameter '{requiredParam}' is missing");
            }
        }
        
        // Validate parameter types and enum values
        foreach (var param in parameters)
        {
            if (paramDefinitions.Items.TryGetValue(param.Key, out var paramDef))
            {
                // Validate enum values
                if (paramDef.Enum != null && paramDef.Enum.Count > 0)
                {
                    var value = param.Value?.ToString();
                    if (value != null && !paramDef.Enum.Contains(value))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"Parameter '{param.Key}' value '{value}' is not in allowed values: {string.Join(", ", paramDef.Enum)}");
                    }
                }
                
                // Validate type (simplified version)
                if (!ValidateType(param.Value, paramDef.Type))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Parameter '{param.Key}' type mismatch, expected: {paramDef.Type}");
                }
            }
        }
        
        return result;
    }

    /// <summary>
    /// Convert ExecuteAsync parameters (Dictionary&lt;string, object&gt;) to the validator shape
    /// (Dictionary&lt;string, object?&gt;).
    ///
    /// Note: nullability annotations are not part of the CLR type identity, so this is purely for
    /// compile-time flow analysis and to keep call sites clean.
    /// </summary>
    protected static Dictionary<string, object?> ToNullableParameters(Dictionary<string, object> parameters)
    {
        var result = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            result[key] = value;
        }
        return result;
    }

    /// <summary>
    /// Whether internal access permission is required
    /// </summary>
    protected virtual bool RequiresInternalAccess() => false;
    
    /// <summary>
    /// Whether can be overridden
    /// </summary>
    protected virtual bool CanBeOverridden() => true;
    
    /// <summary>
    /// Whether confirmation is required
    /// </summary>
    protected virtual bool RequiresConfirmation() => false;
    
    /// <summary>
    /// Whether is a dangerous operation
    /// </summary>
    protected virtual bool IsDangerous() => false;
    
    /// <summary>
    /// Get rate limit
    /// </summary>
    protected virtual int? GetRateLimit() => null;
    
    /// <summary>
    /// Get timeout duration
    /// </summary>
    protected virtual TimeSpan? GetTimeout() => null;
    
    /// <summary>
    /// Validate parameter type
    /// </summary>
    private static bool ValidateType(object? value, string? expectedType)
    {
        if (value == null || string.IsNullOrEmpty(expectedType))
            return true;

        var type = expectedType.Trim().ToLowerInvariant();
        return type switch
        {
            "string" => value is string || value is JsonElement { ValueKind: JsonValueKind.String },
            "integer" or "int" or "int32" => IsInteger(value),
            "number" or "float" or "double" or "decimal" => IsNumber(value),
            "boolean" or "bool" => IsBoolean(value),
            "object" => IsObject(value),
            "array" => IsArray(value),
            _ => true
        };
    }

    private static bool IsBoolean(object value)
    {
        return value switch
        {
            bool => true,
            JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } => true,
            string s => bool.TryParse(s, out _),
            _ => false
        };
    }

    private static bool IsNumber(object value)
    {
        return value switch
        {
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => true,
            JsonElement { ValueKind: JsonValueKind.Number } => true,
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            _ => false
        };
    }

    private static bool IsInteger(object value)
    {
        return value switch
        {
            byte or sbyte or short or ushort or int or uint or long or ulong => true,
            float f => IsWholeNumber(f),
            double d => IsWholeNumber(d),
            decimal m => m == decimal.Truncate(m),
            JsonElement je when je.ValueKind == JsonValueKind.Number =>
                je.TryGetInt64(out _) || (je.TryGetDouble(out var d) && IsWholeNumber(d)),
            string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            _ => false
        };
    }

    private static bool IsObject(object value)
    {
        return value switch
        {
            IDictionary => true,
            IMessage => true,
            JsonElement { ValueKind: JsonValueKind.Object } => true,
            _ => false
        };
    }

    private static bool IsArray(object value)
    {
        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } => true,
            string => false,
            IDictionary => false,
            IMessage => false,
            IEnumerable => true,
            _ => false
        };
    }

    private static bool IsWholeNumber(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
            return false;

        return Math.Abs(d % 1) < 1e-12;
    }

    private static bool IsWholeNumber(float f)
    {
        if (float.IsNaN(f) || float.IsInfinity(f))
            return false;

        return Math.Abs(f % 1) < 1e-6f;
    }
}

/// <summary>
/// Parameter validation result
/// </summary>
public class ToolParameterValidationResult
{
    /// <summary>
    /// Whether valid
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Error list
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Warning list
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}