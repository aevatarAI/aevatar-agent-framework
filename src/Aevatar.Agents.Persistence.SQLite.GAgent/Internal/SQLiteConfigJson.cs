using System.Text.Json;
using Google.Protobuf;

namespace Aevatar.Agents.Persistence.SQLite.GAgent.Internal;

/// <summary>
/// Configuration JSON serialization strategy:
///
/// - If the config type is Protobuf (implements <see cref="IMessage"/>), use Protobuf JSON mapping.
/// - Otherwise fall back to System.Text.Json.
/// </summary>
internal static class SQLiteConfigJson
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    internal static string Serialize<TConfig>(TConfig config)
        where TConfig : class
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config is IMessage msg)
        {
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

        var instance = new TConfig();
        if (instance is IMessage msg)
        {
            var parsed = JsonParser.Default.Parse(json, msg.Descriptor);
            return (TConfig)parsed;
        }

        return JsonSerializer.Deserialize<TConfig>(json, DefaultJsonOptions);
    }
}
