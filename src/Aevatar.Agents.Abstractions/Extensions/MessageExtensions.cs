using System.Runtime.CompilerServices;
using System.Text;
using Aevatar.Agents.Abstractions.Helpers;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Abstractions.Extensions;

/// <summary>
/// Provides extension methods for message operations.
/// </summary>
public static class MessageExtensions
{
    /// <summary>
    /// Cache for MessageDescriptor to avoid reflection or repeated instantiation.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    private static class DescriptorCache<T> where T : IMessage, new()
    {
        public static readonly Google.Protobuf.Reflection.MessageDescriptor Descriptor;

        static DescriptorCache()
        {
            // Create a temporary instance to get the descriptor.
            // This runs only once per type T.
            Descriptor = new T().Descriptor;
        }
    }

    /// <summary>
    /// Determines whether the event envelope contains a payload of the specified type.
    /// </summary>
    public static bool HasPayload<T>(this EventEnvelope envelope) where T : IMessage, new()
    {
        return envelope.Payload != null && envelope.Payload.Is(DescriptorCache<T>.Descriptor);
    }

    /// <summary>
    /// Unpacks the payload of the specified type from the event envelope.
    /// </summary>
    public static T? UnpackPayload<T>(this EventEnvelope envelope) where T : IMessage, new()
    {
        if (envelope.Payload == null || !envelope.HasPayload<T>())
        {
            return default;
        }

        return envelope.Payload.Unpack<T>();
    }

    /// <summary>
    /// Creates an event envelope with the specified payload.
    /// </summary>
    public static EventEnvelope CreateEventEnvelope<T>(this T payload, long version = 1) where T : IMessage
    {
        return new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = TimestampHelper.GetUtcNow(),
            Version = version,
            Payload = Any.Pack(payload)
        };
    }

    /// <summary>
    /// Batch streaming chunks by count (preserve order).
    ///
    /// 中文 + ASCII:
    /// - chunkEvery <= 1 => 每个 chunk 立刻返回
    /// - 保持顺序 + 最后 flush
    /// - 不引入额外线程/任务
    /// </summary>
    public static async IAsyncEnumerable<string> BatchByCountAsync(
        this IAsyncEnumerable<string> source,
        int chunkEvery,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var normalized = Math.Clamp(chunkEvery, 1, 128);
        var pending = new StringBuilder();
        var count = 0;

        await foreach (var chunk in source.WithCancellation(ct))
        {
            if (string.IsNullOrEmpty(chunk))
                continue;

            pending.Append(chunk);
            count++;

            if (normalized > 1 && count % normalized != 0)
                continue;

            yield return pending.ToString();
            pending.Clear();
        }

        if (pending.Length > 0)
            yield return pending.ToString();
    }
}