using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools;

/// <summary>
/// Field navigator
/// Provides reflection and JSON Path navigation functionality
/// </summary>
public static class FieldNavigator
{
    /// <summary>
    /// Get field value via reflection, supports JSON Path
    /// </summary>
    public static object? GetFieldValue(
        object obj,
        string? fieldName,
        string? path,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return obj;
        }

        try
        {
            object? fieldValue = null;
            var type = obj.GetType();

            // Try to get property
            var property = type.GetProperty(fieldName);
            if (property != null)
            {
                fieldValue = property.GetValue(obj);
            }
            else
            {
                // Try to get field
                var field = type.GetField(fieldName);
                if (field != null)
                {
                    fieldValue = field.GetValue(obj);
                }
                // If dictionary type
                else if (obj is IDictionary<string, object> dict)
                {
                    dict.TryGetValue(fieldName, out fieldValue);
                }
            }

            // If field value found and has JSON Path, use JSON Path navigation
            if (fieldValue != null && !string.IsNullOrEmpty(path))
            {
                return NavigateJsonPath(fieldValue, path, logger);
            }

            if (fieldValue == null)
            {
                logger?.LogWarning("Field {FieldName} not found in type {TypeName}",
                    fieldName, type.Name);
            }

            return fieldValue;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error getting field value for {FieldName}", fieldName);
            return null;
        }
    }

    /// <summary>
    /// Navigate object using JSON Path
    /// </summary>
    public static object? NavigateJsonPath(
        object obj,
        string path,
        ILogger? logger = null)
    {
        try
        {
            // Convert object to JSON
            var json = JsonSerializer.Serialize(obj);
            var jsonNode = JsonNode.Parse(json);

            if (jsonNode == null)
            {
                return null;
            }

            // Simplified JSON Path support
            var result = NavigateJsonNode(jsonNode, path);

            if (result == null)
            {
                logger?.LogDebug("JSON Path {Path} returned no results", path);
                return null;
            }

            // Return appropriate value based on result type
            return ConvertJsonNodeToObject(result);
        }
        catch (JsonException ex)
        {
            logger?.LogError(ex, "Error parsing JSON path {Path}", path);
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error navigating JSON path {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Navigate JsonNode
    /// </summary>
    private static JsonNode? NavigateJsonNode(JsonNode node, string path)
    {
        // Remove prefix $ or $.
        if (path.StartsWith("$."))
        {
            path = path.Substring(2);
        }
        else if (path.StartsWith("$"))
        {
            path = path.Substring(1);
        }

        // If path is empty, return current node
        if (string.IsNullOrEmpty(path))
        {
            return node;
        }

        var currentNode = node;
        var segments = ParsePathSegments(path);

        foreach (var segment in segments)
        {
            if (currentNode == null)
            {
                return null;
            }

            // Handle array index
            if (segment.StartsWith("[") && segment.EndsWith("]"))
            {
                if (currentNode is JsonArray array)
                {
                    var indexStr = segment.Substring(1, segment.Length - 2);
                    if (int.TryParse(indexStr, out var index) && index >= 0 && index < array.Count)
                    {
                        currentNode = array[index];
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            // Handle object property
            else
            {
                if (currentNode is JsonObject obj && obj.TryGetPropertyValue(segment, out var value))
                {
                    currentNode = value;
                }
                else
                {
                    return null;
                }
            }
        }

        return currentNode;
    }

    /// <summary>
    /// Parse path segments
    /// </summary>
    private static List<string> ParsePathSegments(string path)
    {
        var segments = new List<string>();
        var current = "";
        var inBracket = false;

        for (int i = 0; i < path.Length; i++)
        {
            var ch = path[i];

            if (ch == '[')
            {
                if (!string.IsNullOrEmpty(current))
                {
                    segments.Add(current);
                    current = "";
                }

                inBracket = true;
                current += ch;
            }
            else if (ch == ']')
            {
                current += ch;
                if (inBracket)
                {
                    segments.Add(current);
                    current = "";
                    inBracket = false;
                }
            }
            else if (ch == '.' && !inBracket)
            {
                if (!string.IsNullOrEmpty(current))
                {
                    segments.Add(current);
                    current = "";
                }
            }
            else
            {
                current += ch;
            }
        }

        if (!string.IsNullOrEmpty(current))
        {
            segments.Add(current);
        }

        return segments;
    }

    /// <summary>
    /// Convert JsonNode to object
    /// </summary>
    private static object? ConvertJsonNodeToObject(JsonNode node)
    {
        return node switch
        {
            JsonObject obj => obj.Deserialize<Dictionary<string, object>>(),
            JsonArray arr => arr.Deserialize<List<object>>(),
            JsonValue val => val.GetValueKind() switch
            {
                JsonValueKind.String => val.GetValue<string>(),
                JsonValueKind.Number => val.TryGetValue<long>(out var longVal)
                    ? longVal
                    : val.GetValue<double>(),
                JsonValueKind.True or JsonValueKind.False => val.GetValue<bool>(),
                JsonValueKind.Null => null,
                _ => val.ToString()
            },
            _ => node?.ToString()
        };
    }
}