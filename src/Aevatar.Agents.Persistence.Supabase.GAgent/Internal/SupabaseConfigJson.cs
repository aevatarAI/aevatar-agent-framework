using System.Text.Json;
using Google.Protobuf;

namespace Aevatar.Agents.Persistence.Supabase.GAgent.Internal;

/// <summary>
/// Configuration JSON serialization strategy:
///
/// - If the config type is Protobuf (implements <see cref="IMessage"/>), use Protobuf JSON mapping (stable & evolvable)
/// - Otherwise fall back to System.Text.Json (for local-only config objects)
///
/// Note: Framework rule says cross-boundary configs must be Protobuf, so Protobuf is the first priority here.
/// </summary>
internal static class SupabaseConfigJson
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    internal static string Serialize<TConfig>(TConfig config)
        where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config is IMessage msg)
        {
            // Protobuf JSON keeps field/default semantics aligned with Protobuf.
            return JsonFormatter.Default.Format(msg);
        }

        return JsonSerializer.Serialize(config, DefaultJsonOptions);
    }

    internal static TConfig? Deserialize<TConfig>(string json)
        where TConfig : class, new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        // Protobuf JSON: parse via MessageDescriptor (avoids requiring IMessage constraint on TConfig).
        var instance = new TConfig();
        if (instance is IMessage msg)
        {
            var parsed = JsonParser.Default.Parse(json, msg.Descriptor);
            return (TConfig)parsed;
        }

        return JsonSerializer.Deserialize<TConfig>(json, DefaultJsonOptions);
    }
}


