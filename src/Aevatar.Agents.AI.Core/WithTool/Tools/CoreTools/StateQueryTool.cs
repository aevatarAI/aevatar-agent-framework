using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools.CoreTools;

/// <summary>
/// State query tool implementation
/// Used to query Agent state information
/// </summary>
public class StateQueryTool : AevatarToolBase
{
    /// <inheritdoc />
    public override string Name => "query_state";
    
    /// <inheritdoc />
    public override string Description => "Query agent state information with JSON Path support";
    
    /// <inheritdoc />
    public override ToolCategory Category => ToolCategory.Core;
    
    /// <inheritdoc />
    public override string Version => "1.0.0";
    
    /// <inheritdoc />
    public override IList<string> Tags => new List<string> { "core", "state", "query", "json-path" };
    
    /// <inheritdoc />
    protected override bool RequiresInternalAccess() => true;
    
    /// <inheritdoc />
    protected override bool CanBeOverridden() => true;
    
    /// <inheritdoc />
    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["field"] = new()
                {
                    Type = "string",
                    Required = true,
                    Description = "The state field to query (e.g., 'Status', 'Configuration')"
                },
                ["path"] = new()
                {
                    Type = "string",
                    Description = "Optional JSON path for nested fields (e.g., '$.nested.property[0]')"
                }
            },
            Required = new[] { "field" }
        };
    }
    
    /// <inheritdoc />
    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // Validate parameters
        var validation = ValidateParameters(ToNullableParameters(parameters));
        if (!validation.IsValid)
        {
            logger?.LogWarning("Invalid parameters: {Errors}", string.Join(", ", validation.Errors));
            var errorResult = new { success = false, errors = validation.Errors };
            return Task.FromResult<IMessage>(JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult)));
        }
        
        if (context.GetStateCallback == null)
        {
            logger?.LogWarning("GetStateCallback not provided, cannot query state");
            var errorResult = new { success = false, error = "State querying not available" };
            return Task.FromResult<IMessage>(JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult)));
        }

        try
        {
            var field = parameters["field"]?.ToString();
            var path = parameters.GetValueOrDefault("path")?.ToString();

            logger?.LogDebug("Querying state field {Field} with path {Path}", field, path);

            var state = context.GetStateCallback();

            if (state == null)
            {
                var errorResult = new { success = false, error = "State is null" };
                return Task.FromResult<IMessage>(JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult)));
            }

            var fieldValue = FieldNavigator.GetFieldValue(state, field, path, logger);

            var result = new
            {
                success = true,
                field = field,
                path = path,
                value = fieldValue,
                stateType = state.GetType().Name,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            logger?.LogDebug("State query successful for field {Field}, value type: {Type}", 
                field, fieldValue?.GetType().Name ?? "null");

            return Task.FromResult<IMessage>(JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(result)));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error querying state");
            var errorResult = new { success = false, error = ex.Message };
            return Task.FromResult<IMessage>(JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult)));
        }
    }
    
    /// <summary>
    /// Validate path format
    /// </summary>
    public static bool IsValidJsonPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return true;
        
        // Simple path format validation
        // Supports: $, $.field, $.field[0], $.nested.field
        return path.StartsWith("$") || 
               path.StartsWith(".") || 
               !path.Contains(" ");
    }
}
