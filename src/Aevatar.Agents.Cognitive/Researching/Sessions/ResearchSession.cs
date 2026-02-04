using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Cognitive.Streaming;
using Aevatar.Agents.Core.Runtime;
using Aevatar.Agents.Cognitive.Researching.Round;
using VibeResearching.Contracts.Sessions;

namespace Aevatar.Agents.Cognitive.Researching.Sessions;

public sealed class ResearchSession(string id, DateTimeOffset? createdAt = null)
{
    private const string EventsHubName = "ResearchSession.Events";
    // ============================================================
    //  Global Knowledge Graph
    //
    //  All sessions share a single global KnowledgeGraph by default.
    //  This enables:
    //  - Cross-session knowledge node visibility
    //  - Cross-session knowledge node connections
    //  - Research in any session can build upon knowledge from other sessions
    // ============================================================
    public const string GlobalDagId = "global";

    public string Id { get; } = id;
    public DateTimeOffset CreatedAt { get; } = createdAt ?? DateTimeOffset.UtcNow;
    public string? ProviderName { get; init; }

    // ------------------------------------------------------------
    // DAG binding
    //
    // 中文说明：
    // - 默认：所有 session 共享全局 KnowledgeGraph（dagId == "global"）
    // - 这样所有 session 的知识节点可以互相连接、互相引用
    // - 如需隔离，可手动设置 DagId = sessionId
    // ------------------------------------------------------------
    public string? DagId { get; set; }

    /// <summary>
    /// Get the effective DAG ID for this session.
    /// Defaults to GlobalDagId for cross-session knowledge sharing.
    /// </summary>
    public string EffectiveDagId => string.IsNullOrWhiteSpace(DagId) ? GlobalDagId : DagId.Trim();

    public BroadcastEventHub<AgUiEvent> Events { get; } = new(replayBufferSize: 0, hubName: EventsHubName);

    // Lightweight server-side workspace state (rendered via AG-UI STATE_SNAPSHOT/DELTA).
    public ResearchWorkspaceState Workspace { get; } = new()
    {
        SessionId = id
    };

    // ------------------------------------------------------------
    // Message log (snapshot-first reconnect)
    //
    // Why:
    // - Multi-agent runs may not write into a single agent's State.History.
    // - We keep a small server-side canonical message log so reconnect always works.
    // ------------------------------------------------------------
    private readonly object _messagesLock = new();
    private readonly List<AgUiMessage> _messages = new();
    private readonly Dictionary<string, int> _messageIndex = new(StringComparer.Ordinal);

    // Serialize chat runs per session (avoid concurrent tool loops / history corruption).
    public SemaphoreSlim RunLock { get; } = new(1, 1);

    private int _runSeq;

    public int NextRunSeq() => Interlocked.Increment(ref _runSeq);

    // ------------------------------------------------------------
    // Interruptible runs (Latest-wins, session scope)
    //
    // 中文说明：
    // - 新输入到来时，取消当前 active run（协作式），并立即开始新 run。
    // - 不做 hard kill；仅依赖 CancellationToken + 下游检查。
    // - 不在 cancel 时 Dispose old RunContext（避免 CTS 过早释放导致回调异常）。
    //   old run 在其 Task 结束时自行 Dispose。
    // ------------------------------------------------------------
    private readonly object _runGate = new();
    public RunContext? ActiveRun { get; private set; }

    public RunContext BeginNewRun(string runId, string? reason, out string? interruptedRunId)
    {
        runId = (runId ?? string.Empty).Trim();
        if (runId.Length == 0) throw new ArgumentException("runId is required", nameof(runId));

        RunContext? old;
        var next = new RunContext(scopeId: Id, runId: runId);

        lock (_runGate)
        {
            old = ActiveRun;
            ActiveRun = next;
        }

        if (old != null)
        {
            interruptedRunId = old.RunId;
            old.MarkSuperseded(runId, reason);
            old.Cancel();
        }
        else
        {
            interruptedRunId = null;
        }

        return next;
    }

    public void ClearRun(RunContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        lock (_runGate)
        {
            if (ReferenceEquals(ActiveRun, ctx))
            {
                ActiveRun = null;
            }
        }
    }

    public bool TryClearActiveRun(string expectedRunId, RunContext ctx)
    {
        expectedRunId = (expectedRunId ?? string.Empty).Trim();
        if (expectedRunId.Length == 0) return false;
        if (ctx == null) return false;

        lock (_runGate)
        {
            if (!ReferenceEquals(ActiveRun, ctx))
                return false;
            if (!string.Equals(ctx.RunId, expectedRunId, StringComparison.Ordinal))
                return false;
            ActiveRun = null;
            return true;
        }
    }

    // ------------------------------------------------------------
    // Message history for reconnect
    // ------------------------------------------------------------

    public IReadOnlyList<AgUiMessage> GetMessagesSnapshot(int maxMessages = 200)
    {
        maxMessages = Math.Clamp(maxMessages, 0, 2000);
        if (maxMessages == 0) return [];

        lock (_messagesLock)
        {
            if (_messages.Count <= maxMessages)
                return _messages.ToList();
            return _messages.Skip(Math.Max(0, _messages.Count - maxMessages)).ToList();
        }
    }

    public void SetMessage(string id, string role, string content)
    {
        id = (id ?? string.Empty).Trim();
        role = (role ?? string.Empty).Trim();
        content = (content ?? string.Empty).Replace("\r", "");

        if (id.Length == 0 || role.Length == 0)
            return;

        lock (_messagesLock)
        {
            if (_messageIndex.TryGetValue(id, out var idx))
            {
                _messages[idx] = new AgUiMessage
                {
                    Id = id,
                    Role = role,
                    Content = content
                };
                return;
            }

            _messageIndex[id] = _messages.Count;
            _messages.Add(new AgUiMessage
            {
                Id = id,
                Role = role,
                Content = content
            });
        }
    }

    public void AppendMessage(string id, string role, string delta)
    {
        id = (id ?? string.Empty).Trim();
        role = (role ?? string.Empty).Trim();
        delta = (delta ?? string.Empty).Replace("\r", "");

        if (id.Length == 0 || role.Length == 0 || delta.Length == 0)
            return;

        lock (_messagesLock)
        {
            if (_messageIndex.TryGetValue(id, out var idx))
            {
                var msg = _messages[idx];
                var updated = msg with { Content = (msg.Content ?? string.Empty) + delta };
                _messages[idx] = updated;
                return;
            }

            _messageIndex[id] = _messages.Count;
            _messages.Add(new AgUiMessage
            {
                Id = id,
                Role = role,
                Content = delta
            });
        }
    }

    // ------------------------------------------------------------
    //  WorkflowRunEventSink compatibility (naming)
    // ------------------------------------------------------------
    public void AppendToMessage(string messageId, string role, string delta)
        => AppendMessage(messageId, role, delta);

    // ------------------------------------------------------------
    //  Interruption context (best-effort; may be null)
    // ------------------------------------------------------------
    private readonly object _interruptLock = new();
    private InterruptionContext? _lastInterruption;

    public InterruptionContext? GetLastInterruption()
    {
        lock (_interruptLock)
        {
            return _lastInterruption;
        }
    }

    public InterruptionContext? ConsumeLastInterruption()
    {
        lock (_interruptLock)
        {
            var x = _lastInterruption;
            _lastInterruption = null;
            return x;
        }
    }

    public void SetLastInterruption(InterruptionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        lock (_interruptLock)
        {
            _lastInterruption = ctx;
        }
    }

    public void RecordInterruption(InterruptionContext ctx) => SetLastInterruption(ctx);

    public void RemoveMessage(string id)
    {
        id = (id ?? string.Empty).Trim();
        if (id.Length == 0)
            return;

        lock (_messagesLock)
        {
            if (!_messageIndex.TryGetValue(id, out var idx))
                return;

            _messages.RemoveAt(idx);
            _messageIndex.Remove(id);

            // Rebuild index to keep it consistent after removal.
            _messageIndex.Clear();
            for (var i = 0; i < _messages.Count; i++)
            {
                var mid = (_messages[i].Id ?? string.Empty).Trim();
                if (mid.Length > 0)
                    _messageIndex[mid] = i;
            }
        }
    }

    public void PublishEvent(AgUiEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        Events.Publish(evt);
    }

    public void PublishEvents(IEnumerable<AgUiEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var evt in events)
            PublishEvent(evt);
    }

    // ------------------------------------------------------------
    // Session records for persistence (optional)
    // ------------------------------------------------------------
    public VibeSessionRecord ToRecord()
    {
        return new VibeSessionRecord
        {
            SessionId = Id,
            ProviderName = ProviderName ?? string.Empty,
            DagId = DagId ?? string.Empty,
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(CreatedAt.UtcDateTime),
            UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
    }
}
