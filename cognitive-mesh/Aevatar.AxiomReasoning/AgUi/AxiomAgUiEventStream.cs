using System.Runtime.CompilerServices;
using System.Text;
using Aevatar.Agents.AGUI;
using Aevatar.AxiomReasoning.Models;

namespace Aevatar.AxiomReasoning.AgUi;

// ============================================================
//  AXIOM → AG-UI EVENT STREAM
//
//  设计原则：
//  - 不动 CognitiveStrategy / 工作流：只在边界层做“协议投影”
//  - 既提供标准 AG-UI 事件（RUN/STEP/TEXT/STATE），也保留 CUSTOM 事件让现有 UI 继续“丝滑”
//  - 所有输出 best-effort：绝不因映射错误影响主流程
// ============================================================

public static class AxiomAgUiEventStream
{
    public static async IAsyncEnumerable<AgUiEvent> BuildAsync(
        AxiomSession session,
        IAsyncEnumerable<AxiomEvent> source,
        List<AgUiMessage>? initialMessages = null,
        GraphEvent? initialGraph = null,
        IReadOnlyList<AgUiEvent>? initialExtraEvents = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var threadId = session.Id;
        var runId = session.Id; // NOTE: 当前 session 默认只有一次 run；未来可扩展为 {sessionId}:{runSeq}

        var runStartedSent = false;

        // Track step status transitions to avoid spamming STEP_* events.
        var stepLastStatus = new Dictionary<string, string>(StringComparer.Ordinal);

        // Dedup legacy CUSTOM progress spam (especially fan_out coordinator ticks).
        var progressLastSignature = new Dictionary<string, string>(StringComparer.Ordinal);

        // Track open streaming messages.
        var openMessages = new HashSet<string>(StringComparer.Ordinal);
        var messageSentLen = new Dictionary<string, int>(StringComparer.Ordinal);
        var messageDeltaBuffer = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);

        // Track last emitted graph snapshot for STATE_DELTA.
        GraphEvent? lastGraph = initialGraph;

        long Ts(DateTimeOffset? ts) => (ts ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();

        // 1) Initial snapshots (useful for reconnect / generic AG-UI clients)
        var messages = initialMessages;
        if (messages is null || messages.Count == 0)
        {
            messages = new List<AgUiMessage>(capacity: 1)
            {
                new()
                {
                    Id = $"msg:{threadId}:user:input",
                    Role = "user",
                    Content = BuildUserInputMessage(session)
                }
            };
        }

        yield return new MessagesSnapshotEvent
        {
            Timestamp = Ts(DateTimeOffset.UtcNow),
            Messages = messages
        };

        if (initialExtraEvents is { Count: > 0 })
        {
            foreach (var e in initialExtraEvents)
            {
                if (e == null) continue;
                yield return e;
            }
        }

        yield return new CustomEvent
        {
            Timestamp = Ts(DateTimeOffset.UtcNow),
            Name = "aevatar.axiom.status_snapshot",
            Value = new
            {
                sessionId = session.Id,
                status = session.Status.ToString(),
                phase = session.CurrentPhase,
                progressPercent = session.ProgressPercent,
                totalLlmCalls = session.TotalLlmCalls,
                totalTokens = session.TotalTokens
            }
        };

        if (initialGraph is not null)
        {
            yield return new StateSnapshotEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                Snapshot = new
                {
                    kind = "aevatar.axiom.graph",
                    sessionId = session.Id,
                    graph = initialGraph
                }
            };
        }

        yield return new CustomEvent
        {
            Timestamp = Ts(DateTimeOffset.UtcNow),
            Name = "aevatar.axiom.session",
            Value = new
            {
                sessionId = session.Id,
                status = session.Status.ToString(),
                workflow = session.Workflow,
                language = session.Language,
                k = session.K,
                maxRounds = session.MaxRounds,
                maxDepth = session.MaxDepth,
                continueOnFailure = session.ContinueOnFailure,
                budgets = new
                {
                    maxDurationMinutes = session.MaxDurationMinutes,
                    maxLlmCalls = session.MaxLlmCallsBudget,
                    maxTokens = session.MaxTokensBudget
                },
                hpa = new
                {
                    enabled = session.HpaEnabled,
                    alpha = session.HpaAlpha,
                    seedPhase = session.HpaSeedPhase,
                    betaModel = session.HpaBetaModel,
                    beta0 = session.HpaBeta0,
                    beta1 = session.HpaBeta1,
                    seed = session.HpaSeed,
                    radialWBase = session.HpaRadialWBase,
                    radialWScale = session.HpaRadialWScale,
                    gates = new
                    {
                        minCoherence = session.MinCoherence,
                        maxGapNorm = session.MaxGapNorm,
                        maxAssociatorMean = session.MaxAssociatorMean
                    }
                }
            }
        };

        if (session.Status == AxiomSessionStatus.Running)
        {
            yield return new RunStartedEvent
            {
                Timestamp = Ts(DateTimeOffset.UtcNow),
                ThreadId = threadId,
                RunId = runId
            };
            runStartedSent = true;
        }

        // 2) Live stream
        var outEvents = new List<AgUiEvent>(capacity: 12);
        await foreach (var evt in source.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();

            var ts = Ts(evt.Timestamp);

            outEvents.Clear();
            try
            {
                MapOne(
                    session,
                    evt,
                    ts,
                    threadId,
                    runId,
                    ref runStartedSent,
                    stepLastStatus,
                    progressLastSignature,
                    openMessages,
                    messageSentLen,
                    messageDeltaBuffer,
                    ref lastGraph,
                    outEvents);
            }
            catch
            {
                // best-effort: ignore bad event and keep stream alive
            }

            for (var i = 0; i < outEvents.Count; i++)
                yield return outEvents[i];
        }
    }

    private static void MapOne(
        AxiomSession session,
        AxiomEvent evt,
        long ts,
        string threadId,
        string runId,
        ref bool runStartedSent,
        Dictionary<string, string> stepLastStatus,
        Dictionary<string, string> progressLastSignature,
        HashSet<string> openMessages,
        Dictionary<string, int> messageSentLen,
        Dictionary<string, StringBuilder> messageDeltaBuffer,
        ref GraphEvent? lastGraph,
        List<AgUiEvent> output)
    {
        // RunStarted should be emitted before any step/message event.
        if (!runStartedSent && session.Status == AxiomSessionStatus.Running)
        {
            output.Add(new RunStartedEvent
            {
                Timestamp = ts,
                ThreadId = threadId,
                RunId = runId
            });
            runStartedSent = true;
        }

        switch (evt)
        {
            case ProgressEvent p:
            {
                // STEP_* (dedup by status transition)
                var stepName = NormalizeStepName(p.StepId, p.Phase, p.StepType);
                var progressKey = $"{(string.IsNullOrWhiteSpace(p.WorkerId) ? "coordinator" : p.WorkerId.Trim())}|{stepName}";

                MaybeEmitStepLifecycle(stepName, p.StepStatus, stepLastStatus, out var stepStarted, out var stepFinished);
                if (stepStarted)
                {
                    output.Add(new StepStartedEvent
                    {
                        Timestamp = ts,
                        StepName = stepName
                    });
                    // New step => allow the first progress tick through.
                    progressLastSignature.Remove(progressKey);
                }

                if (stepFinished)
                {
                    output.Add(new StepFinishedEvent
                    {
                        Timestamp = ts,
                        StepName = stepName
                    });
                    progressLastSignature.Remove(progressKey);
                }

                // Keep existing UI stable: wrap legacy event as CUSTOM so frontend can reuse logic.
                //
                // Perf:
                // - Token streaming 下 ProgressEvent 是“每 token 一条”，体量大、频率高。
                // - AG-UI 已经有 TEXT_MESSAGE_CONTENT(delta) 表达 streaming。
                // - 因此：对 llm_call 的 streaming token 事件，跳过 progress 转发，避免重复刷两条 SSE。
                var isStreamingToken =
                    string.Equals(p.StepType, "llm_call", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(p.TokenDelta) &&
                    !IsFinished(p.StepStatus);

                if (!isStreamingToken)
                {
                    var emitLegacyProgress = true;

                    // Only dedup "lightweight running ticks" (fan_out can be very noisy).
                    var hasBody = !string.IsNullOrEmpty(p.AssistantResponse) || !string.IsNullOrEmpty(p.AssistantResponsePreview);
                    var isLightTick = IsRunning(p.StepStatus) && !hasBody && string.IsNullOrEmpty(p.TokenDelta);
                    if (isLightTick)
                    {
                        var sig = $"{p.Phase}|{p.ProgressPercent}|{p.ParallelCompleted}|{p.ParallelFailed}|{p.TotalLlmCalls}|{p.TotalTokens}|{p.Message}";
                        if (progressLastSignature.TryGetValue(progressKey, out var last) &&
                            string.Equals(last, sig, StringComparison.Ordinal))
                        {
                            emitLegacyProgress = false;
                        }
                        else
                        {
                            progressLastSignature[progressKey] = sig;
                        }
                    }

                    if (emitLegacyProgress)
                    {
                        output.Add(new CustomEvent
                        {
                            Timestamp = ts,
                            Name = "aevatar.axiom.progress",
                            Value = p
                        });
                    }
                }

                // TEXT_MESSAGE_* for llm_call streaming
                if (string.Equals(p.StepType, "llm_call", StringComparison.OrdinalIgnoreCase))
                {
                    var workerId = string.IsNullOrWhiteSpace(p.WorkerId) ? "coordinator" : p.WorkerId.Trim();
                    var messageId = $"msg:{threadId}:{workerId}:{stepName}";

                    // Ensure message start once we have any meaningful content.
                    if (!openMessages.Contains(messageId) && HasAnyMessageSignal(p))
                    {
                        openMessages.Add(messageId);
                        messageSentLen[messageId] = 0;

                        output.Add(new TextMessageStartEvent
                        {
                            Timestamp = ts,
                            MessageId = messageId,
                            Role = "assistant"
                        });

                        // Attach metadata for rich UI (worker/provider/prompts/etc).
                        output.Add(new CustomEvent
                        {
                            Timestamp = ts,
                            Name = "aevatar.axiom.message_meta",
                            Value = new
                            {
                                messageId,
                                workerId,
                                stepId = p.StepId,
                                stepType = p.StepType,
                                phase = p.Phase,
                                providerName = p.ProviderName,
                                tokenIndex = p.TokenIndex,
                                systemPrompt = p.SystemPrompt,
                                userPrompt = p.UserPrompt
                            }
                        });
                    }

                    var isFinished = IsFinished(p.StepStatus);

                    // Streaming delta (hot path)
                    var delta = p.TokenDelta;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        // If we didn't see a start signal earlier (rare), start now.
                        if (!openMessages.Contains(messageId))
                        {
                            openMessages.Add(messageId);
                            messageSentLen[messageId] = 0;

                            output.Add(new TextMessageStartEvent
                            {
                                Timestamp = ts,
                                MessageId = messageId,
                                Role = "assistant"
                            });
                        }

                        BufferOrEmitDelta(messageId, delta, ts, messageDeltaBuffer, messageSentLen, output);
                    }

                    // Completion fallback:
                    // - Flush buffered deltas
                    // - Emit remaining tail (if any) from the completed body
                    if (isFinished)
                    {
                        FlushBufferedDelta(messageId, ts, messageDeltaBuffer, messageSentLen, output);

                        var completion = ComputeCompletionDelta(messageId, p, messageSentLen);
                        if (!string.IsNullOrEmpty(completion))
                        {
                            if (!openMessages.Contains(messageId))
                            {
                                openMessages.Add(messageId);
                                messageSentLen[messageId] = 0;

                                output.Add(new TextMessageStartEvent
                                {
                                    Timestamp = ts,
                                    MessageId = messageId,
                                    Role = "assistant"
                                });
                            }

                            output.Add(new TextMessageContentEvent
                            {
                                Timestamp = ts,
                                MessageId = messageId,
                                Delta = completion
                            });

                            messageSentLen[messageId] = messageSentLen.GetValueOrDefault(messageId, 0) + completion.Length;
                        }
                    }

                    if (openMessages.Contains(messageId) && isFinished)
                    {
                        FlushBufferedDelta(messageId, ts, messageDeltaBuffer, messageSentLen, output);
                        output.Add(new TextMessageEndEvent
                        {
                            Timestamp = ts,
                            MessageId = messageId
                        });

                        openMessages.Remove(messageId);
                        messageSentLen.Remove(messageId);
                        messageDeltaBuffer.Remove(messageId);
                    }
                }

                return;
            }

            case GraphEvent g:
            {
                // AG-UI State:
                // - First graph: emit STATE_SNAPSHOT
                // - Subsequent graphs: emit STATE_DELTA (RFC6902), fallback to snapshot if needed
                if (lastGraph is null)
                {
                    output.Add(new StateSnapshotEvent
                    {
                        Timestamp = ts,
                        Snapshot = new
                        {
                            kind = "aevatar.axiom.graph",
                            sessionId = session.Id,
                            graph = g
                        }
                    });
                }
                else
                {
                    var delta = BuildGraphJsonPatch(lastGraph, g);
                    if (delta.Length == 0)
                    {
                        // no-op
                    }
                    else
                    {
                        output.Add(new StateDeltaEvent
                        {
                            Timestamp = ts,
                            Delta = delta
                        });
                    }
                }

                lastGraph = g;
                return;
            }

            case ResultEvent r:
            {
                output.Add(new CustomEvent
                {
                    Timestamp = ts,
                    Name = "aevatar.axiom.result",
                    Value = r
                });

                if (!runStartedSent)
                {
                    output.Add(new RunStartedEvent
                    {
                        Timestamp = ts,
                        ThreadId = threadId,
                        RunId = runId
                    });
                    runStartedSent = true;
                }

                output.Add(new RunFinishedEvent
                {
                    Timestamp = ts,
                    ThreadId = threadId,
                    RunId = runId,
                    Result = new
                    {
                        success = r.Success,
                        content = r.Content,
                        error = r.Error,
                        totalLlmCalls = r.TotalLlmCalls,
                        totalTokens = r.TotalTokens
                    }
                });

                // Close any dangling message streams (best-effort).
                foreach (var mid in openMessages.ToArray())
                {
                    FlushBufferedDelta(mid, ts, messageDeltaBuffer, messageSentLen, output);
                    output.Add(new TextMessageEndEvent { Timestamp = ts, MessageId = mid });
                }
                openMessages.Clear();
                messageSentLen.Clear();
                messageDeltaBuffer.Clear();

                return;
            }

            case ErrorEvent e:
            {
                output.Add(new CustomEvent
                {
                    Timestamp = ts,
                    Name = "aevatar.axiom.error",
                    Value = e
                });

                output.Add(new RunErrorEvent
                {
                    Timestamp = ts,
                    Message = e.Message,
                    Code = "AXIOM_SESSION_ERROR"
                });

                // Close any dangling message streams (best-effort).
                foreach (var mid in openMessages.ToArray())
                {
                    FlushBufferedDelta(mid, ts, messageDeltaBuffer, messageSentLen, output);
                    output.Add(new TextMessageEndEvent { Timestamp = ts, MessageId = mid });
                }
                openMessages.Clear();
                messageSentLen.Clear();
                messageDeltaBuffer.Clear();

                return;
            }

            default:
            {
                output.Add(new CustomEvent
                {
                    Timestamp = ts,
                    Name = "aevatar.axiom.unknown",
                    Value = evt
                });

                return;
            }
        }
    }

    private static string BuildUserInputMessage(AxiomSession session)
    {
        var axioms = string.IsNullOrWhiteSpace(session.AxiomsText) ? "(empty)" : session.AxiomsText.Trim();
        var goal = string.IsNullOrWhiteSpace(session.Goal) ? "(none)" : session.Goal.Trim();

        return $"""
               AXIOMS:
               {axioms}

               GOAL / FOCUS:
               {goal}
               """;
    }

    private static string NormalizeStepName(string? stepId, string? phase, string? stepType)
    {
        if (!string.IsNullOrWhiteSpace(stepId)) return stepId.Trim();
        if (!string.IsNullOrWhiteSpace(phase)) return phase.Trim();
        if (!string.IsNullOrWhiteSpace(stepType)) return stepType.Trim();
        return "main";
    }

    private static void MaybeEmitStepLifecycle(
        string stepName,
        string? stepStatus,
        Dictionary<string, string> stepLastStatus,
        out bool stepStarted,
        out bool stepFinished)
    {
        stepStarted = false;
        stepFinished = false;

        if (string.IsNullOrWhiteSpace(stepName)) return;
        if (string.IsNullOrWhiteSpace(stepStatus)) return;

        var cur = stepStatus.Trim();
        var lower = cur.ToLowerInvariant();
        var isRunning = lower.Contains("running");
        var isFinished = lower.Contains("completed") || lower.Contains("failed") || lower.Contains("skipped");

        stepLastStatus.TryGetValue(stepName, out var prev);
        var prevLower = (prev ?? "").ToLowerInvariant();

        if (isRunning && !prevLower.Contains("running"))
            stepStarted = true;

        if (isFinished && !(prevLower.Contains("completed") || prevLower.Contains("failed") || prevLower.Contains("skipped")))
            stepFinished = true;

        stepLastStatus[stepName] = cur;
    }

    // Small streaming coalescing:
    // - Reduces SSE event count (token-per-event is expensive)
    // - Keeps latency low by flushing frequently enough
    private const int StreamingDeltaFlushChars = 96;

    private static void BufferOrEmitDelta(
        string messageId,
        string delta,
        long ts,
        Dictionary<string, StringBuilder> messageDeltaBuffer,
        Dictionary<string, int> messageSentLen,
        List<AgUiEvent> output)
    {
        if (string.IsNullOrEmpty(delta)) return;

        if (!messageDeltaBuffer.TryGetValue(messageId, out var sb))
        {
            sb = new StringBuilder(capacity: Math.Max(StreamingDeltaFlushChars, delta.Length));
            messageDeltaBuffer[messageId] = sb;
        }

        sb.Append(delta);

        // Flush policy:
        // - Always flush the very first chunk to minimize "time-to-first-token"
        // - Afterwards, flush when buffer exceeds a small threshold
        var sent = messageSentLen.GetValueOrDefault(messageId, 0);
        if (sent == 0 || sb.Length >= StreamingDeltaFlushChars)
            FlushBufferedDelta(messageId, ts, messageDeltaBuffer, messageSentLen, output);
    }

    private static void FlushBufferedDelta(
        string messageId,
        long ts,
        Dictionary<string, StringBuilder> messageDeltaBuffer,
        Dictionary<string, int> messageSentLen,
        List<AgUiEvent> output)
    {
        if (!messageDeltaBuffer.TryGetValue(messageId, out var sb)) return;
        if (sb.Length == 0) return;

        var chunk = sb.ToString();
        sb.Clear();

        output.Add(new TextMessageContentEvent
        {
            Timestamp = ts,
            MessageId = messageId,
            Delta = chunk
        });

        messageSentLen[messageId] = messageSentLen.GetValueOrDefault(messageId, 0) + chunk.Length;
    }

    private static object[] BuildGraphJsonPatch(GraphEvent prev, GraphEvent cur)
    {
        // Patch target:
        // - The "state" object previously emitted via STATE_SNAPSHOT:
        //   { kind, sessionId, graph: { iteration, axioms, assumptions, theorems } }
        //
        // Therefore patch paths are rooted at "/graph/*" using camelCase property names.

        static object Replace(string path, object? value) => new { op = "replace", path, value };
        static object Add(string path, object? value) => new { op = "add", path, value };

        static bool IsPrefix<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
        {
            if (a.Count > b.Count) return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
                    return false;
            }
            return true;
        }

        static void PatchList<T>(string path, IReadOnlyList<T> a, IReadOnlyList<T> b, List<object> ops)
        {
            if (a.Count == b.Count && IsPrefix(a, b))
                return;

            // Good taste:
            // - Theorem discovery loop is append-heavy.
            // - If it's a pure append, emit a minimal "add" patch.
            // - Otherwise (reorder/replace/shrink), do a single replace to avoid fragile index math.
            if (IsPrefix(a, b))
            {
                for (var i = a.Count; i < b.Count; i++)
                    ops.Add(Add(path + "/-", b[i]));
                return;
            }

            ops.Add(Replace(path, b));
        }

        var ops = new List<object>(capacity: 12);

        if (prev.Iteration != cur.Iteration)
            ops.Add(Replace("/graph/iteration", cur.Iteration));

        PatchList("/graph/axioms", prev.Axioms, cur.Axioms, ops);
        PatchList("/graph/assumptions", prev.Assumptions, cur.Assumptions, ops);
        PatchList("/graph/theorems", prev.Theorems, cur.Theorems, ops);

        return ops.ToArray();
    }

    private static bool HasAnyMessageSignal(ProgressEvent p)
    {
        // Start streaming only when we have something to show or it is already finished with a body.
        if (!string.IsNullOrEmpty(p.TokenDelta)) return true;
        if (!string.IsNullOrEmpty(p.AssistantResponsePreview)) return true;
        if (!string.IsNullOrEmpty(p.AssistantResponse)) return true;
        if (!string.IsNullOrEmpty(p.SystemPrompt)) return true;
        if (!string.IsNullOrEmpty(p.UserPrompt)) return true;
        return IsFinished(p.StepStatus);
    }

    private static bool IsRunning(string? stepStatus)
    {
        if (string.IsNullOrWhiteSpace(stepStatus)) return false;
        var s = stepStatus.ToLowerInvariant();
        return s.Contains("running") || s.Contains("started");
    }

    private static bool IsFinished(string? stepStatus)
    {
        if (string.IsNullOrWhiteSpace(stepStatus)) return false;
        var s = stepStatus.ToLowerInvariant();
        return s.Contains("completed") || s.Contains("failed") || s.Contains("skipped");
    }

    private static string? ComputeCompletionDelta(
        string messageId,
        ProgressEvent p,
        Dictionary<string, int> messageSentLen)
    {
        // Prefer full body if present, otherwise preview.
        var body = p.AssistantResponse ?? p.AssistantResponsePreview;
        if (string.IsNullOrEmpty(body)) return null;

        var already = messageSentLen.GetValueOrDefault(messageId, 0);
        if (already < 0 || already > body.Length) already = 0;
        if (body.Length <= already) return null;
        return body[already..];
    }
}

