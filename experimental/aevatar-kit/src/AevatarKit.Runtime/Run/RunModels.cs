using System.Collections.Concurrent;
using System.Threading.Channels;
using AevatarKit;

namespace AevatarKit.Runtime.Run;

/// <summary>
/// Run state stored in-memory for MVP.
/// </summary>
public sealed class RunInstance
{
    public required string RunId { get; init; }
    public required DateTime CreatedAtUtc { get; init; }

    public string? SessionId { get; set; }
    public string? GraphId { get; set; }
    public string? GraphName { get; set; }
    public string? GraphSource { get; set; }
    public string? Input { get; set; }

    public Channel<WorkflowStepEvent> EventChannel { get; } =
        Channel.CreateUnbounded<WorkflowStepEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });

    /// <summary>
    /// Append-only event log for run history / replay (MVP).
    /// </summary>
    public ConcurrentQueue<WorkflowStepEvent> EventHistory { get; } = new();

    public ConcurrentQueue<MemoryEntry> MemoryEntries { get; } = new();

    public CancellationTokenSource Cancellation { get; } = new();
}


