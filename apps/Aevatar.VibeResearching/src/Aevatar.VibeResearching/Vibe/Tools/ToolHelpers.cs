using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace VibeResearching.Vibe.Tools;

/// <summary>
/// Base class for Vibe tools with common helper methods.
/// Extends AevatarToolBase to use the framework's tool infrastructure.
/// </summary>
internal abstract class VibeToolBase : AevatarToolBase
{
    protected static string GetSessionId(ToolContext? context)
    {
        var sessionId = (context?.GetSessionIdCallback?.Invoke() ?? string.Empty).Trim();
        if (sessionId.Length == 0)
            throw new InvalidOperationException("missing sessionId in tool context");
        return sessionId;
    }

    protected static string GetRequiredString(Dictionary<string, object> parameters, string name)
    {
        var value = (parameters.GetValueOrDefault(name)?.ToString() ?? "").Trim();
        if (value.Length == 0)
            throw new ArgumentException($"{name} is required");
        return value;
    }
}

/// <summary>
/// JsonElement extension methods for parsing tool parameters.
/// </summary>
internal static class JsonElementExtensions
{
    public static string GetPropertyOrDefault(this JsonElement element, string propertyName, string defaultValue)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? defaultValue
            : defaultValue;
    }

    public static string? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    public static List<string>? GetStringArray(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var result = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                result.Add(s.Trim());
        }
        return result.Count > 0 ? result : null;
    }
}
