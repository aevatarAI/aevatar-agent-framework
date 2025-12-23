using System.Globalization;
using System.Text;
using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Agents.Core.Context;

/// <summary>
/// Serializes/deserializes agent context to/from EventEnvelope metadata.
/// Uses protobuf ContextValue oneof for type-safe, extensible serialization.
/// </summary>
/// <remarks>
/// Supported types:
/// - string, bool, int, long, double, DateTime, Guid
/// 
/// Unsupported types will be converted to string via ToString().
/// </remarks>
public static class AgentContextSerializer
{
    /// <summary>
    /// Serialize context to ContextValue dictionary for EventEnvelope.
    /// Uses options to filter keys and enforce limits.
    /// </summary>
    public static IDictionary<string, ContextValue> Serialize(
        IAgentContext context,
        AgentContextPropagationOptions? options = null)
    {
        options ??= AgentContextPropagationOptions.Default;
        var result = new Dictionary<string, ContextValue>();
        var totalBytes = 0;

        foreach (var (key, value) in context.GetAll())
        {
            // Check if key should be propagated
            if (!options.ShouldPropagate(key))
                continue;

            if (value == null)
                continue;

            // Check key limit
            if (result.Count >= options.MaxKeys)
                break;

            var contextValue = ToContextValue(value);

            // ============================================================
            //  Size limit (bytes)
            //
            //  - MaxTotalBytes 是“字节”而不是“字符数”
            //  - 单个 entry 超限：跳过该 entry，而不是 break 影响后续小 entry
            // ============================================================
            var entrySizeBytes = Encoding.UTF8.GetByteCount(key) + contextValue.CalculateSize();
            if (totalBytes + entrySizeBytes > options.MaxTotalBytes)
                continue;

            result[key] = contextValue;
            totalBytes += entrySizeBytes;
        }

        return result;
    }

    /// <summary>
    /// Deserialize context from EventEnvelope metadata.
    /// Uses options to filter keys.
    /// </summary>
    public static void Deserialize(
        IDictionary<string, ContextValue> metadata,
        IAgentContext context,
        AgentContextPropagationOptions? options = null)
    {
        options ??= AgentContextPropagationOptions.Default;

        foreach (var (key, contextValue) in metadata)
        {
            // Check if key should be accepted
            if (!options.ShouldPropagate(key))
                continue;

            var value = FromContextValue(contextValue);
            if (value != null)
            {
                context.Set(key, value);
            }
        }
    }

    /// <summary>
    /// Convert a CLR value to protobuf ContextValue.
    /// </summary>
    public static ContextValue ToContextValue(object value)
    {
        var contextValue = new ContextValue();

        switch (value)
        {
            case string s:
                contextValue.StringValue = s;
                break;
            case bool b:
                contextValue.BoolValue = b;
                break;
            case int i:
                contextValue.IntValue = i;
                break;
            case long l:
                contextValue.IntValue = l;
                break;
            case double d:
                contextValue.DoubleValue = d;
                break;
            case float f:
                contextValue.DoubleValue = f;
                break;
            case DateTime dt:
                contextValue.DatetimeIso = dt.ToString("O", CultureInfo.InvariantCulture);
                break;
            case DateTimeOffset dto:
                contextValue.DatetimeIso = dto.ToString("O", CultureInfo.InvariantCulture);
                break;
            case Guid g:
                contextValue.GuidString = g.ToString("D");
                break;
            default:
                // Fallback: convert to string
                contextValue.StringValue = value.ToString() ?? string.Empty;
                break;
        }

        return contextValue;
    }

    /// <summary>
    /// Convert protobuf ContextValue back to CLR value.
    /// </summary>
    public static object? FromContextValue(ContextValue contextValue)
    {
        return contextValue.ValueCase switch
        {
            ContextValue.ValueOneofCase.StringValue => contextValue.StringValue,
            ContextValue.ValueOneofCase.BoolValue => contextValue.BoolValue,
            ContextValue.ValueOneofCase.IntValue => contextValue.IntValue,
            ContextValue.ValueOneofCase.DoubleValue => contextValue.DoubleValue,
            ContextValue.ValueOneofCase.DatetimeIso => ParseDateTime(contextValue.DatetimeIso),
            ContextValue.ValueOneofCase.GuidString => ParseGuid(contextValue.GuidString),
            ContextValue.ValueOneofCase.None => null,
            _ => null
        };
    }

    private static object ParseDateTime(string iso)
    {
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : iso;
    }

    private static object ParseGuid(string guidString)
    {
        return Guid.TryParse(guidString, out var g) ? g : guidString;
    }

    // NOTE: 字节大小用 protobuf CalculateSize() 精确计算，不再手工估算。
}
