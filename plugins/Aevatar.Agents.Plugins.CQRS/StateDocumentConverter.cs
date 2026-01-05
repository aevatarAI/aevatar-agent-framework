using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions.CQRS;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Plugins.CQRS;

/// <summary>
/// Shared converter for transforming StateWrapper to StateIndexDocument.
/// Used by both ElasticsearchStateProjector and BatchedStateProjector.
/// </summary>
public class StateDocumentConverter
{
    private readonly ILogger _logger;
    
    // Core caches (only what's necessary)
    private static readonly ConcurrentDictionary<string, Type?> _typeCache = new();
    private static readonly ConcurrentDictionary<Type, MessageParser?> _parserCache = new();
    private static readonly ConcurrentDictionary<Type, (PropertyInfo? Key, PropertyInfo? Value)> _kvAccessorCache = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // ============================================================
    //  Search-friendly projection fields (for FTS / tools)
    //
    //  WHY:
    //  - Generic JSON serialization of map/repeated fields is searchable but noisy.
    //  - Tools like `search_memory` benefit from compact text fields:
    //    - historyText: role/content transcript (tail)
    //    - contextText: key/value pairs
    //    - historySummary: rolling summary (if present in context)
    // ============================================================
    private const int SearchTextMaxChars = 20_000;
    private const int MapTextMaxEntries = 200;
    private const int RepeatedTextMaxItems = 80;
    private const int LineMaxChars = 800;
    private const string HistorySummaryKey = "history_summary";

    public StateDocumentConverter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Convert StateWrapper to StateIndexDocument with full state unpacking.
    /// </summary>
    public StateIndexDocument Convert(StateWrapper wrapper)
    {
        var data = new Dictionary<string, object>();

        // Try to resolve and unpack state
        var stateType = ResolveStateType(wrapper.AgentType, wrapper.StateData);
        var state = UnpackState(wrapper.StateData, stateType);

        if (state != null && stateType != null)
        {
            ExtractStateProperties(state, stateType, data);
        }

        return new StateIndexDocument
        {
            AgentId = wrapper.AgentId,
            AgentType = wrapper.AgentType,
            Data = data,
            Version = wrapper.Version,
            IndexedAt = wrapper.PublishedAt?.ToDateTime() ?? DateTime.UtcNow
        };
    }

    #region State Resolution

    /// <summary>
    /// Resolve the state type from TypeUrl with caching.
    /// </summary>
    public Type? ResolveStateType(string agentType, Google.Protobuf.WellKnownTypes.Any? stateData)
    {
        if (stateData == null || string.IsNullOrEmpty(stateData.TypeUrl))
            return null;

        var typeUrl = stateData.TypeUrl;
        
        // Check cache first
        if (_typeCache.TryGetValue(typeUrl, out var cachedType))
            return cachedType;

        // Parse TypeUrl: type.googleapis.com/package.TypeName -> TypeName
        var typeName = typeUrl.Contains('/')
            ? typeUrl.Substring(typeUrl.LastIndexOf('/') + 1)
            : typeUrl;
        
        // Simple type name (after last dot)
        var simpleName = typeName.Contains('.')
            ? typeName.Substring(typeName.LastIndexOf('.') + 1)
            : typeName;

        // Search for type (only runs once per TypeUrl due to caching)
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t => 
                typeof(IMessage).IsAssignableFrom(t) && 
                !t.IsAbstract && 
                (t.Name == simpleName || t.FullName == typeName));

        // Cache result (including null for negative caching)
        _typeCache[typeUrl] = type;
        
        if (type == null)
        {
            _logger.LogWarning("Could not resolve type for {AgentType} from {TypeUrl}", agentType, typeUrl);
        }

        return type;
    }

    private IMessage? UnpackState(Google.Protobuf.WellKnownTypes.Any? stateData, Type? stateType)
    {
        if (stateData == null || stateType == null || !typeof(IMessage).IsAssignableFrom(stateType))
            return null;

        try
        {
            // Get parser from cache or resolve once
            var parser = _parserCache.GetOrAdd(stateType, type =>
            {
                var parserProperty = type.GetProperty("Parser",
                    BindingFlags.Public | BindingFlags.Static);
                return parserProperty?.GetValue(null) as MessageParser;
            });

            if (parser == null)
            {
                _logger.LogWarning("No Parser found for type {TypeName}", stateType.Name);
                return null;
            }

            return parser.ParseFrom(stateData.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unpack state of type {TypeName}", stateType?.Name);
            return null;
        }
    }

    #endregion

    #region Property Extraction

    private void ExtractStateProperties(IMessage state, Type stateType, Dictionary<string, object> data)
    {
        foreach (var property in stateType.GetProperties())
        {
            // Skip Protobuf internal properties
            if (property.Name is "Parser" or "Descriptor" or "MessageType" ||
                property.DeclaringType == typeof(IMessage) ||
                property.DeclaringType == typeof(object))
                continue;

            try
            {
                var value = property.GetValue(state);
                if (value == null) continue;

                var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..]; // camelCase

                data[name] = value switch
                {
                    Google.Protobuf.WellKnownTypes.Timestamp ts => ts.ToDateTime(),
                    _ when IsBasicType(property.PropertyType) => value,
                    _ => JsonSerializer.Serialize(value, _jsonOptions)
                };

                // Additional: add compact search-friendly text fields for complex protobuf containers.
                // This is best-effort and never overrides existing keys.
                TryAddSearchFriendlyFields(name, property.PropertyType, value, data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract property {Property}", property.Name);
            }
        }
    }

    private void TryAddSearchFriendlyFields(
        string propertyNameCamel,
        Type propertyType,
        object value,
        Dictionary<string, object> data)
    {
        // MapField<string,string> -> <propertyName>Text + optional historySummary
        if (TryBuildMapText(propertyType, value, out var mapText, out var historySummary))
        {
            if (!string.IsNullOrWhiteSpace(mapText))
            {
                data.TryAdd($"{propertyNameCamel}Text", mapText);
            }

            if (!string.IsNullOrWhiteSpace(historySummary))
            {
                // Stable key for tools.
                data.TryAdd("historySummary", historySummary);
            }
        }

        // RepeatedField<TMessage> with Content -> <propertyName>Text
        if (TryBuildRepeatedMessageText(propertyType, value, out var repeatedText))
        {
            if (!string.IsNullOrWhiteSpace(repeatedText))
            {
                data.TryAdd($"{propertyNameCamel}Text", repeatedText);
            }
        }
    }

    private bool TryBuildMapText(
        Type propertyType,
        object value,
        out string? mapText,
        out string? historySummary)
    {
        mapText = null;
        historySummary = null;

        if (!propertyType.IsGenericType)
            return false;

        if (propertyType.GetGenericTypeDefinition() != typeof(MapField<,>))
            return false;

        var args = propertyType.GetGenericArguments();
        if (args.Length != 2)
            return false;

        // Only optimize MapField<string,string> (common for Context).
        if (args[0] != typeof(string) || args[1] != typeof(string))
            return false;

        if (value is not IEnumerable enumerable)
            return false;

        var sb = new StringBuilder();
        var count = 0;

        foreach (var item in enumerable)
        {
            if (item == null) continue;
            if (count >= MapTextMaxEntries) break;

            var (keyProp, valueProp) = GetKeyValueAccessors(item.GetType());
            if (keyProp == null || valueProp == null) continue;

            var k = keyProp.GetValue(item)?.ToString();
            var v = valueProp.GetValue(item)?.ToString();

            if (string.IsNullOrWhiteSpace(k) || string.IsNullOrWhiteSpace(v))
                continue;

            if (k.Equals(HistorySummaryKey, StringComparison.OrdinalIgnoreCase))
            {
                historySummary = TrimKeepTail(v.Trim(), SearchTextMaxChars);
            }

            // Keep line compact to avoid indexing huge blobs.
            var line = $"{k}: {TrimLine(v)}";
            sb.AppendLine(line);
            count++;
        }

        var text = sb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            mapText = TrimKeepTail(text, SearchTextMaxChars);
            return true;
        }

        return historySummary != null;
    }

    private bool TryBuildRepeatedMessageText(
        Type propertyType,
        object value,
        out string? repeatedText)
    {
        repeatedText = null;

        if (!propertyType.IsGenericType)
            return false;

        if (propertyType.GetGenericTypeDefinition() != typeof(RepeatedField<>))
            return false;

        var itemType = propertyType.GetGenericArguments().FirstOrDefault();
        if (itemType == null)
            return false;

        var contentProp = itemType.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public);
        if (contentProp == null || contentProp.PropertyType != typeof(string))
            return false;

        var roleProp = itemType.GetProperty("Role", BindingFlags.Instance | BindingFlags.Public);

        // Prefer tail items (more recent is usually more relevant).
        var items = new List<object>();

        if (value is IList list)
        {
            var start = Math.Max(0, list.Count - RepeatedTextMaxItems);
            for (var i = start; i < list.Count; i++)
            {
                if (list[i] != null) items.Add(list[i]!);
            }
        }
        else if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item == null) continue;
                items.Add(item);
                if (items.Count > RepeatedTextMaxItems)
                {
                    // Keep tail only
                    items.RemoveAt(0);
                }
            }
        }
        else
        {
            return false;
        }

        if (items.Count == 0)
            return false;

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var content = contentProp.GetValue(item) as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
                continue;

            content = TrimLine(content);
            if (roleProp != null)
            {
                var role = roleProp.GetValue(item)?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(role))
                {
                    sb.Append(role);
                    sb.Append(": ");
                }
            }

            sb.AppendLine(content);
        }

        var text = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        repeatedText = TrimKeepTail(text, SearchTextMaxChars);
        return true;
    }

    private static (PropertyInfo? Key, PropertyInfo? Value) GetKeyValueAccessors(Type kvType)
    {
        return _kvAccessorCache.GetOrAdd(kvType, t =>
        {
            // KeyValuePair<K,V> style
            var key = t.GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
            var val = t.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            return (key, val);
        });
    }

    private static string TrimLine(string s)
    {
        var text = (s ?? string.Empty).Replace("\r", "").Replace("\n", " ").Trim();
        if (text.Length <= LineMaxChars) return text;
        return text[..LineMaxChars] + "…";
    }

    private static string TrimKeepTail(string s, int maxChars)
    {
        if (maxChars <= 0) return string.Empty;
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= maxChars) return s;
        return s[^maxChars..];
    }

    private static bool IsBasicType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t == typeof(string) || t == typeof(DateTime) || 
               t == typeof(decimal) || t == typeof(Guid) || 
               t == typeof(Google.Protobuf.WellKnownTypes.Timestamp);
    }

    #endregion
}

