using Google.Protobuf;

namespace AevatarKit.Core.Serialization;

/// <summary>
/// Protobuf JSON 统一序列化工具。
///
/// 目的：
/// - 前端/后端跨边界类型用 Protobuf 定义（铁律）
/// - 但 MVP 使用 HTTP + JSON 传输，便于调试与快速迭代
/// </summary>
public static class ProtobufJson
{
    public static string ToJson(IMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonFormatter.Default.Format(message);
    }

    public static T Parse<T>(string json)
        where T : class, IMessage<T>, new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }

        var msg = new T();
        // JsonParser.Default.Merge 在当前引用的 Protobuf 包版本不一定存在，使用 Parse + MergeFrom 规避差异。
        var parsed = (T)JsonParser.Default.Parse(json, msg.Descriptor);
        msg.MergeFrom(parsed.ToByteArray());
        return msg;
    }
}


