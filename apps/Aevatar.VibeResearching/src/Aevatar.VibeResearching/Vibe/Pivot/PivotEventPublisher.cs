using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibeResearching.Vibe.Pivot.Messages;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Vibe.Pivot;

/// <summary>
/// Publishes pivot events to subagents and tracks acknowledgments.
/// Uses in-memory channels for session-scoped event distribution.
/// </summary>
public sealed class PivotEventPublisher : IPivotEventPublisher, IDisposable
{
    private readonly PivotOptions _options;
    private readonly ILogger<PivotEventPublisher> _logger;

    // Session -> (SubscriberId -> Channel)
    private readonly ConcurrentDictionary<string, SessionEventHub> _sessionHubs = new();

    // PivotId -> Acknowledgment tracking
    private readonly ConcurrentDictionary<string, AckTracker> _ackTrackers = new();

    // Session -> Registered agents
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _registeredAgents = new();

    public PivotEventPublisher(
        IOptions<PivotOptions> options,
        ILogger<PivotEventPublisher> logger)
    {
        _options = options?.Value ?? new PivotOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task PublishPivotEventAsync(PivotEvent pivotEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pivotEvent);

        var sessionId = pivotEvent.SessionId;
        var pivotId = pivotEvent.PivotId;

        _logger.LogInformation(
            "Publishing pivot event {PivotId} for session {SessionId}: {OldDirection} -> {NewDirection}",
            pivotId, sessionId, pivotEvent.OldDirectionSummary, pivotEvent.NewDirection);

        // Initialize acknowledgment tracker for this pivot
        var expectedAgents = GetRegisteredAgents(sessionId);
        _ackTrackers[pivotId] = new AckTracker(expectedAgents);

        // Get or create session hub
        var hub = _sessionHubs.GetOrAdd(sessionId, _ => new SessionEventHub());

        // Publish to all subscribers (non-blocking fan-out)
        await hub.PublishAsync(pivotEvent, cancellationToken);

        _logger.LogDebug(
            "Pivot event {PivotId} published to {SubscriberCount} subscribers",
            pivotId, hub.SubscriberCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PivotAcknowledgment>> WaitForAcknowledgmentsAsync(
        string pivotId,
        IEnumerable<string> expectedAgents,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pivotId);

        if (!_ackTrackers.TryGetValue(pivotId, out var tracker))
        {
            // Create tracker if not exists (for late-start scenarios)
            tracker = new AckTracker(expectedAgents);
            _ackTrackers[pivotId] = tracker;
        }

        var effectiveTimeout = timeout > TimeSpan.Zero
            ? timeout
            : TimeSpan.FromSeconds(_options.SubagentAckTimeoutSeconds);

        _logger.LogDebug(
            "Waiting for acknowledgments for pivot {PivotId}, timeout={TimeoutMs}ms, expected={ExpectedCount}",
            pivotId, effectiveTimeout.TotalMilliseconds, tracker.ExpectedCount);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        try
        {
            // Wait until all expected agents acknowledge or timeout
            while (!tracker.IsComplete)
            {
                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred - this is expected, proceed with partial acknowledgments
            _logger.LogWarning(
                "Acknowledgment timeout for pivot {PivotId}: received {ReceivedCount}/{ExpectedCount}",
                pivotId, tracker.ReceivedCount, tracker.ExpectedCount);
        }

        var acks = tracker.GetReceivedAcknowledgments();

        // Cleanup tracker after retrieval
        _ackTrackers.TryRemove(pivotId, out _);

        _logger.LogInformation(
            "Pivot {PivotId} acknowledgment collection complete: {ReceivedCount}/{ExpectedCount}",
            pivotId, acks.Count, tracker.ExpectedCount);

        return acks;
    }

    /// <inheritdoc />
    public void RecordAcknowledgment(PivotAcknowledgment acknowledgment)
    {
        ArgumentNullException.ThrowIfNull(acknowledgment);

        var pivotId = acknowledgment.PivotId;
        var agentId = acknowledgment.AgentId;

        if (_ackTrackers.TryGetValue(pivotId, out var tracker))
        {
            tracker.RecordAck(acknowledgment);
            _logger.LogDebug(
                "Recorded acknowledgment from {AgentId} for pivot {PivotId}: {Status}",
                agentId, pivotId, acknowledgment.Status);
        }
        else
        {
            _logger.LogWarning(
                "Received acknowledgment for unknown pivot {PivotId} from {AgentId}",
                pivotId, agentId);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PivotEvent> SubscribeAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var hub = _sessionHubs.GetOrAdd(sessionId, _ => new SessionEventHub());

        await foreach (var evt in hub.SubscribeAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredAgents(string sessionId)
    {
        if (_registeredAgents.TryGetValue(sessionId, out var agents))
        {
            return agents.Keys.ToList();
        }

        return [];
    }

    /// <inheritdoc />
    public void RegisterAgent(string sessionId, string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var agents = _registeredAgents.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, byte>());
        agents.TryAdd(agentId, 0);

        _logger.LogDebug("Registered agent {AgentId} for session {SessionId}", agentId, sessionId);
    }

    /// <inheritdoc />
    public void UnregisterAgent(string sessionId, string agentId)
    {
        if (_registeredAgents.TryGetValue(sessionId, out var agents))
        {
            agents.TryRemove(agentId, out _);
            _logger.LogDebug("Unregistered agent {AgentId} from session {SessionId}", agentId, sessionId);
        }
    }

    public void Dispose()
    {
        foreach (var hub in _sessionHubs.Values)
        {
            hub.Complete();
        }

        _sessionHubs.Clear();
        _ackTrackers.Clear();
        _registeredAgents.Clear();
    }

    /// <summary>
    /// Session-scoped event hub for pivot events (multi-subscriber fan-out).
    /// </summary>
    private sealed class SessionEventHub
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, Channel<PivotEvent>> _subscribers = new();
        private ChannelWriter<PivotEvent>[] _writersSnapshot = [];
        private int _nextId;
        private bool _completed;

        public int SubscriberCount
        {
            get
            {
                lock (_lock)
                {
                    return _subscribers.Count;
                }
            }
        }

        public Task PublishAsync(PivotEvent evt, CancellationToken ct)
        {
            ChannelWriter<PivotEvent>[] writers;
            lock (_lock)
            {
                if (_completed) return Task.CompletedTask;
                writers = _writersSnapshot;
            }

            foreach (var w in writers)
            {
                w.TryWrite(evt);
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<PivotEvent> SubscribeAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Channel<PivotEvent>? ch;
            int id;

            lock (_lock)
            {
                if (_completed) yield break;

                id = ++_nextId;
                ch = Channel.CreateUnbounded<PivotEvent>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = true
                });
                _subscribers[id] = ch;
                RefreshWritersSnapshot();
            }

            try
            {
                await foreach (var item in ch.Reader.ReadAllAsync(ct))
                {
                    yield return item;
                }
            }
            finally
            {
                lock (_lock)
                {
                    _subscribers.Remove(id);
                    RefreshWritersSnapshot();
                }

                ch.Writer.TryComplete();
            }
        }

        public void Complete()
        {
            ChannelWriter<PivotEvent>[] writers;
            lock (_lock)
            {
                if (_completed) return;
                _completed = true;
                writers = _writersSnapshot;
                _subscribers.Clear();
                _writersSnapshot = [];
            }

            foreach (var w in writers)
            {
                w.TryComplete();
            }
        }

        private void RefreshWritersSnapshot()
        {
            _writersSnapshot = _subscribers.Count == 0
                ? []
                : _subscribers.Values.Select(x => x.Writer).ToArray();
        }
    }

    /// <summary>
    /// Tracks acknowledgments for a single pivot operation.
    /// </summary>
    private sealed class AckTracker
    {
        private readonly HashSet<string> _expectedAgents;
        private readonly ConcurrentDictionary<string, PivotAcknowledgment> _received = new();

        public AckTracker(IEnumerable<string> expectedAgents)
        {
            _expectedAgents = new HashSet<string>(expectedAgents, StringComparer.OrdinalIgnoreCase);
        }

        public int ExpectedCount => _expectedAgents.Count;
        public int ReceivedCount => _received.Count;

        public bool IsComplete =>
            _expectedAgents.Count == 0 ||
            _expectedAgents.All(a => _received.ContainsKey(a));

        public void RecordAck(PivotAcknowledgment ack)
        {
            _received[ack.AgentId] = ack;
        }

        public IReadOnlyList<PivotAcknowledgment> GetReceivedAcknowledgments()
        {
            return _received.Values.ToList();
        }
    }
}
