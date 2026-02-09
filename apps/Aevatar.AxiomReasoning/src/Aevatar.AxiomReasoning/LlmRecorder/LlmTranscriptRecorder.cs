using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aevatar.AxiomReasoning.Models;
using Aevatar.Agents.Cognitive.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.AxiomReasoning.LlmRecorder;

// ============================================================
//  LLM TRANSCRIPT RECORDER (Local)
//
//  目标：
//  - 把 Cognitive DSL 的 llm_call / vote（共识 winner）过程落到本地文件
//  - 产物：
//    - output/{sessionId}/llm/transcript.jsonl  (机器可读、可喂给 AI 做诊断)
//    - output/{sessionId}/llm/review.md         (人类可读、便于快速审阅)
//
//  关键约束：
//  - 这是 Progress<T> 回调路径：绝对不能抛异常（否则进程被杀）
//  - 不能 per-token 写盘：否则 I/O 把系统拖死
// ============================================================

public sealed class LlmTranscriptRecorder
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep JSONL readable (avoid \uXXXX for Chinese/math symbols).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IOptions<LlmTranscriptOptions> _options;
    private readonly ILogger<LlmTranscriptRecorder> _logger;

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    public LlmTranscriptRecorder(IOptions<LlmTranscriptOptions> options, ILogger<LlmTranscriptRecorder> logger)
    {
        _options = options;
        _logger = logger;
    }

    public void OnSessionStarted(AxiomSession session)
    {
        try
        {
            var opt = _options.Value;
            if (!opt.Enabled) return;
            if (session == null) return;
            if (string.IsNullOrWhiteSpace(session.OutputDir)) return;

            var state = GetOrCreateSessionState(session.Id, session.OutputDir);
            EnsureDirectories(state);

            if (opt.WriteJsonlTranscript)
            {
                AppendJsonl(state, new SessionStartRecord
                {
                    Type = "session_start",
                    SchemaVersion = SchemaVersion,
                    Timestamp = DateTimeOffset.UtcNow,
                    Session = new SessionMeta
                    {
                        SessionId = session.Id,
                        CreatedAt = session.CreatedAt,
                        Workflow = session.Workflow,
                        Language = session.Language,
                        K = session.K,
                        MaxRounds = session.MaxRounds,
                        MaxDepth = session.MaxDepth,
                        MaxDurationMinutes = session.MaxDurationMinutes,
                        MaxLlmCallsBudget = session.MaxLlmCallsBudget,
                        MaxTokensBudget = session.MaxTokensBudget,
                        ContinueOnFailure = session.ContinueOnFailure
                    },
                    Input = new SessionInput
                    {
                        AxiomsText = session.AxiomsText,
                        Goal = session.Goal,
                        SeedHypothesis = session.SeedHypothesis
                    }
                });
            }

            if (opt.WriteMarkdownReview)
            {
                InitializeReviewFile(state, session, opt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LLM-LOG] OnSessionStarted failed (ignored)");
        }
    }

    public void HandleProgress(AxiomSession session, ReasoningProgress p)
    {
        try
        {
            var opt = _options.Value;
            if (!opt.Enabled) return;
            if (session == null || p == null) return;
            if (string.IsNullOrWhiteSpace(session.OutputDir)) return;

            var state = GetOrCreateSessionState(session.Id, session.OutputDir);
            EnsureDirectories(state);

            var stepType = (p.StepType ?? "").Trim();
            if (!IsTranscriptTarget(stepType)) return;

            var depth = p.Depth ?? 0;
            var workerId = (p.StreamingToken?.WorkerId ?? p.TaskId ?? "coordinator").Trim();
            if (string.IsNullOrWhiteSpace(workerId)) workerId = "coordinator";

            var providerName = (p.StreamingToken?.ProviderName ?? p.Proposal?.ProviderName ?? "").Trim();
            var stepId = (p.StepId ?? p.StreamingToken?.ProposalId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(stepId)) return;

            // Key must be stable across running/complete events.
            // Depth matters because workflow_call can reuse step IDs.
            var callKey = $"{stepType}:{stepId}:d{depth}";

            var statusLower = (p.StepStatus ?? "").Trim().ToLowerInvariant();
            var isRunning = statusLower.Contains("running", StringComparison.Ordinal);
            var isCompleted = statusLower.Contains("completed", StringComparison.Ordinal);
            var isFailed = statusLower.Contains("failed", StringComparison.Ordinal);

            // ─────────────────────────────────────────────
            //  1) Streaming path (llm_call)
            // ─────────────────────────────────────────────
            if (p.StreamingToken != null && stepType.Equals("llm_call", StringComparison.OrdinalIgnoreCase))
            {
                var st = p.StreamingToken;

                if (st.IsFirstToken || isRunning)
                {
                    UpsertDraftStart(state, callKey, session, p, workerId, providerName, stepId, stepType, depth);
                }

                if (st.IsLastToken || isCompleted || isFailed)
                {
                    var assistant = st.AccumulatedContent ?? p.AssistantResponse ?? "";
                    FinalizeCall(state, callKey, session, p, workerId, providerName, stepId, stepType, depth, assistant, isFailed, opt);
                }

                return;
            }

            // ─────────────────────────────────────────────
            //  2) Non-streaming path (vote / llm_call fallback)
            // ─────────────────────────────────────────────
            if (isRunning)
            {
                UpsertDraftStart(state, callKey, session, p, workerId, providerName, stepId, stepType, depth);
            }

            if (isCompleted || isFailed)
            {
                var assistant = p.AssistantResponse ?? "";
                FinalizeCall(state, callKey, session, p, workerId, providerName, stepId, stepType, depth, assistant, isFailed, opt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LLM-LOG] HandleProgress failed (ignored)");
        }
    }

    public void OnSessionCompleted(AxiomSession session)
    {
        try
        {
            var opt = _options.Value;
            if (!opt.Enabled) return;
            if (session == null) return;
            if (string.IsNullOrWhiteSpace(session.OutputDir)) return;

            var state = GetOrCreateSessionState(session.Id, session.OutputDir);
            EnsureDirectories(state);

            // Flush unfinished drafts (best-effort, mark as incomplete).
            foreach (var (callKey, draft) in state.Drafts.ToArray())
            {
                if (!state.Drafts.TryRemove(callKey, out _)) continue;
                if (!state.CompletedKeys.TryAdd(callKey, 0)) continue;

                WriteInteractionRecords(
                    state,
                    session,
                    draft,
                    progress: null,
                    endedAt: DateTimeOffset.UtcNow,
                    status: "incomplete",
                    assistant: draft.LastAssistantSnapshot ?? "",
                    isFailed: true,
                    opt);
            }

            if (opt.WriteJsonlTranscript)
            {
                AppendJsonl(state, new SessionEndRecord
                {
                    Type = "session_end",
                    SchemaVersion = SchemaVersion,
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = session.Id,
                    Status = session.Status.ToString(),
                    Error = session.Error,
                    TotalLlmCalls = session.TotalLlmCalls,
                    TotalTokens = session.TotalTokens,
                    DurationSeconds = session.Duration.TotalSeconds
                });
            }

            if (opt.WriteMarkdownReview)
            {
                AppendReviewFooter(state, session);
            }

            // Prevent unbounded memory growth for long-running web host.
            _sessions.TryRemove(session.Id, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LLM-LOG] OnSessionCompleted failed (ignored)");
        }
    }

    // ============================================================
    //  Core: start/update/finalize calls
    // ============================================================

    private static bool IsTranscriptTarget(string stepType) =>
        stepType.Equals("llm_call", StringComparison.OrdinalIgnoreCase) ||
        stepType.Equals("vote", StringComparison.OrdinalIgnoreCase);

    private void UpsertDraftStart(
        SessionState state,
        string callKey,
        AxiomSession session,
        ReasoningProgress p,
        string workerId,
        string providerName,
        string stepId,
        string stepType,
        int depth)
    {
        var startedAt = p.Timestamp;
        var sys = p.StreamingToken?.SystemPrompt ?? p.SystemPrompt;
        var user = p.StreamingToken?.UserPrompt ?? p.UserPrompt;

        state.Drafts.AddOrUpdate(
            callKey,
            _ => new DraftCall
            {
                CallKey = callKey,
                SessionId = session.Id,
                StepId = stepId,
                StepType = stepType,
                WorkerId = workerId,
                ProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName,
                Depth = depth,
                Phase = p.Phase,
                StartedAt = startedAt,
                SystemPrompt = sys,
                UserPrompt = user
            },
            (_, existing) =>
            {
                // Keep earliest start time; fill missing prompts when they arrive late.
                if (existing.StartedAt == default || startedAt < existing.StartedAt)
                    existing.StartedAt = startedAt;

                existing.Phase ??= p.Phase;
                existing.ProviderName ??= string.IsNullOrWhiteSpace(providerName) ? null : providerName;

                if (string.IsNullOrWhiteSpace(existing.SystemPrompt) && !string.IsNullOrWhiteSpace(sys))
                    existing.SystemPrompt = sys;
                if (string.IsNullOrWhiteSpace(existing.UserPrompt) && !string.IsNullOrWhiteSpace(user))
                    existing.UserPrompt = user;

                if (!string.IsNullOrWhiteSpace(p.AssistantResponse))
                    existing.LastAssistantSnapshot = p.AssistantResponse;
                if (!string.IsNullOrWhiteSpace(p.StreamingToken?.AccumulatedContent))
                    existing.LastAssistantSnapshot = p.StreamingToken!.AccumulatedContent;

                return existing;
            });
    }

    private void FinalizeCall(
        SessionState state,
        string callKey,
        AxiomSession session,
        ReasoningProgress p,
        string workerId,
        string providerName,
        string stepId,
        string stepType,
        int depth,
        string assistant,
        bool isFailed,
        LlmTranscriptOptions opt)
    {
        // Dedup: completion might be reported multiple times in edge cases.
        if (!state.CompletedKeys.TryAdd(callKey, 0)) return;

        var endedAt = p.Timestamp;
        state.Drafts.TryRemove(callKey, out var draft);

        // If we never saw a "start", still write a record (best-effort).
        draft ??= new DraftCall
        {
            CallKey = callKey,
            SessionId = session.Id,
            StepId = stepId,
            StepType = stepType,
            WorkerId = workerId,
            ProviderName = string.IsNullOrWhiteSpace(providerName) ? null : providerName,
            Depth = depth,
            Phase = p.Phase,
            StartedAt = endedAt
        };

        if (string.IsNullOrWhiteSpace(draft.SystemPrompt) && !string.IsNullOrWhiteSpace(p.SystemPrompt))
            draft.SystemPrompt = p.SystemPrompt;
        if (string.IsNullOrWhiteSpace(draft.UserPrompt) && !string.IsNullOrWhiteSpace(p.UserPrompt))
            draft.UserPrompt = p.UserPrompt;
        draft.ProviderName ??= string.IsNullOrWhiteSpace(providerName) ? null : providerName;
        draft.LastAssistantSnapshot = assistant;

        var status = isFailed ? "failed" : "completed";
        WriteInteractionRecords(state, session, draft, p, endedAt, status, assistant, isFailed, opt);
    }

    private void WriteInteractionRecords(
        SessionState state,
        AxiomSession session,
        DraftCall draft,
        ReasoningProgress? progress,
        DateTimeOffset endedAt,
        string status,
        string assistant,
        bool isFailed,
        LlmTranscriptOptions opt)
    {
        var startedAt = draft.StartedAt == default ? endedAt : draft.StartedAt;
        var durationMs = (long)Math.Max(0, (endedAt - startedAt).TotalMilliseconds);

        // Safety truncation (disk guardrail).
        var systemPrompt = TruncateWithMarker(draft.SystemPrompt, opt.MaxPromptChars);
        var userPrompt = TruncateWithMarker(draft.UserPrompt, opt.MaxPromptChars);
        var assistantResponse = TruncateWithMarker(assistant, opt.MaxResponseChars);

        var estPromptTokens = EstimateTokens(systemPrompt) + EstimateTokens(userPrompt);
        var estCompletionTokens = EstimateTokens(assistantResponse);

        if (opt.WriteJsonlTranscript)
        {
            var voteRound = progress?.VoteRound ?? 0;
            var voteMaxRounds = progress?.VoteMaxRounds ?? 0;
            var voteK = progress?.VoteK ?? 0;
            var voteCurrentVotes = progress?.VoteCurrentVotes ?? 0;

            AppendJsonl(state, new InteractionRecord
            {
                Type = "interaction",
                SchemaVersion = SchemaVersion,
                Timestamp = endedAt,
                SessionId = session.Id,
                Call = new CallMeta
                {
                    StepId = draft.StepId,
                    StepType = draft.StepType,
                    Phase = draft.Phase ?? session.CurrentPhase,
                    Depth = draft.Depth,
                    WorkerId = draft.WorkerId,
                    ProviderName = draft.ProviderName,
                    StepStatus = progress?.StepStatus,
                    Message = progress?.Message,
                    VoteRound = voteRound,
                    VoteMaxRounds = voteMaxRounds,
                    VoteK = voteK,
                    VoteCurrentVotes = voteCurrentVotes
                },
                Timing = new TimingMeta
                {
                    StartedAt = startedAt,
                    EndedAt = endedAt,
                    DurationMs = durationMs
                },
                Status = status,
                Error = isFailed
                    ? (progress?.Message ?? session.Error ?? "failed")
                    : null,
                Prompts = new PromptBlock
                {
                    System = systemPrompt,
                    User = userPrompt
                },
                Response = new ResponseBlock
                {
                    Assistant = assistantResponse
                },
                Metrics = new InteractionMetrics
                {
                    EstimatedPromptTokens = estPromptTokens,
                    EstimatedCompletionTokens = estCompletionTokens,
                    EstimatedTotalTokens = estPromptTokens + estCompletionTokens
                }
            });
        }

        if (opt.WriteMarkdownReview)
        {
            AppendReviewEntry(
                state,
                draft,
                startedAt,
                endedAt,
                durationMs,
                status,
                systemPrompt,
                userPrompt,
                assistantResponse,
                estPromptTokens,
                estCompletionTokens,
                progress?.Message,
                progress?.VoteRound,
                progress?.VoteK,
                progress?.VoteCurrentVotes,
                opt);
        }

        // Local helpers: avoid leaking big strings in memory.
        draft.SystemPrompt = null;
        draft.UserPrompt = null;
        draft.LastAssistantSnapshot = null;
    }

    // ============================================================
    //  File IO (all guarded by per-session lock)
    // ============================================================

    private static void EnsureDirectories(SessionState state)
    {
        if (string.IsNullOrWhiteSpace(state.OutputDir)) return;
        Directory.CreateDirectory(state.LlmDir);
    }

    private static void AppendJsonl(SessionState state, object record)
    {
        var json = JsonSerializer.Serialize(record, JsonOptions);
        lock (state.Sync)
        {
            File.AppendAllText(state.TranscriptPath, json + "\n", Encoding.UTF8);
        }
    }

    private static void InitializeReviewFile(SessionState state, AxiomSession session, LlmTranscriptOptions opt)
    {
        lock (state.Sync)
        {
            if (state.ReviewInitialized) return;
            state.ReviewInitialized = true;

            var sb = new StringBuilder();
            sb.AppendLine("# AxiomReasoning · LLM Review");
            sb.AppendLine();
            sb.AppendLine($"- SessionId: `{session.Id}`");
            sb.AppendLine($"- CreatedAt: `{session.CreatedAt:O}`");
            sb.AppendLine($"- Workflow: `{session.Workflow}`");
            sb.AppendLine($"- Language: `{session.Language}`");
            sb.AppendLine($"- K: `{session.K}`");
            sb.AppendLine($"- MaxRounds: `{session.MaxRounds}`");
            sb.AppendLine($"- MaxDepth: `{session.MaxDepth}`");
            sb.AppendLine();
            sb.AppendLine("## Input");
            sb.AppendLine();
            sb.AppendLine("<details open><summary>Axioms</summary><pre>");
            sb.AppendLine(WebUtility.HtmlEncode(TruncateWithMarker(session.AxiomsText, opt.MaxMarkdownCharsPerMessage)));
            sb.AppendLine("</pre></details>");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(session.Goal))
            {
                sb.AppendLine("<details><summary>Goal</summary><pre>");
                sb.AppendLine(WebUtility.HtmlEncode(TruncateWithMarker(session.Goal, opt.MaxMarkdownCharsPerMessage)));
                sb.AppendLine("</pre></details>");
                sb.AppendLine();
            }
            sb.AppendLine("## Interactions");
            sb.AppendLine();

            File.WriteAllText(state.ReviewPath, sb.ToString(), Encoding.UTF8);
        }
    }

    private static void AppendReviewEntry(
        SessionState state,
        DraftCall draft,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        long durationMs,
        string status,
        string? systemPrompt,
        string? userPrompt,
        string? assistant,
        int estPromptTokens,
        int estCompletionTokens,
        string? message,
        int? voteRound,
        int? voteK,
        int? voteCurrentVotes,
        LlmTranscriptOptions opt)
    {
        lock (state.Sync)
        {
            var idx = ++state.ReviewIndex;

            var sb = new StringBuilder();
            sb.AppendLine($"### [{idx:D4}] {draft.StepType} · {draft.WorkerId} · depth={draft.Depth} · {status}");
            sb.AppendLine();
            sb.AppendLine($"- stepId: `{draft.StepId}`");
            if (!string.IsNullOrWhiteSpace(draft.Phase)) sb.AppendLine($"- phase: `{draft.Phase}`");
            if (!string.IsNullOrWhiteSpace(draft.ProviderName)) sb.AppendLine($"- provider: `{draft.ProviderName}`");
            if (!string.IsNullOrWhiteSpace(message)) sb.AppendLine($"- message: `{message}`");
            if (voteK is > 0) sb.AppendLine($"- vote: round `{voteRound ?? 0}` · votes `{voteCurrentVotes ?? 0}/{voteK}`");
            sb.AppendLine($"- startedAt: `{startedAt:O}`");
            sb.AppendLine($"- endedAt: `{endedAt:O}`");
            sb.AppendLine($"- durationMs: `{durationMs}`");
            sb.AppendLine($"- estTokens: prompt≈`{estPromptTokens}` + completion≈`{estCompletionTokens}`");
            sb.AppendLine();

            AppendDetails(sb, "System Prompt", systemPrompt, opt.MaxMarkdownCharsPerMessage);
            AppendDetails(sb, "User Prompt", userPrompt, opt.MaxMarkdownCharsPerMessage);
            AppendDetails(sb, "Assistant", assistant, opt.MaxMarkdownCharsPerMessage);

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            File.AppendAllText(state.ReviewPath, sb.ToString(), Encoding.UTF8);
        }
    }

    private static void AppendReviewFooter(SessionState state, AxiomSession session)
    {
        lock (state.Sync)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Session Summary");
            sb.AppendLine();
            sb.AppendLine($"- Status: `{session.Status}`");
            if (!string.IsNullOrWhiteSpace(session.Error)) sb.AppendLine($"- Error: `{WebUtility.HtmlEncode(session.Error)}`");
            sb.AppendLine($"- TotalLlmCalls: `{session.TotalLlmCalls}`");
            sb.AppendLine($"- TotalTokens: `{session.TotalTokens}`");
            sb.AppendLine($"- DurationSeconds: `{session.Duration.TotalSeconds:F1}`");
            sb.AppendLine();
            sb.AppendLine("Artifacts:");
            sb.AppendLine($"- `artifacts/state.json`");
            sb.AppendLine($"- `artifacts/theorems.json`");
            sb.AppendLine($"- `llm/transcript.jsonl`");
            sb.AppendLine();

            File.AppendAllText(state.ReviewPath, sb.ToString(), Encoding.UTF8);
        }
    }

    private static void AppendDetails(StringBuilder sb, string title, string? content, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        sb.AppendLine($"<details><summary>{WebUtility.HtmlEncode(title)}</summary><pre>");
        // Human readability:
        // - Many prompts include JSON blocks rendered by the template filter `| json`.
        //   System.Text.Json defaults to \uXXXX escaping for non-ASCII, which is painful in review.md.
        // - Decode common backslash escapes (e.g. \u210B, \n) BEFORE HtmlEncode.
        var text = TruncateWithMarker(content, maxChars);
        text = DecodeBackslashEscapes(text);
        sb.AppendLine(WebUtility.HtmlEncode(text));
        sb.AppendLine("</pre></details>");
        sb.AppendLine();
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private static string? DecodeBackslashEscapes(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (!s.Contains('\\', StringComparison.Ordinal)) return s;

        // Best-effort JSON-style unescape for readability in review.md:
        // - \uXXXX (incl. surrogate pairs)
        // - \n \r \t \\ \" \/
        // NOTE:
        // - This is intentionally NOT a strict JSON parser; it just improves human readability.
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch != '\\' || i + 1 >= s.Length)
            {
                sb.Append(ch);
                continue;
            }

            var next = s[i + 1];
            switch (next)
            {
                case 'n':
                    sb.Append('\n');
                    i++;
                    break;
                case 'r':
                    sb.Append('\r');
                    i++;
                    break;
                case 't':
                    sb.Append('\t');
                    i++;
                    break;
                case 'b':
                    sb.Append('\b');
                    i++;
                    break;
                case 'f':
                    sb.Append('\f');
                    i++;
                    break;
                case '\\':
                    sb.Append('\\');
                    i++;
                    break;
                case '"':
                    sb.Append('"');
                    i++;
                    break;
                case '/':
                    sb.Append('/');
                    i++;
                    break;
                case 'u':
                {
                    // Need 4 hex digits
                    if (i + 6 > s.Length)
                    {
                        sb.Append('\\');
                        break;
                    }

                    var h1 = s[i + 2];
                    var h2 = s[i + 3];
                    var h3 = s[i + 4];
                    var h4 = s[i + 5];
                    if (!IsHex(h1) || !IsHex(h2) || !IsHex(h3) || !IsHex(h4))
                    {
                        sb.Append('\\');
                        break;
                    }

                    var code = (HexVal(h1) << 12) | (HexVal(h2) << 8) | (HexVal(h3) << 4) | HexVal(h4);
                    var c1 = (char)code;
                    i += 5;

                    // Surrogate pair: \uD83D\uDE00
                    if (char.IsHighSurrogate(c1) &&
                        i + 6 < s.Length &&
                        s[i + 1] == '\\' &&
                        s[i + 2] == 'u' &&
                        IsHex(s[i + 3]) &&
                        IsHex(s[i + 4]) &&
                        IsHex(s[i + 5]) &&
                        IsHex(s[i + 6]))
                    {
                        var code2 = (HexVal(s[i + 3]) << 12) | (HexVal(s[i + 4]) << 8) | (HexVal(s[i + 5]) << 4) | HexVal(s[i + 6]);
                        var c2 = (char)code2;
                        if (char.IsLowSurrogate(c2))
                        {
                            sb.Append(char.ConvertFromUtf32(char.ConvertToUtf32(c1, c2)));
                            i += 6; // consume \uXXXX (the leading '\' is at i+1)
                            break;
                        }
                    }

                    sb.Append(c1);
                    break;
                }
                default:
                    // Unknown escape: keep the backslash as-is for safety.
                    sb.Append('\\');
                    break;
            }
        }

        return sb.ToString();

        static bool IsHex(char c)
            => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        static int HexVal(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => 10 + (c - 'a'),
            >= 'A' and <= 'F' => 10 + (c - 'A'),
            _ => 0
        };
    }

    private SessionState GetOrCreateSessionState(string sessionId, string outputDir)
    {
        return _sessions.AddOrUpdate(
            sessionId,
            _ =>
            {
                var s = new SessionState(sessionId);
                s.SetOutputDir(outputDir);
                return s;
            },
            (_, existing) =>
            {
                existing.SetOutputDir(outputDir);
                return existing;
            });
    }

    private static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // 经验估算：英语/代码约 4 chars/token；中文更密，但这里仅作相对量度。
        return Math.Max(1, text.Length / 4);
    }

    private static string? TruncateWithMarker(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (maxChars <= 0) return "";
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "\n…(truncated)…";
    }

    // ============================================================
    //  JSONL schema records
    // ============================================================

    private sealed record SessionStartRecord
    {
        public required string Type { get; init; }
        public required int SchemaVersion { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required SessionMeta Session { get; init; }
        public required SessionInput Input { get; init; }
    }

    private sealed record SessionEndRecord
    {
        public required string Type { get; init; }
        public required int SchemaVersion { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required string SessionId { get; init; }
        public required string Status { get; init; }
        public string? Error { get; init; }
        public int TotalLlmCalls { get; init; }
        public long TotalTokens { get; init; }
        public double DurationSeconds { get; init; }
    }

    private sealed record InteractionRecord
    {
        public required string Type { get; init; }
        public required int SchemaVersion { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required string SessionId { get; init; }
        public required CallMeta Call { get; init; }
        public required TimingMeta Timing { get; init; }
        public required string Status { get; init; }
        public string? Error { get; init; }
        public required PromptBlock Prompts { get; init; }
        public required ResponseBlock Response { get; init; }
        public required InteractionMetrics Metrics { get; init; }
    }

    private sealed record SessionMeta
    {
        public required string SessionId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public string Workflow { get; init; } = "";
        public string Language { get; init; } = "";
        public int K { get; init; }
        public int MaxRounds { get; init; }
        public int MaxDepth { get; init; }
        public int MaxDurationMinutes { get; init; }
        public int MaxLlmCallsBudget { get; init; }
        public long MaxTokensBudget { get; init; }
        public bool ContinueOnFailure { get; init; }
    }

    private sealed record SessionInput
    {
        public string AxiomsText { get; init; } = "";
        public string Goal { get; init; } = "";
        public string SeedHypothesis { get; init; } = "";
    }

    private sealed record CallMeta
    {
        public string StepId { get; init; } = "";
        public string StepType { get; init; } = "";
        public string? Phase { get; init; }
        public int Depth { get; init; }
        public string WorkerId { get; init; } = "";
        public string? ProviderName { get; init; }
        public string? StepStatus { get; init; }
        public string? Message { get; init; }
        public int VoteRound { get; init; }
        public int VoteMaxRounds { get; init; }
        public int VoteK { get; init; }
        public int VoteCurrentVotes { get; init; }
    }

    private sealed record TimingMeta
    {
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset EndedAt { get; init; }
        public long DurationMs { get; init; }
    }

    private sealed record PromptBlock
    {
        public string? System { get; init; }
        public string? User { get; init; }
    }

    private sealed record ResponseBlock
    {
        public string? Assistant { get; init; }
    }

    private sealed record InteractionMetrics
    {
        public int EstimatedPromptTokens { get; init; }
        public int EstimatedCompletionTokens { get; init; }
        public int EstimatedTotalTokens { get; init; }
    }

    private sealed class DraftCall
    {
        public string CallKey { get; init; } = "";
        public string SessionId { get; init; } = "";
        public string StepId { get; init; } = "";
        public string StepType { get; init; } = "";
        public string WorkerId { get; init; } = "";
        public string? ProviderName { get; set; }
        public int Depth { get; init; }
        public string? Phase { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string? SystemPrompt { get; set; }
        public string? UserPrompt { get; set; }
        public string? LastAssistantSnapshot { get; set; }
    }

    private sealed class SessionState
    {
        public SessionState(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }
        public object Sync { get; } = new();

        public string OutputDir { get; private set; } = "";
        public string LlmDir => Path.Combine(OutputDir, "llm");
        public string TranscriptPath => Path.Combine(LlmDir, "transcript.jsonl");
        public string ReviewPath => Path.Combine(LlmDir, "review.md");

        public bool ReviewInitialized { get; set; }
        public int ReviewIndex { get; set; }

        public ConcurrentDictionary<string, DraftCall> Drafts { get; } = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, byte> CompletedKeys { get; } = new(StringComparer.Ordinal);

        public void SetOutputDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            OutputDir = dir;
        }
    }
}

