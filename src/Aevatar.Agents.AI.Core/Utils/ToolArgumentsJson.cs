using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Core.Utils;

/// <summary>
/// Tool arguments JSON parser.
///
/// WHY:
/// - LLM function-calling arguments are JSON; numbers have no integer/float distinction.
/// - Many tools expect integers (delay/count/etc). If we always parse to double, tools become fragile.
///
/// POLICY:
/// - Prefer int32/int64 for whole numbers
/// - Fall back to double for non-integral numbers
/// - Keep objects/arrays as <see cref="JsonElement"/> (tools can decide how to interpret)
/// </summary>
public static class ToolArgumentsJson
{
    // ============================================================
    //  公共入口
    // ============================================================
    public static Dictionary<string, object> Parse(string? argumentsJson, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return new Dictionary<string, object>(StringComparer.Ordinal);

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
            if (dict == null || dict.Count == 0)
                return new Dictionary<string, object>(StringComparer.Ordinal);

            var result = new Dictionary<string, object>(dict.Count, StringComparer.Ordinal);
            foreach (var (key, el) in dict)
            {
                result[key] = ConvertJsonElement(el);
            }

            return result;
        }
        catch (JsonException ex)
        {
            // Best-effort: do not fail the whole chat/strategy due to malformed tool args.
            logger?.LogDebug(ex, "Failed to parse tool arguments JSON (best-effort).");
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }

    // ============================================================
    //  Implementation
    // ============================================================
    private static object ConvertJsonElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => TryCoerceNumber(el, out var number) ? number : el,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null!,
            _ => el
        };
    }

    private static bool TryCoerceNumber(JsonElement el, out object value)
    {
        value = 0;
        if (el.ValueKind != JsonValueKind.Number)
            return false;

        if (el.TryGetInt32(out var i32))
        {
            value = i32;
            return true;
        }

        if (el.TryGetInt64(out var i64))
        {
            value = i64;
            return true;
        }

        if (el.TryGetDouble(out var d))
        {
            value = d;
            return true;
        }

        return false;
    }
}



