using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Aevatar.Agents;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.Abstractions.Tracing;
using Google.Protobuf.WellKnownTypes;
using VibeResearching.Api.Vibe;
using VibeResearching.Api.Vibe.Dag;

namespace VibeResearching.Api.Sessions;

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
    private readonly ResearchRuntime _runtime;
    private readonly DagStore _dag;

    private readonly ConcurrentDictionary<string, byte> _attached = new(StringComparer.Ordinal);

    public SessionUiTraceRecorder(
        SessionUiSnapshotStore store,
        ResearchRuntime runtime,
        DagStore dag,
        ILogger<SessionUiTraceRecorder> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
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

        var dagCandidates = new Dictionary<string, string>(StringComparer.Ordinal);
        var dagConsensus = new Dictionary<string, bool>(StringComparer.Ordinal);
        var dagApplied = new HashSet<string>(StringComparer.Ordinal);
        var rawTraceLogCount = 0;

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
            await foreach (var evt in session.Events.SubscribeAsync(CancellationToken.None))
            {
                if (evt.RawEvent is ExecutionTraceEvent raw)
                {
                    if (rawTraceLogCount < 3)
                    {
                        rawTraceLogCount++;
                        var nodeId = (raw.NodeId ?? string.Empty).Trim();
                        var status = ReadStringField(raw, ExecutionTraceEventFields.Status) ?? string.Empty;
                        var stepType = ReadStringField(raw, ExecutionTraceEventFields.StepType) ?? string.Empty;
                        var assistantLen = ReadStringField(raw, ExecutionTraceEventFields.AssistantResponse)?.Length ?? 0;
                        // #region agent log
                        File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                            JsonSerializer.Serialize(new
                            {
                                sessionId,
                                runId = ReadStringField(raw, ExecutionTraceEventFields.ExecutionId) ?? string.Empty,
                                hypothesisId = "H24",
                                location = "SessionUiTraceRecorder.cs:RunAsync",
                                message = "trace_raw_seen",
                                data = new
                                {
                                    nodeId,
                                    status,
                                    stepType,
                                    assistantLen,
                                    isDagStep = nodeId is "dag_builder" or "maker_consensus_parse" or "verifier_quorum"
                                },
                                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            }) + Environment.NewLine);
                        // #endregion
                    }

                    var dagNodeId = (raw.NodeId ?? string.Empty).Trim();
                    if (dagNodeId is "dag_builder" or "maker_consensus_parse" or "verifier_quorum")
                    {
                        // #region agent log
                        System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                            JsonSerializer.Serialize(new
                            {
                                sessionId,
                                runId = ReadStringField(raw, ExecutionTraceEventFields.ExecutionId) ?? string.Empty,
                                hypothesisId = "H25",
                                location = "SessionUiTraceRecorder.cs:RunAsync",
                                message = "trace_dag_step_seen",
                                data = new
                                {
                                    nodeId = dagNodeId,
                                    status = ReadStringField(raw, ExecutionTraceEventFields.Status) ?? string.Empty,
                                    assistantLen = ReadStringField(raw, ExecutionTraceEventFields.AssistantResponse)?.Length ?? 0
                                },
                                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            }) + Environment.NewLine);
                        // #endregion
                    }
                }

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

                    case RunErrorEvent e when IsCancelMessage(e.Message):
                    {
                        var runId = string.IsNullOrWhiteSpace(currentRunId) ? "run" : currentRunId;
                        session.Events.Publish(new CustomEvent
                        {
                            Timestamp = e.Timestamp,
                            Name = "aevatar.scientific.run_canceled",
                            Value = new { threadId = sessionId, runId }
                        });
                        await _store.AppendRunEventAsync(sessionId, runId, new { type = e.Type, ts = e.Timestamp, runId, error = e.Message }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;
                    }

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
                            var metaJson = e.Value == null ? "" : JsonSerializer.Serialize(e.Value);
                            if (string.IsNullOrWhiteSpace(metaJson)) break;

                            using var doc = JsonDocument.Parse(metaJson);
                            var root = doc.RootElement;

                            var messageId = root.TryGetProperty("messageId", out var midEl) ? (midEl.GetString() ?? "") : "";
                            var agent = root.TryGetProperty("agent", out var aEl) ? (aEl.GetString() ?? "") : "";
                            var stepName = root.TryGetProperty("stepName", out var sEl) ? (sEl.GetString() ?? "") : "";
                            var providerName = root.TryGetProperty("providerName", out var pEl) ? (pEl.GetString() ?? "") : "";

                            messageId = messageId.Trim();
                            agent = agent.Trim();
                            stepName = stepName.Trim();
                            providerName = providerName.Trim();
                            if (messageId.Length == 0) break;
                            meta[messageId] = new SessionUiSnapshotStore.UiMessageMeta(messageId, agent, stepName, providerName);
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
                            Error: null,
                            StartedAt: e.Timestamp);

                        await PublishToolStartAsync(session, currentRunId, e, tc, CancellationToken.None);
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

                        SessionUiSnapshotStore.UiToolOutput? prev = null;
                        if (tools.TryGetValue((mid, tc), out var stored))
                        {
                            prev = stored;
                            tools[(mid, tc)] = stored with { Status = "done" };
                        }

                        await PublishToolEndAsync(session, currentRunId, e, tc, prev, CancellationToken.None);
                        await _store.AppendRunEventAsync(sessionId, currentRunId, new { type = e.Type, ts = e.Timestamp, messageId = mid, toolCallId = tc }, CancellationToken.None);
                        await FlushSnapshotAsync();
                        break;
                    }

                    case { RawEvent: ExecutionTraceEvent trace }:
                    {
                        var status = ReadStringField(trace, ExecutionTraceEventFields.Status) ?? string.Empty;
                        if (!string.Equals(status, ExecutionTraceEventStatus.Completed, StringComparison.OrdinalIgnoreCase))
                            break;

                        var runId = ReadStringField(trace, ExecutionTraceEventFields.ExecutionId) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(runId))
                            break;

                        var stepId = (trace.NodeId ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(stepId))
                            break;

                        if (string.Equals(stepId, "dag_builder", StringComparison.OrdinalIgnoreCase))
                        {
                            var assistantRaw = ReadStringField(trace, ExecutionTraceEventFields.AssistantResponse) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(assistantRaw))
                            {
                                dagCandidates[runId] = assistantRaw;
                                // #region agent log
                                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                    JsonSerializer.Serialize(new
                                    {
                                        sessionId,
                                        runId,
                                        hypothesisId = "H14",
                                        location = "SessionUiTraceRecorder.cs:RunAsync",
                                        message = "dag_candidate_captured",
                                        data = new { stepId, length = assistantRaw.Length },
                                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                    }) + Environment.NewLine);
                                // #endregion
                            }
                        }

                        if (string.Equals(stepId, "maker_consensus_parse", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(stepId, "verifier_quorum", StringComparison.OrdinalIgnoreCase))
                        {
                            var assistantRaw = ReadStringField(trace, ExecutionTraceEventFields.AssistantResponse) ?? string.Empty;
                            if (TryParseDagConsensus(assistantRaw, out var accept, out var redFlagsCount))
                            {
                                dagConsensus[runId] = accept;
                                // #region agent log
                                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                    JsonSerializer.Serialize(new
                                    {
                                        sessionId,
                                        runId,
                                        hypothesisId = "H15",
                                        location = "SessionUiTraceRecorder.cs:RunAsync",
                                        message = "dag_consensus_captured",
                                        data = new { stepId, accept, redFlagsCount },
                                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                    }) + Environment.NewLine);
                                // #endregion
                            }
                        }

                        if (!dagApplied.Contains(runId) &&
                            dagConsensus.TryGetValue(runId, out var accepted) &&
                            accepted &&
                            dagCandidates.TryGetValue(runId, out var candidateRaw))
                        {
                            var dagId = session.EffectiveDagId;
                            // #region agent log
                            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                JsonSerializer.Serialize(new
                                {
                                    sessionId,
                                    runId,
                                    hypothesisId = "H16",
                                    location = "SessionUiTraceRecorder.cs:RunAsync",
                                    message = "dag_apply_attempt",
                                    data = new { dagId, candidateLength = candidateRaw.Length, accepted },
                                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                }) + Environment.NewLine);
                            // #endregion

                            try
                            {
                                var currentDag = await _dag.LoadSnapshotAsync(dagId, CancellationToken.None);
                                var mutation = VibeWorkflowParsing.TryParseDagBuilderCandidate(
                                    session.Id,
                                    candidateRaw,
                                    currentDag);
                                if (mutation == null)
                                {
                                    // #region agent log
                                    System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                        JsonSerializer.Serialize(new
                                        {
                                            sessionId,
                                            runId,
                                            hypothesisId = "H17",
                                            location = "SessionUiTraceRecorder.cs:RunAsync",
                                            message = "dag_apply_skipped",
                                            data = new { reason = "candidate_parse_failed" },
                                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                        }) + Environment.NewLine);
                                    // #endregion
                                    break;
                                }

                                if (mutation.UpsertNodes.Count == 0 && mutation.UpsertEdges.Count == 0)
                                {
                                    dagApplied.Add(runId);
                                    // #region agent log
                                    System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                        JsonSerializer.Serialize(new
                                        {
                                            sessionId,
                                            runId,
                                            hypothesisId = "H17",
                                            location = "SessionUiTraceRecorder.cs:RunAsync",
                                            message = "dag_apply_skipped",
                                            data = new { reason = "no_changes" },
                                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                        }) + Environment.NewLine);
                                    // #endregion
                                    break;
                                }

                                var applied = await _dag.ApplyMutationAsync(dagId, mutation, CancellationToken.None);
                                dagApplied.Add(runId);

                                session.Events.Publish(new CustomEvent
                                {
                                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                                    Name = "aevatar.vibe.dag_updated",
                                    Value = new
                                    {
                                        sessionId = session.Id,
                                        dagId,
                                        runId,
                                        mutationId = mutation.MutationId,
                                        nodes = mutation.UpsertNodes.Count,
                                        edges = mutation.UpsertEdges.Count,
                                        consensusWorkflow = "workflow",
                                        consensusArtifact = string.Empty,
                                        updatedAt = applied.UpdatedAt?.ToDateTime().ToUniversalTime().ToString("O") ?? ""
                                    }
                                });

                                // #region agent log
                                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                    JsonSerializer.Serialize(new
                                    {
                                        sessionId,
                                        runId,
                                        hypothesisId = "H62",
                                        location = "SessionUiTraceRecorder.cs:RunAsync",
                                        message = "dag_updated_published",
                                        data = new
                                        {
                                            streamHash = RuntimeHelpers.GetHashCode(session.Events),
                                            dagId,
                                            nodes = mutation.UpsertNodes.Count,
                                            edges = mutation.UpsertEdges.Count
                                        },
                                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                    }) + Environment.NewLine);
                                // #endregion

                                // #region agent log
                                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                    JsonSerializer.Serialize(new
                                    {
                                        sessionId,
                                        runId,
                                        hypothesisId = "H17",
                                        location = "SessionUiTraceRecorder.cs:RunAsync",
                                        message = "dag_apply_succeeded",
                                        data = new { nodes = mutation.UpsertNodes.Count, edges = mutation.UpsertEdges.Count },
                                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                    }) + Environment.NewLine);
                                // #endregion
                            }
                            catch (Exception ex)
                            {
                                // #region agent log
                                System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                                    JsonSerializer.Serialize(new
                                    {
                                        sessionId,
                                        runId,
                                        hypothesisId = "H17",
                                        location = "SessionUiTraceRecorder.cs:RunAsync",
                                        message = "dag_apply_failed",
                                        data = new { error = ex.Message },
                                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                                    }) + Environment.NewLine);
                                // #endregion
                            }
                        }
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

    private async Task PublishToolStartAsync(
        ResearchSession session,
        string currentRunId,
        ToolCallStartEvent evt,
        string toolCallId,
        CancellationToken ct)
    {
        var toolName = (evt.ToolName ?? string.Empty).Trim();
        if (toolName.Length == 0)
            return;

        var runId = ResolveRunId(currentRunId, evt.MessageId);
        var isMcp = await _runtime.IsMcpToolAsync(session.Id, toolName, ct);

        session.Events.Publish(new CustomEvent
        {
            Timestamp = evt.Timestamp,
            Name = "aevatar.scientific.tool_start",
            Value = new
            {
                threadId = session.Id,
                runId,
                toolCallId,
                toolName,
                isMcp
            }
        });
    }

    private async Task PublishToolEndAsync(
        ResearchSession session,
        string currentRunId,
        ToolCallEndEvent evt,
        string toolCallId,
        SessionUiSnapshotStore.UiToolOutput? previous,
        CancellationToken ct)
    {
        var toolName = previous?.ToolName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(toolName))
            toolName = ReadStringField(evt.RawEvent as ExecutionTraceEvent, ExecutionTraceEventFields.ToolName) ?? string.Empty;
        toolName = toolName.Trim();
        if (toolName.Length == 0)
            return;

        var runId = ResolveRunId(currentRunId, evt.MessageId);
        var raw = evt.RawEvent as ExecutionTraceEvent;
        var status = ReadStringField(raw, ExecutionTraceEventFields.Status);
        var durationMs = ReadLongField(raw, ExecutionTraceEventFields.DurationMs);
        var error = ReadStringField(raw, ExecutionTraceEventFields.Error);
        var success = !IsFailureStatus(status);
        var isMcp = await _runtime.IsMcpToolAsync(session.Id, toolName, ct);

        session.Events.Publish(new CustomEvent
        {
            Timestamp = evt.Timestamp,
            Name = "aevatar.scientific.tool_end",
            Value = new
            {
                threadId = session.Id,
                runId,
                toolCallId,
                toolName,
                isMcp,
                success,
                durationMs,
                error,
                resultPreview = previous?.ResultPreview ?? string.Empty
            }
        });
    }

    private static string ResolveRunId(string currentRunId, string? messageId)
    {
        if (!string.IsNullOrWhiteSpace(currentRunId))
            return currentRunId;

        var mid = (messageId ?? string.Empty).Trim();
        if (mid.Length == 0)
            return string.Empty;

        // Expected: msg:{sessionId}:assistant:{runId}
        var parts = mid.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 4)
            return parts[^1];

        return string.Empty;
    }

    private static bool IsCancelMessage(string? message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
            return false;
        return text.Contains("cancel", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadStringField(ExecutionTraceEvent? evt, string key)
    {
        if (evt?.Fields == null) return null;
        if (!evt.Fields.TryGetValue(key, out var value)) return null;
        return value.StringValue;
    }

    private static long? ReadLongField(ExecutionTraceEvent? evt, string key)
    {
        if (evt?.Fields == null) return null;
        if (!evt.Fields.TryGetValue(key, out var value)) return null;
        return value.ValueCase switch
        {
            ContextValue.ValueOneofCase.IntValue => value.IntValue,
            ContextValue.ValueOneofCase.DoubleValue => (long)value.DoubleValue,
            _ => null
        };
    }

    private static bool IsFailureStatus(string? status)
    {
        return string.Equals(status, ExecutionTraceEventStatus.Failed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(status, ExecutionTraceEventStatus.Cancelled, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseDagConsensus(string raw, out bool accept, out int redFlagsCount)
    {
        accept = false;
        redFlagsCount = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (!TryParseJson(raw, out var root))
            return false;

        if (root.TryGetProperty("accept", out var acceptEl))
        {
            accept = acceptEl.ValueKind == JsonValueKind.True ||
                     (acceptEl.ValueKind == JsonValueKind.String &&
                      string.Equals(acceptEl.GetString(), "true", StringComparison.OrdinalIgnoreCase));
        }

        if (root.TryGetProperty("red_flags", out var redFlagsEl) && redFlagsEl.ValueKind == JsonValueKind.Array)
            redFlagsCount = redFlagsEl.GetArrayLength();
        else if (root.TryGetProperty("redFlags", out var redFlagsCamel) && redFlagsCamel.ValueKind == JsonValueKind.Array)
            redFlagsCount = redFlagsCamel.GetArrayLength();

        return true;
    }

    private static bool TryParseJson(string raw, out JsonElement root)
    {
        root = default;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start) return false;

            try
            {
                using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
                root = doc.RootElement.Clone();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}


