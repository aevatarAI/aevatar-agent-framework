namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  MESH EVENT SINK
//  事件输出接口（用于 SSE 推送）
// ============================================================

/// <summary>
/// 网格事件接收器。
/// 策略执行时通过此接口推送实时事件。
/// </summary>
public interface IMeshEventSink
{
    /// <summary>
    /// 发送事件。
    /// </summary>
    ValueTask SendAsync(MeshEvent evt, CancellationToken ct = default);

    /// <summary>
    /// 标记事件流结束。
    /// </summary>
    void Complete();
}

/// <summary>
/// 网格事件基类。
/// </summary>
public abstract record MeshEvent
{
    /// <summary>事件类型</summary>
    public abstract string Type { get; }

    /// <summary>运行 ID</summary>
    public required string RunId { get; init; }

    /// <summary>时间戳</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 进度事件。
/// </summary>
public sealed record ProgressEvent : MeshEvent
{
    public override string Type => "progress";

    public required string Phase { get; init; }
    public string? Message { get; init; }
    public string? TaskId { get; init; }
    public int? Depth { get; init; }
    public float? ProgressPercent { get; init; }
}

/// <summary>
/// 投票事件。
/// </summary>
public sealed record VotingEvent : MeshEvent
{
    public override string Type => "voting";

    public required string TaskId { get; init; }
    public required string VotingType { get; init; }
    public int Round { get; init; }
    public int TotalVotes { get; init; }
    public int VotesNeeded { get; init; }
    public int LeaderVotes { get; init; }
    public int RunnerUpVotes { get; init; }
    public int ClusterCount { get; init; }
    public bool UsedSemanticClustering { get; init; }
    public bool Success { get; init; }
}

/// <summary>
/// 提案事件。
/// </summary>
public sealed record ProposalEvent : MeshEvent
{
    public override string Type => "proposal";

    public required string TaskId { get; init; }
    public string? Content { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public string? ProviderName { get; init; }
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
}

/// <summary>
/// 流式输出事件。
/// </summary>
public sealed record StreamingEvent : MeshEvent
{
    public override string Type => "streaming";

    public required string TaskId { get; init; }
    public required string WorkerId { get; init; }
    public required string ProposalId { get; init; }
    public string? Token { get; init; }
    public string? AccumulatedContent { get; init; }
    public int TokenIndex { get; init; }
    public bool IsFirstToken { get; init; }
    public bool IsLastToken { get; init; }
    public string? ProviderName { get; init; }
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
}

/// <summary>
/// 结果事件。
/// </summary>
public sealed record ResultEvent : MeshEvent
{
    public override string Type => "result";

    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public long TotalTokens { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalLlmCalls { get; init; }
}

/// <summary>
/// 错误事件。
/// </summary>
public sealed record ErrorEvent : MeshEvent
{
    public override string Type => "error";

    public required string Message { get; init; }
    public string? StackTrace { get; init; }
}

/// <summary>
/// 文件生成事件。
/// </summary>
public sealed record FileEvent : MeshEvent
{
    public override string Type => "file";

    public required string Category { get; init; }
    public required string FileName { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// DSL 工作流步骤事件（用于可视化）。
/// </summary>
public sealed record WorkflowStepMeshEvent : MeshEvent
{
    public override string Type => "workflow_step";

    /// <summary>步骤 ID</summary>
    public required string StepId { get; init; }

    /// <summary>步骤类型 (llm_call, vote, fan_out, etc.)</summary>
    public required string StepType { get; init; }

    /// <summary>步骤状态 (Pending, Running, Completed, Failed, Skipped)</summary>
    public required string Status { get; init; }

    /// <summary>进度 (0-1)</summary>
    public float Progress { get; init; }

    /// <summary>消息</summary>
    public string? Message { get; init; }

    /// <summary>父步骤 ID（嵌套时）</summary>
    public string? ParentStepId { get; init; }

    /// <summary>递归深度</summary>
    public int Depth { get; init; }

    // ─── 投票特有 ───
    public int? VoteRound { get; init; }
    public int? VoteMaxRounds { get; init; }
    public int? VoteK { get; init; }
    public int? VoteCurrentVotes { get; init; }

    // ─── 并行特有 ───
    public int? ParallelTotal { get; init; }
    public int? ParallelCompleted { get; init; }
    public int? ParallelFailed { get; init; }

    // ─── 统计 ───
    public int? DurationMs { get; init; }
    public int? LlmCalls { get; init; }
    public int? TokensUsed { get; init; }

    // ─── LLM 对话记录（用于前端可视化）───
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
    public string? AssistantResponse { get; init; }
}

