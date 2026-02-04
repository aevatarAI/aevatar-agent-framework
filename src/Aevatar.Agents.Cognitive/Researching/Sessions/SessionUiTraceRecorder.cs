using System.Text.Json;
using System.Collections.Concurrent;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Cognitive.Researching.Workspace;

namespace Aevatar.Agents.Cognitive.Researching.Sessions;

// ============================================================
//  SessionUiTraceRecorder
//
//  - Attaches to a ResearchSession's AG-UI event hub.
//  - Persists a "UI snapshot" (for refresh hydration) + per-run JSONL work trace.
//
//  Design taste:
//  - No special casing in frontend; refresh should rehydrate from file snapshots.
//  - Never block runs: all persistence is best-effort.
// ============================================================
public sealed class SessionUiTraceRecorder
{
    private const int MaxSnapshotMessages = 200;
    private const int MaxMetaItems = 400;
    private const int MaxTools = 400;
    private const int MaxStepMap = 200;

    private readonly SessionUiSnapshotStore _store;
    private readonly ILogger<SessionUiTraceRecorder> _logger;

    private readonly ConcurrentDictionary<string, byte> _attached = new(StringComparer.Ordinal);

    public SessionUiTraceRecorder(SessionUiSnapshotStore store, ILogger<SessionUiTraceRecorder> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Attach(ResearchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_attached.TryAdd(session.Id, 1))
            return;

        _ = Task.Run(() => RunAsync(session));
    }

    private async Task RunAsync(ResearchSession session)
    {
        var sessionId = session.Id;

        // In-memory projections (used to build ui_snapshot.json)
        var meta = new Dictionary<string, SessionUiSnapshotStore.UiMessageMeta>(StringComparer.Ordinal);
        var tools = new Dictionary<(string MessageId, string ToolCallId), SessionUiSnapshotStore.UiToolOutput>();
        var messageRoles = new Dictionary<string, string>(StringComparer.Ordinal);
        var runStepsOrder = new List<string>();
        var runStepsMap = new Dictionary<string, SessionUiSnapshotStore.UiRunStep>(StringComparer.Ordinal);

        var currentRunId = string.Empty;
        var version = 0;

        // Best-effort: restore prior snapshot into memory structures.
        try
        {
            var snap = await _store.LoadAsync(sessionId, CancellationToken.None);
            version = Math.Max(0, snap.Version);

            foreach (var m in snap.Messages)
                session.SetMessage(m.Id, m.Role, m.Content);

            foreach (var m in snap.MessageMeta)
            {
                if (string.IsNullOrWhiteSpace(m.MessageId)) continue;
                meta[m.MessageId.Trim()] = m;
            }

            foreach (var t in snap.Tools)
            {
                if (string.IsNullOrWhiteSpace(t.MessageId) || string.IsNullOrWhiteSpace(t.ToolCallId)) continue;
                tools[(t.MessageId.Trim(), t.ToolCallId.Trim())] = t;
            }

            if (snap.RunSteps != null)
            {
                currentRunId = (snap.RunSteps.RunId ?? string.Empty).Trim();
                runStepsOrder = snap.RunSteps.Order?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? [];
                runStepsMap = snap.RunSteps.Map ?? new Dictionary<string, SessionUiSnapshotStore.UiRunStep>(StringComparer.Ordinal);
            }
        }
        catch
        {
            // best-effort only
        }

        async Task FlushSnapshotAsync()
        {
            try
            {
                version++;

                var messages = session.GetMessagesSnapshot(maxMessages: MaxSnapshotMessages);
                var uiMessages = SessionUiSnapshotStore.FromAgUiMessages(messages, MaxSnapshotMessages);

                var metaList = meta.Values
                    .TakeLast(MaxMetaItems)
                    .ToList();

                var toolsList = tools.Values
                    .TakeLast(MaxTools)
                    .ToList();

                // Bound run steps
                if (runStepsOrder.Count > MaxStepMap)
                    runStepsOrder = runStepsOrder.TakeLast(MaxStepMap).ToList();

                if (runStepsMap.Count > MaxStepMap)
                {
                    // Keep only steps in order tail
                    var keep = new HashSet<string>(runStepsOrder, StringComparer.Ordinal);
                    runStepsMap = runStepsMap.Where(kv => keep.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
                }

                SessionUiSnapshotStore.UiRunStepsSnapshot? steps = null;
                if (runStepsOrder.Count > 0)
                {
                    steps = new SessionUiSnapshotStore.UiRunStepsSnapshot(currentRunId, runStepsOrder, runStepsMap);
                }

                var snap = new SessionUiSnapshotStore.UiSnapshot(
                    Version: version,
                    UpdatedAt: DateTimeOffset.UtcNow.ToString("O"),
                    Messages: uiMessages,
                    MessageMeta: metaList,
                    Tools: toolsList,
                    RunSteps: steps);

                await _store.SaveAsync(sessionId, snap, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to flush UI snapshot (best-effort).");
            }
        }

        static long NowMs(long? tsMs)
        {
            try
            {
                if (tsMs.HasValue)
                    return tsMs.Value;
            }
            catch
            {
                // ignore
            }
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        // Subscribe to session events
        await foreach (var evt in session.Events.SubscribeAsync())
        {
            try
            {
                switch (evt)
                {
                    case StepStartedEvent stepStarted:
                        {
                            var stepName = (stepStarted.StepName ?? string.Empty).Trim();
                            if (stepName.Length == 0) break;

                            if (!runStepsOrder.Contains(stepName))
                                runStepsOrder.Add(stepName);

                            runStepsMap[stepName] = new SessionUiSnapshotStore.UiRunStep("running", NowMs(stepStarted.Timestamp), null);

                            await FlushSnapshotAsync();
                            await _store.AppendRunEventAsync(sessionId, currentRunId, evt, CancellationToken.None);
                            break;
                        }

                    case StepFinishedEvent stepFinished:
                        {
                            var stepName = (stepFinished.StepName ?? string.Empty).Trim();
                            if (stepName.Length == 0) break;

                            if (!runStepsOrder.Contains(stepName))
                                runStepsOrder.Add(stepName);

                            // Preserve prior startedAt if available.
                            var startedAt = runStepsMap.TryGetValue(stepName, out var existing) ? existing.StartedAt : NowMs(null);
                            runStepsMap[stepName] = new SessionUiSnapshotStore.UiRunStep("done", startedAt, NowMs(stepFinished.Timestamp));

                            await FlushSnapshotAsync();
                            await _store.AppendRunEventAsync(sessionId, currentRunId, evt, CancellationToken.None);
                            break;
                        }

                    case TextMessageStartEvent msgStart:
                        {
                            var id = (msgStart.MessageId ?? string.Empty).Trim();
                            var role = (msgStart.Role ?? string.Empty).Trim();
                            if (id.Length == 0 || role.Length == 0) break;

                            session.SetMessage(id, role, "");
                            messageRoles[id] = role;

                            var metaItem = new SessionUiSnapshotStore.UiMessageMeta(
                                MessageId: id,
                                Agent: "",
                                StepName: "",
                                ProviderName: "");

                            meta[id] = metaItem;

                            await FlushSnapshotAsync();
                            await _store.AppendRunEventAsync(sessionId, currentRunId, evt, CancellationToken.None);
                            break;
                        }

                    case TextMessageContentEvent msgDelta:
                        {
                            var id = (msgDelta.MessageId ?? string.Empty).Trim();
                            if (id.Length == 0) break;
                            var role = messageRoles.TryGetValue(id, out var r) ? r : "assistant";
                            session.AppendMessage(id, role, msgDelta.Delta ?? "");
                            await FlushSnapshotAsync();
                            await _store.AppendRunEventAsync(sessionId, currentRunId, evt, CancellationToken.None);
                            break;
                        }

                    case TextMessageEndEvent msgEnd:
                        {
                            await FlushSnapshotAsync();
                            await _store.AppendRunEventAsync(sessionId, currentRunId, evt, CancellationToken.None);
                            break;
                        }

                    case ToolCallStartEvent toolStart:
                        {
                            var msgId = (toolStart.MessageId ?? string.Empty).Trim();
                            var callId = (toolStart.ToolCallId ?? string.Empty).Trim();
                            if (msgId.Length == 0 || callId.Length == 0) break;

                            tools[(msgId, callId)] = new SessionUiSnapshotStore.UiToolOutput(
                                MessageId: msgId,
                                ToolCallId: callId,
                                ToolName: toolStart.ToolName ?? "",
                                Status: "running",
                                ResultPreview: null,
                                Error: null,
                                StartedAt: NowMs(toolStart.Timestamp),
                                ProviderName: null,
                                TargetAgent: null);

                            await FlushSnapshotAsync();
                            await _store.AppendRunEventAsync(sessionId, currentRunId, evt, CancellationToken.None);
                            break;
                        }

                    case ToolCallEndEvent toolEnd:
                        {
                            var msgId = (toolEnd.MessageId ?? string.Empty).Trim();
                            var callId = (toolEnd.ToolCallId ?? string.Empty).Trim();
                            if (msgId.Length == 0 || callId.Length == 0) break;

                            // ToolCallEndEvent doesn't carry success/result in the minimal AG-UI subset.
                            // Keep any previously known toolName and mark "done".
                            SessionUiSnapshotStore.UiToolOutput? prev = null;
                            tools.TryGetValue((msgId, callId), out prev);
                            tools[(msgId, callId)] = new SessionUiSnapshotStore.UiToolOutput(
                                MessageId: msgId,
                                ToolCallId: callId,
                                ToolName: prev?.ToolName ?? "",
                                Status: "done",
                                ResultPreview: prev?.ResultPreview,
                                Error: prev?.Error,
                                StartedAt: prev?.StartedAt,
                                ProviderName: prev?.ProviderName,
                                TargetAgent: prev?.TargetAgent);

                            await FlushSnapshotAsync();
                            await _store.AppendRunEventAsync(sessionId, currentRunId, evt, CancellationToken.None);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to handle UI trace event (best-effort).");
            }
        }
    }
}
