using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Aevatar.Agents.Cognitive.Streaming;

// ============================================================
//  BROADCAST EVENT HUB
//  职责：把“单消费者 Channel”升级为“多订阅广播”。
//
//  WHY:
//  - 原生 Channel<T> 是 queue 语义：多个 reader 会竞争消费，导致 SSE 多连接“抢消息”。
//  - 这里提供 pub-sub：每个 subscriber 拿到自己独立的 ChannelReader<T>，事件 fan-out。
//  - 支持小容量 replay buffer：新连接可回放最近 N 条事件（避免 UI 空窗）。
// ============================================================

public sealed class BroadcastEventHub<T>
{
    private readonly ConcurrentQueue<T> _replay = new();
    private ChannelWriter<T>[] _writersSnapshot = [];
    private readonly int _replayBufferSize;
    private readonly int _subscriberBufferSize;
    private readonly Action<string>? _warningLogger;
    private readonly string _hubName;
    private long _dropLogAtMs;
    private int _dropCount;
    private int _replayCount;
    private int _completed;

    public BroadcastEventHub(
        int replayBufferSize = 256,
        int subscriberBufferSize = 512,
        Action<string>? warningLogger = null,
        string? hubName = null)
    {
        _replayBufferSize = Math.Max(0, replayBufferSize);
        _subscriberBufferSize = Math.Max(0, subscriberBufferSize);
        _warningLogger = warningLogger;
        _hubName = string.IsNullOrWhiteSpace(hubName) ? typeof(T).Name : hubName.Trim();
    }

    public void Publish(T evt)
    {
        // 边界层：永远不要抛异常（SSE/Progress 回调里抛异常会直接杀进程）
        try
        {
            if (Volatile.Read(ref _completed) != 0) return;

            if (_replayBufferSize > 0)
            {
                _replay.Enqueue(evt);
                var count = Interlocked.Increment(ref _replayCount);
                if (count > _replayBufferSize)
                    TrimReplay();
            }

            // Hot path optimization:
            // - Publish 是高频路径（token streaming 下可能是“每 token 一次”）
            // - 避免在这里分配 List/ToList；改为读取订阅变更时维护的 writers 快照
            var writers = Volatile.Read(ref _writersSnapshot);

            foreach (var w in writers)
            {
                if (!w.TryWrite(evt))
                    LogDropWarning(writers.Length);
            }
        }
        catch
        {
            // ignored
        }
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return;

        var writers = Interlocked.Exchange(ref _writersSnapshot, Array.Empty<ChannelWriter<T>>());

        foreach (var w in writers)
            w.TryComplete();
    }

    public async IAsyncEnumerable<T> SubscribeAsync(
        bool replay = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<T>? snapshot = null;
        Channel<T>? ch = null;

        if (replay && _replayBufferSize > 0 && !_replay.IsEmpty)
            snapshot = _replay.ToList();

        if (Volatile.Read(ref _completed) == 0)
        {
            ch = CreateSubscriberChannel();
            AddWriter(ch.Writer);

            // 竞争：完成信号在订阅期间触发时，主动撤销
            if (Volatile.Read(ref _completed) != 0)
            {
                RemoveWriter(ch.Writer);
                ch.Writer.TryComplete();
                ch = null;
            }
        }

        if (snapshot is not null)
        {
            foreach (var item in snapshot)
                yield return item;
        }

        if (ch is null)
            yield break;

        try
        {
            await foreach (var item in ch.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            // best-effort: in case someone is still awaiting
            RemoveWriter(ch.Writer);
            ch.Writer.TryComplete();
        }
    }

    private void AddWriter(ChannelWriter<T> writer)
    {
        // NOTE: subscribe 是低频路径，允许 CAS + 分配新数组。
        while (true)
        {
            var snapshot = Volatile.Read(ref _writersSnapshot);
            var updated = new ChannelWriter<T>[snapshot.Length + 1];
            Array.Copy(snapshot, updated, snapshot.Length);
            updated[^1] = writer;

            if (Interlocked.CompareExchange(ref _writersSnapshot, updated, snapshot) == snapshot)
                return;
        }
    }

    private Channel<T> CreateSubscriberChannel()
    {
        // NOTE: 有界 buffer 能避免慢订阅者拖垮内存；满了就丢消息 + warning。
        if (_subscriberBufferSize <= 0)
            return Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true
            });

        return Channel.CreateBounded<T>(new BoundedChannelOptions(_subscriberBufferSize)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    private void LogDropWarning(int subscriberCount)
    {
        // NOTE: 聚合日志，避免 token streaming 场景刷屏。
        Interlocked.Increment(ref _dropCount);
        var now = Environment.TickCount64;
        var last = Volatile.Read(ref _dropLogAtMs);
        if (now - last < 1000) return;

        if (Interlocked.CompareExchange(ref _dropLogAtMs, now, last) != last)
            return;

        var dropped = Interlocked.Exchange(ref _dropCount, 0);
        EmitWarning(
            $"BroadcastEventHub drop: hub={_hubName}, subscribers={subscriberCount}, " +
            $"subscriberBuffer={_subscriberBufferSize}, replayBuffer={_replayBufferSize}, " +
            $"dropped={dropped} message(s).");
    }

    private void EmitWarning(string message)
    {
        if (_warningLogger is not null)
        {
            _warningLogger(message);
            return;
        }

        Trace.TraceWarning(message);
    }

    private void RemoveWriter(ChannelWriter<T> writer)
    {
        // NOTE: unsubscribe 是低频路径，允许 CAS + 分配新数组。
        while (true)
        {
            var snapshot = Volatile.Read(ref _writersSnapshot);
            if (snapshot.Length == 0) return;

            var index = Array.IndexOf(snapshot, writer);
            if (index < 0) return;

            if (snapshot.Length == 1)
            {
                if (Interlocked.CompareExchange(ref _writersSnapshot, Array.Empty<ChannelWriter<T>>(), snapshot) == snapshot)
                    return;
                continue;
            }

            var updated = new ChannelWriter<T>[snapshot.Length - 1];
            if (index > 0)
                Array.Copy(snapshot, 0, updated, 0, index);
            if (index < snapshot.Length - 1)
                Array.Copy(snapshot, index + 1, updated, index, snapshot.Length - index - 1);

            if (Interlocked.CompareExchange(ref _writersSnapshot, updated, snapshot) == snapshot)
                return;
        }
    }

    private void TrimReplay()
    {
        // NOTE: replay buffer 是 best-effort，允许并发下略微超/欠清理。
        while (Volatile.Read(ref _replayCount) > _replayBufferSize && _replay.TryDequeue(out _))
            Interlocked.Decrement(ref _replayCount);
    }
}

