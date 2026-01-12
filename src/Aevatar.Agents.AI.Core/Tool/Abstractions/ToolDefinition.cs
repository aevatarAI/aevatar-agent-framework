using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Messages;
using Google.Protobuf;

namespace Aevatar.Agents.AI.Tool.Abstractions;

/// <summary>
/// Tool definition
/// Describes runtime information for an executable tool
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// Tool name (unique identifier)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Display name
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    /// <summary>
    /// Tool description
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Parameter definitions
    /// </summary>
    public ToolParameters Parameters { get; set; } = new();
    
    /// <summary>
    /// Return value definition
    /// </summary>
    public ToolReturnValue? ReturnValue { get; set; }
    
    /// <summary>
    /// Execution function
    /// </summary>
    public Func<Dictionary<string, object>, ToolExecutionContext?, CancellationToken, Task<IMessage>>? ExecuteAsync { get; set; }
    
    /// <summary>
    /// Tags
    /// </summary>
    public IList<string> Tags { get; set; } = new List<string>();
    
    /// <summary>
    /// Category
    /// </summary>
    public ToolCategory Category { get; set; } = ToolCategory.Custom;
    
    /// <summary>
    /// Version
    /// </summary>
    public string Version { get; set; } = ToolConstants.DefaultVersion;
    
    /// <summary>
    /// Whether enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Whether confirmation is required
    /// </summary>
    public bool RequiresConfirmation { get; set; }
    
    /// <summary>
    /// Whether is a dangerous operation
    /// </summary>
    public bool IsDangerous { get; set; }
    
    /// <summary>
    /// Whether internal access permission is required
    /// </summary>
    public bool RequiresInternalAccess { get; set; }
    
    /// <summary>
    /// Whether can be overridden
    /// </summary>
    public bool CanBeOverridden { get; set; } = true;
    
    /// <summary>
    /// Rate limit (maximum calls per minute)
    /// </summary>
    public int? RateLimit { get; set; }
    
    /// <summary>
    /// Timeout duration
    /// </summary>
    public TimeSpan? Timeout { get; set; }
    
    /// <summary>
    /// Retry policy
    /// </summary>
    public RetryPolicy? RetryPolicy { get; set; }
    
    /// <summary>
    /// Metadata
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Tool parameter definitions
/// </summary>
public class ToolParameters
{
    /// <summary>
    /// Parameter dictionary
    /// </summary>
    public Dictionary<string, ToolParameter> Items { get; set; } = new();
    
    /// <summary>
    /// Required parameter list
    /// </summary>
    public IList<string> Required { get; set; } = new List<string>();
    
    /// <summary>
    /// Indexer
    /// </summary>
    public ToolParameter this[string name]
    {
        get => Items[name];
        set => Items[name] = value;
    }
}

/// <summary>
/// Tool parameter
/// </summary>
public class ToolParameter
{
    /// <summary>
    /// Parameter type
    /// </summary>
    public string Type { get; set; } = ToolConstants.DefaultParameterType;
    
    /// <summary>
    /// Whether required
    /// </summary>
    public bool Required { get; set; }
    
    /// <summary>
    /// Description
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Default value
    /// </summary>
    public object? DefaultValue { get; set; }
    
    /// <summary>
    /// Enum values (if restricted)
    /// </summary>
    public IList<object>? Enum { get; set; }
    
    /// <summary>
    /// Minimum value (numeric type)
    /// </summary>
    public double? Minimum { get; set; }
    
    /// <summary>
    /// Maximum value (numeric type)
    /// </summary>
    public double? Maximum { get; set; }
    
    /// <summary>
    /// Minimum length (string type)
    /// </summary>
    public int? MinLength { get; set; }
    
    /// <summary>
    /// Maximum length (string type)
    /// </summary>
    public int? MaxLength { get; set; }
    
    /// <summary>
    /// Regular expression pattern (string type)
    /// </summary>
    public string? Pattern { get; set; }
    
    /// <summary>
    /// Format (e.g., email, uri, date-time, etc.)
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Array item schema (only meaningful when Type == "array").
    /// </summary>
    public ToolParameter? Items { get; set; }
}

/// <summary>
/// Tool return value definition
/// </summary>
public class ToolReturnValue
{
    /// <summary>
    /// Return value type
    /// </summary>
    public string Type { get; set; } = ToolConstants.DefaultReturnType;
    
    /// <summary>
    /// Description
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Schema definition (JSON Schema)
    /// </summary>
    public Dictionary<string, object>? Schema { get; set; }
}

/// <summary>
/// Retry policy
/// </summary>
public class RetryPolicy
{
    /// <summary>
    /// Maximum retry count
    /// </summary>
    public int MaxRetries { get; set; } = RetryPolicyDefaults.MaxRetries;
    
    /// <summary>
    /// Retry delay (milliseconds)
    /// </summary>
    public int RetryDelayMs { get; set; } = RetryPolicyDefaults.RetryDelayMs;
    
    /// <summary>
    /// Whether to use exponential backoff
    /// </summary>
    public bool UseExponentialBackoff { get; set; }
    
    /// <summary>
    /// Maximum delay (milliseconds)
    /// </summary>
    public int MaxRetryDelayMs { get; set; } = RetryPolicyDefaults.MaxRetryDelayMs;
}