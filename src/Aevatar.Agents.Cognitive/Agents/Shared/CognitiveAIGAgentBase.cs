using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveAIGAgentBase
//
//  WHY:
//  - CognitiveCoordinator/Worker both need:
//    1) Reuse AIGAgentBase.ChatAsync/ChatStreamAsync (avoid manual Provider call chain)
//    2) State.History only for UI hydration (not involved in next LLM prompt construction)
//    3) Write stable metadata per step (step_id/step_type/...) to support parallel/replay/refresh recovery
//
//  NOTE:
//  - This base class only unifies "LLM request form + history persistence strategy", doesn't touch business orchestration.
// ============================================================
public abstract class CognitiveAIGAgentBase<TCustomState> : AIGAgentBase<TCustomState>
    where TCustomState : class, IMessage<TCustomState>, new()
{
    private string? _sessionId;

    protected string? SessionId => _sessionId;

    // ============================================================
    //  History policy (no extra LLM calls)
    //
    //  WHY:
    //  - AIGAgentBase compaction (Layer 2) may call LLM to summarize history.
    //  - Cognitive workflow has strict budget; hidden LLM calls unacceptable.
    //
    //  HOW:
    //  - Keep a bounded sliding window only (Layer 1), never summarize.
    // ============================================================

    protected CognitiveAIGAgentBase()
    {
        EnableChatHistoryInState = true;
        EnableChatHistoryCompaction = false; // critical: avoid LLM summarization

        // Keep a small window for UI hydration. Hard cap is enforced in AddMessageToHistory.
        ChatHistoryMaxMessages = 32;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Best-effort: restore session id from persisted state context.
        if (State.Context.TryGetValue(ChatRequest.SessionIdKey, out var raw) &&
            !string.IsNullOrWhiteSpace(raw))
        {
            _sessionId = raw.Trim();
        }
        else if (State.Context.TryGetValue(ChatRequest.SessionIdKeyCamel, out var camel) &&
                 !string.IsNullOrWhiteSpace(camel))
        {
            _sessionId = camel.Trim();
        }
    }

    /// <summary>
    /// Configure session context for memory/history aggregation.
    /// </summary>
    public void ConfigureSessionContext(
        string sessionId,
        bool enableSessionMemory,
        bool enableAgentMemory)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return;

        _sessionId = sessionId.Trim();
        
        // NOTE: Do NOT modify State.Context here to avoid Event Sourcing conflicts.
        // When Event Sourcing is active (Version > 0), direct State modification is not allowed.
        // The _sessionId field is sufficient - ApplySessionContext() uses it directly,
        // and OnActivateAsync() can restore it from State.Context if needed.
        // This fixes: "Direct State modification is not allowed when Event Sourcing is active (Version > 0)"

        EnableSessionMemoryStoreAppend = enableSessionMemory;
        EnableMemoryStoreAppend = enableAgentMemory;
    }

    protected void ApplySessionContext(ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(_sessionId))
            return;

        request.SetSessionId(_sessionId);
    }

    private void TrimHistoryWindowBestEffort()
    {
        try
        {
            // Skip trimming when Event Sourcing is active (Version > 0)
            // History modifications should go through RaiseEvent in Event Sourcing mode
            if (GetCurrentVersion() > 0)
                return;

            var max = ChatHistoryMaxMessages;
            if (max <= 0) return;

            // Actor model: single-threaded per agent. Keep it simple.
            while (State.History.Count > max)
                State.History.RemoveAt(0);
        }
        catch
        {
            // best-effort only (history is optional)
        }
    }

    // ============================================================
    //  Tools policy
    //
    //  WHY:
    //  - Cognitive DSL is prompt-driven, Function Calling would pollute prompt semantics
    // ============================================================
    protected override Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        // Do NOT register built-in tools for Cognitive agents by default.
        return Task.CompletedTask;
    }

    // ============================================================
    //  Prompt policy: keep requests stateless
    //
    //  WHY:
    //  - State.History only for UI hydration (short window), cannot replay to LLM
    //  - Cognitive prompt already contains workflow variables/context; replaying history only amplifies tokens + noise
    // ============================================================
    protected override AevatarLLMRequest BuildLLMRequest(ChatRequest request)
    {
        var settings = GetLLMSettings(request);
        if (request.StopSequences.Count > 0)
        {
            settings.StopSequences = new List<string>(request.StopSequences);
        }

        var messages = new List<AevatarChatMessage>
        {
            new()
            {
                Role = AevatarChatRole.User,
                Content = request.Message
            }
        };

        // Per-step system prompt override.
        var systemPrompt = GetEffectiveSystemPrompt() ?? string.Empty;
        if (request.Context.TryGetValue("system_prompt", out var overrideSp) &&
            !string.IsNullOrWhiteSpace(overrideSp))
        {
            systemPrompt = overrideSp.Trim();
        }

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = systemPrompt,
            Messages = messages,
            Settings = settings
        };

        if (!string.IsNullOrWhiteSpace(request.StageHint))
        {
            llmRequest.Context = new Dictionary<string, object>
            {
                ["stage_hint"] = request.StageHint
            };
        }

        return llmRequest;
    }

    // ============================================================
    //  Step-scoped history metadata injection
    // ============================================================

    private static readonly AsyncLocal<StepHistoryContext?> StepHistory = new();

    private sealed class StepHistoryContext
    {
        public Dictionary<string, string> Metadata { get; init; } = new();
        public string? SystemPrompt { get; init; }
        public bool SystemWritten { get; set; }
    }

    private sealed class StepHistoryScope : IDisposable
    {
        private readonly StepHistoryContext? _prev;

        public StepHistoryScope(StepHistoryContext ctx)
        {
            _prev = StepHistory.Value;
            StepHistory.Value = ctx;
        }

        public void Dispose()
        {
            StepHistory.Value = _prev;
        }
    }

    protected IDisposable BeginStepHistory(
        string stepId,
        string stepType,
        string? systemPrompt = null,
        string? requestId = null)
    {
        var meta = new Dictionary<string, string>(capacity: 8)
        {
            ["step_id"] = stepId ?? string.Empty,
            ["step_type"] = stepType ?? string.Empty,
            ["agent_kind"] = AgentKind
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            meta["request_id"] = requestId!;
        }

        AppendAgentHistoryMetadata(meta);

        return new StepHistoryScope(new StepHistoryContext
        {
            Metadata = meta,
            SystemPrompt = systemPrompt
        });
    }

    /// <summary>
    /// Agent kind identifier for history metadata (e.g. "cognitive_worker").
    /// </summary>
    protected abstract string AgentKind { get; }

    /// <summary>
    /// Allow derived classes to inject extra metadata (e.g. worker_id/execution_id).
    /// </summary>
    protected virtual void AppendAgentHistoryMetadata(Dictionary<string, string> metadata)
    {
    }

    protected override void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
    {
        var ctx = StepHistory.Value;
        if (ctx == null)
        {
            base.AddMessageToHistory(content, role, name);
            TrimHistoryWindowBestEffort();
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
            return;

        // Ensure per-step system prompt is captured once (before the user message).
        if (role == AevatarChatRole.User &&
            !ctx.SystemWritten &&
            !string.IsNullOrWhiteSpace(ctx.SystemPrompt))
        {
            base.AddMessageToHistory(BuildStepHistoryMessage(AevatarChatRole.System, ctx.SystemPrompt!, ctx.Metadata));
            ctx.SystemWritten = true;
        }

        base.AddMessageToHistory(BuildStepHistoryMessage(role, content, ctx.Metadata));
        TrimHistoryWindowBestEffort();
    }

    private static AevatarChatMessage BuildStepHistoryMessage(
        AevatarChatRole role,
        string content,
        Dictionary<string, string> metadata)
    {
        var msg = new AevatarChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        foreach (var (k, v) in metadata)
        {
            msg.Metadata[k] = v;
        }

        return msg;
    }
}

