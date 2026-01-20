using System.Collections;
using System.Text.Json;

namespace Aevatar.Platform.Core.Config;

// ============================================================
//  ConfigMapNormalizer
//
//  说明：
//  - 统一把 object / JsonElement 归一化为 Map 结构
// ============================================================
public static class ConfigMapNormalizer
{
    public static Dictionary<string, object?>? Normalize(object? value)
    {
        if (value == null)
            return null;

        if (value is JsonElement json)
            return Normalize(json);

        if (value is JsonDocument doc)
            return Normalize(doc.RootElement);

        if (value is IDictionary map)
            return NormalizeDictionary(map);

        return null;
    }

    public static Dictionary<string, object?>? Normalize(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            var key = (prop.Name ?? string.Empty).Trim();
            if (key.Length == 0)
                continue;
            mapped[key] = NormalizeJsonNode(prop.Value);
        }

        return mapped;
    }

    private static Dictionary<string, object?> NormalizeDictionary(IDictionary map)
    {
        var mapped = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry kv in map)
        {
            var key = (kv.Key?.ToString() ?? string.Empty).Trim();
            if (key.Length == 0)
                continue;
            mapped[key] = NormalizeObjectNode(kv.Value);
        }

        return mapped;
    }

    private static object? NormalizeObjectNode(object? value)
    {
        if (value == null)
            return null;

        if (value is JsonElement json)
            return NormalizeJsonNode(json);

        if (value is IDictionary map)
            return NormalizeDictionary(map);

        if (value is IEnumerable list && value is not string)
        {
            var items = new List<object?>();
            foreach (var item in list)
                items.Add(NormalizeObjectNode(item));
            return items;
        }

        return value;
    }

    private static object? NormalizeJsonNode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return Normalize(element);
            case JsonValueKind.Array:
                var items = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    items.Add(NormalizeJsonNode(item));
                return items;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Number:
                return element.ToString();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return element.ToString();
        }
    }
}
