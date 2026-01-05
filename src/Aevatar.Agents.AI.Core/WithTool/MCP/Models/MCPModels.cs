using System.Text.Json.Serialization;

namespace Aevatar.Agents.AI.WithTool.MCP.Models;

/// <summary>
/// Represents an MCP tool definition.
/// </summary>
public class MCPToolDefinition
{
    /// <summary>
    /// Tool name (unique identifier).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Tool description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Input schema (JSON Schema format).
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public MCPToolSchema InputSchema { get; set; } = new();

    /// <summary>
    /// Tool metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Represents an MCP tool schema (JSON Schema).
/// </summary>
public class MCPToolSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, MCPPropertySchema>? Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }

    [JsonPropertyName("additionalProperties")]
    public bool AdditionalProperties { get; set; } = false;
}

/// <summary>
/// Represents a property schema in MCP tool schema.
/// </summary>
public class MCPPropertySchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("enum")]
    public List<string>? Enum { get; set; }

    [JsonPropertyName("default")]
    public object? Default { get; set; }
}

/// <summary>
/// Represents the result of an MCP tool execution.
/// </summary>
public class MCPToolResult
{
    /// <summary>
    /// Indicates if the execution was successful.
    /// </summary>
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Result content (can be text, JSON, etc.).
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Additional metadata about the result.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Represents MCP server information.
/// </summary>
public class MCPServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string>? Capabilities { get; set; }
}
