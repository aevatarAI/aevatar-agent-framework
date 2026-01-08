using System.Text.Json;
using System.Collections.Concurrent;
using Aevatar.Agents.AGUI;

namespace ScientificResearchAssistant.Api.Sessions;

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
                if (!string.IsNullOrWhiteSpace(currentRunId) && runStepsOrder.Count > 0)
                    steps = new SessionUiSnapshotStore.UiRunStepsSnapshot(currentRunId, runStepsOrder, runStepsMap);

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
                _logger.LogDebug(ex, "[UiTrace] FlushSnapshot failed (best-effort).");
            }
        }

        void EnsureStepInOrder(string stepName)
        {
            if (runStepsMap.ContainsKey(stepName))
                return;
            runStepsOrder.Add(stepName);
        }

        try
        {
            await foreach (var evt in session.Events.SubscribeAsync(replay: false))
            {
                // Persist small "work trace" for audit (best-effort).
                switch (evt)
                {
                    case RunStartedEvent e:
                        currentRunId = (e.RunId ?? string.Empty).Trim();
                        runStepsOrder.Clear();
                        runStepsMap.Clear();
                        await _store.AppendRunEventAsync(sessionId, currentRunId, new { type = e.Type, ts = e.Timestamp, runId = currentRunId }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;

                    case RunFinishedEvent e:
                        await _store.AppendRunEventAsync(sessionId, currentRunId, new { type = e.Type, ts = e.Timestamp, runId = e.RunId }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;

                    case StepStartedEvent e:
                    {
                        var stepName = (e.StepName ?? string.Empty).Trim();
                        if (stepName.Length == 0) break;
                        EnsureStepInOrder(stepName);
                        runStepsMap[stepName] = runStepsMap.TryGetValue(stepName, out var prev)
                            ? prev with { Status = "running", StartedAt = e.Timestamp }
                            : new SessionUiSnapshotStore.UiRunStep("running", e.Timestamp, null);

                        // Also materialize as a stable system message for refresh (same id scheme as frontend).
                        var rid = string.IsNullOrWhiteSpace(currentRunId) ? "run" : currentRunId;
                        var mid = $"sys:{sessionId}:step:{rid}:{stepName}";
                        session.SetMessage(mid, role: "system", content: $"⏳ {stepName}");

                        await _store.AppendRunEventAsync(sessionId, rid, new { type = e.Type, ts = e.Timestamp, stepName }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;
                    }

                    case StepFinishedEvent e:
                    {
                        var stepName = (e.StepName ?? string.Empty).Trim();
                        if (stepName.Length == 0) break;
                        EnsureStepInOrder(stepName);
                        runStepsMap[stepName] = runStepsMap.TryGetValue(stepName, out var prev)
                            ? prev with { Status = "done", FinishedAt = e.Timestamp }
                            : new SessionUiSnapshotStore.UiRunStep("done", null, e.Timestamp);

                        var rid = string.IsNullOrWhiteSpace(currentRunId) ? "run" : currentRunId;
                        var mid = $"sys:{sessionId}:step:{rid}:{stepName}";
                        session.SetMessage(mid, role: "system", content: $"✅ {stepName}");

                        await _store.AppendRunEventAsync(sessionId, rid, new { type = e.Type, ts = e.Timestamp, stepName }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;
                    }

                    case CustomEvent e when string.Equals(e.Name, "aevatar.vibe.message_meta", StringComparison.Ordinal):
                    {
                        try
                        {
                            // Value is typically an anonymous object; parse via JSON for robustness.
                            var raw = e.Value == null ? "" : JsonSerializer.Serialize(e.Value);
                            if (string.IsNullOrWhiteSpace(raw)) break;

                            using var doc = JsonDocument.Parse(raw);
                            var root = doc.RootElement;

                            var messageId = root.TryGetProperty("messageId", out var midEl) ? (midEl.GetString() ?? "") : "";
                            var agent = root.TryGetProperty("agent", out var aEl) ? (aEl.GetString() ?? "") : "";
                            var stepName = root.TryGetProperty("stepName", out var sEl) ? (sEl.GetString() ?? "") : "";

                            messageId = messageId.Trim();
                            agent = agent.Trim();
                            stepName = stepName.Trim();
                            if (messageId.Length == 0) break;
                            meta[messageId] = new SessionUiSnapshotStore.UiMessageMeta(messageId, agent, stepName);
                            await FlushSnapshotAsync();
                        }
                        catch
                        {
                            // ignore
                        }
                        break;
                    }

                    case TextMessageEndEvent e:
                        await _store.AppendRunEventAsync(sessionId, currentRunId, new { type = e.Type, ts = e.Timestamp, messageId = e.MessageId }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;

                    case ToolCallStartEvent e:
                    {
                        var mid = (e.MessageId ?? string.Empty).Trim();
                        var tc = (e.ToolCallId ?? string.Empty).Trim();
                        if (mid.Length == 0 || tc.Length == 0) break;

                        tools[(mid, tc)] = new SessionUiSnapshotStore.UiToolOutput(
                            MessageId: mid,
                            ToolCallId: tc,
                            ToolName: (e.ToolName ?? string.Empty).Trim(),
                            Status: "running",
                            ResultPreview: null,
                            Error: null);

                        await _store.AppendRunEventAsync(sessionId, currentRunId, new { type = e.Type, ts = e.Timestamp, messageId = mid, toolCallId = tc, toolName = e.ToolName }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;
                    }

                    case ToolCallResultEvent e:
                    {
                        var mid = (e.MessageId ?? string.Empty).Trim();
                        var tc = (e.ToolCallId ?? string.Empty).Trim();
                        if (mid.Length == 0 || tc.Length == 0) break;

                        if (tools.TryGetValue((mid, tc), out var prev))
                        {
                            var rp = e.Result ?? "";
                            if (rp.Length > 20_000) rp = rp[..20_000];
                            tools[(mid, tc)] = prev with { ResultPreview = rp };
                        }

                        await _store.AppendRunEventAsync(sessionId, currentRunId, new { type = e.Type, ts = e.Timestamp, messageId = mid, toolCallId = tc }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;
                    }

                    case ToolCallEndEvent e:
                    {
                        var mid = (e.MessageId ?? string.Empty).Trim();
                        var tc = (e.ToolCallId ?? string.Empty).Trim();
                        if (mid.Length == 0 || tc.Length == 0) break;

                        if (tools.TryGetValue((mid, tc), out var prev))
                            tools[(mid, tc)] = prev with { Status = "done" };

                        await _store.AppendRunEventAsync(sessionId, currentRunId, new { type = e.Type, ts = e.Timestamp, messageId = mid, toolCallId = tc }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[UiTrace] Session event tap stopped (best-effort).");
        }
    }
}


