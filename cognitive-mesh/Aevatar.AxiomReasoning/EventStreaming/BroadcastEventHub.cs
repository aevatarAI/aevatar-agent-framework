using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Aevatar.AxiomReasoning.EventStreaming;

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
    private readonly object _lock = new();
    private readonly Dictionary<int, Channel<T>> _subscribers = new();
    private ChannelWriter<T>[] _writersSnapshot = [];
    private readonly Queue<T> _replay;
    private readonly int _replayBufferSize;
    private int _nextSubscriberId;
    private bool _completed;

    public BroadcastEventHub(int replayBufferSize = 256)
    {
        _replayBufferSize = Math.Max(0, replayBufferSize);
        _replay = new Queue<T>(_replayBufferSize > 0 ? _replayBufferSize : 4);
    }

    public void Publish(T evt)
    {
        // 边界层：永远不要抛异常（SSE/Progress 回调里抛异常会直接杀进程）
        try
        {
            ChannelWriter<T>[] writers;
            lock (_lock)
            {
                if (_completed) return;

                if (_replayBufferSize > 0)
                {
                    _replay.Enqueue(evt);
                    while (_replay.Count > _replayBufferSize)
                        _replay.Dequeue();
                }

                // Hot path optimization:
                // - Publish 是高频路径（token streaming 下可能是“每 token 一次”）
                // - 避免在这里分配 List/ToList；改为读取订阅变更时维护的 writers 快照
                writers = _writersSnapshot;
            }

            foreach (var w in writers)
                w.TryWrite(evt);
        }
        catch
        {
            // ignored
        }
    }

    public void Complete()
    {
        ChannelWriter<T>[] writers;
        lock (_lock)
        {
            if (_completed) return;
            _completed = true;
            writers = _writersSnapshot;
            _subscribers.Clear();
            _writersSnapshot = [];
        }

        foreach (var w in writers)
            w.TryComplete();
    }

    public async IAsyncEnumerable<T> SubscribeAsync(
        bool replay = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        List<T>? snapshot = null;
        Channel<T>? ch = null;
        var id = 0;

        lock (_lock)
        {
            if (replay && _replay.Count > 0)
                snapshot = _replay.ToList();

            if (_completed)
                ch = null;
            else
            {
                id = ++_nextSubscriberId;
                ch = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = true
                });
                _subscribers[id] = ch;
                RefreshWritersSnapshotLocked();
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
            lock (_lock)
            {
                _subscribers.Remove(id);
                RefreshWritersSnapshotLocked();
            }

            // best-effort: in case someone is still awaiting
            ch.Writer.TryComplete();
        }
    }

    private void RefreshWritersSnapshotLocked()
    {
        // NOTE: subscribe/unsubscribe 是低频路径，允许分配一次新数组。
        _writersSnapshot = _subscribers.Count == 0
            ? []
            : _subscribers.Values.Select(x => x.Writer).ToArray();
    }
}

