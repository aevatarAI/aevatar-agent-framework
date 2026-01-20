using Aevatar.Agents;

namespace Aevatar.Agents.Abstractions.Tracing;

// ============================================================
//  ExecutionTraceEvent Fields (Unified)
//
//  统一字段键名：Maker / Cognitive / UoT 等执行流统一投影到 ExecutionTraceEvent.fields
//  NOTE:
//  - fields 只承载“轻量、可索引”的信息；大块内容请使用 message 或外部存储。
//  - status 使用小写：pending/running/completed/failed/cancelled
// ============================================================
public static class ExecutionTraceEventFields
{
    // Identity
    public const string AgentId = "agent_id";
    public const string SessionId = "session_id";
    public const string MessageId = "message_id";

    // Core
    public const string Status = "status";
    public const string Progress = "progress";
    public const string ExecutionId = "execution_id";
    public const string WorkflowName = "workflow_name";
    public const string StepType = "step_type";
    public const string ParentStepId = "parent_step_id";
    public const string Depth = "depth";

    // Voting
    public const string VoteRound = "vote_round";
    public const string VoteMaxRounds = "vote_max_rounds";
    public const string VoteK = "vote_k";
    public const string VoteCurrentVotes = "vote_current_votes";
    public const string WinnerProposalId = "winner_proposal_id";
    public const string WinnerHash = "winner_hash";
    public const string WinnerVotes = "winner_votes";
    public const string WinnerRunnerUpVotes = "winner_runner_up_votes";
    public const string WinnerClusterCount = "winner_cluster_count";
    public const string WinnerSemantic = "winner_semantic";
    public const string WinnerIsConsensus = "winner_is_consensus";

    // Parallel / fan-out
    public const string ParallelTotal = "parallel_total";
    public const string ParallelCompleted = "parallel_completed";
    public const string ParallelFailed = "parallel_failed";

    // Worker / proposal
    public const string WorkerId = "worker_id";
    public const string ProposalId = "proposal_id";

    // Token / cost
    public const string TokensUsed = "tokens_used";
    public const string LlmCalls = "llm_calls";
    public const string PromptTokens = "prompt_tokens";
    public const string CompletionTokens = "completion_tokens";

    // LLM/tool lifecycle
    public const string Phase = "phase";
    public const string LlmModel = "llm_model";
    public const string ToolName = "tool_name";
    public const string ToolCallId = "tool_call_id";
    public const string DurationMs = "duration_ms";
    public const string Error = "error";

    // Optional LLM context (keep short; prefer external storage for large payloads)
    public const string SystemPrompt = "system_prompt";
    public const string UserPrompt = "user_prompt";
    public const string AssistantResponse = "assistant_response";
}

public static class ExecutionTraceEventStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class ExecutionTraceEventPhase
{
    public const string SessionStart = "session.start";
    public const string SessionStop = "session.stop";
    public const string LlmRequest = "llm.request";
    public const string LlmResponse = "llm.response";
    public const string ToolStart = "tool.start";
    public const string ToolProgress = "tool.progress";
    public const string ToolEnd = "tool.end";
    public const string Error = "error";
}

public static class ExecutionTraceEventFieldValue
{
    public static ContextValue FromString(string? value) => new() { StringValue = value ?? string.Empty };
    public static ContextValue FromBool(bool value) => new() { BoolValue = value };
    public static ContextValue FromInt(int value) => new() { IntValue = value };
    public static ContextValue FromLong(long value) => new() { IntValue = value };
    public static ContextValue FromDouble(double value) => new() { DoubleValue = value };
}
