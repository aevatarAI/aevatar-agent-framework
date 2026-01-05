using System.Threading.Channels;
using Aevatar.Novel.Contracts;

namespace Aevatar.Novel.Sidecar.Services;

// ============================================================
//  SidecarEventHub
//
//  - In-process pub/sub for sidecar -> UI event streaming (SSE/WS/IPC).
//  - Payloads are Protobuf messages (SidecarEvent).
// ============================================================

public sealed class SidecarEventHub
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Channel<SidecarEvent>> _subscribers = new();

    public ChannelReader<SidecarEvent> Subscribe(CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<SidecarEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        lock (_lock)
        {
            _subscribers[id] = channel;
        }

        ct.Register(() =>
        {
            lock (_lock)
            {
                if (_subscribers.Remove(id, out var ch))
                {
                    ch.Writer.TryComplete();
                }
            }
        });

        return channel.Reader;
    }

    public void Publish(SidecarEvent evt)
    {
        List<ChannelWriter<SidecarEvent>> writers;
        lock (_lock)
        {
            writers = _subscribers.Values.Select(s => s.Writer).ToList();
        }

        foreach (var w in writers)
        {
            // Best-effort: if a subscriber is slow/dead, we drop for that subscriber only.
            w.TryWrite(evt);
        }
    }
}


