namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  REASONING PROGRESS
//  统一的推理进度报告模型
// ============================================================

/// <summary>
/// 推理进度报告。
/// 所有策略都可以填充的通用进度字段。
/// </summary>
public sealed record ReasoningProgress
{
    /// <summary>
    /// 当前阶段名称。
    /// </summary>
    public required string Phase { get; init; }

    /// <summary>
    /// 进度百分比 (0.0 - 1.0)。
    /// </summary>
    public float ProgressPercent { get; init; }

    /// <summary>
    /// 进度描述消息。
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 当前任务 ID。
    /// </summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// 时间戳。
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // ─────────────────────────────────────────────────────────
    //  MAKER 特有字段
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// MAKER: 当前递归深度。
    /// </summary>
    public int? Depth { get; init; }

    /// <summary>
    /// MAKER: 投票进度。
    /// </summary>
    public VotingProgress? Voting { get; init; }

    /// <summary>
    /// MAKER: 提案进度。
    /// </summary>
    public ProposalProgress? Proposal { get; init; }

    /// <summary>
    /// MAKER: 流式 Token。
    /// </summary>
    public StreamingTokenProgress? StreamingToken { get; init; }

    // ─────────────────────────────────────────────────────────
    //  UoT 特有字段
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// UoT: 已找到的类比数。
    /// </summary>
    public int? AnalogiesFound { get; init; }

    /// <summary>
    /// UoT: 已提取的思维单元数。
    /// </summary>
    public int? ThoughtsExtracted { get; init; }

    /// <summary>
    /// UoT: 已生成的候选方案数。
    /// </summary>
    public int? CandidatesGenerated { get; init; }

    /// <summary>
    /// UoT: 已通过可行性筛选的候选数。
    /// </summary>
    public int? CandidatesPassed { get; init; }

    // ─────────────────────────────────────────────────────────
    //  Cognitive DSL 工作流特有字段
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// DSL: 当前步骤 ID。
    /// </summary>
    public string? StepId { get; init; }

    /// <summary>
    /// DSL: 步骤类型 (llm_call, vote, fan_out, conditional, etc.)。
    /// </summary>
    public string? StepType { get; init; }

    /// <summary>
    /// DSL: 步骤状态 (Pending, Running, Completed, Failed, Skipped)。
    /// </summary>
    public string? StepStatus { get; init; }

    /// <summary>
    /// DSL Vote: 当前投票轮次。
    /// </summary>
    public int? VoteRound { get; init; }

    /// <summary>
    /// DSL Vote: 最大投票轮次。
    /// </summary>
    public int? VoteMaxRounds { get; init; }

    /// <summary>
    /// DSL Vote: 共识所需票数 K。
    /// </summary>
    public int? VoteK { get; init; }

    /// <summary>
    /// DSL Vote: 当前领先票数。
    /// </summary>
    public int? VoteCurrentVotes { get; init; }

    /// <summary>
    /// DSL Fan-out: 总任务数。
    /// </summary>
    public int? ParallelTotal { get; init; }

    /// <summary>
    /// DSL Fan-out: 已完成任务数。
    /// </summary>
    public int? ParallelCompleted { get; init; }

    /// <summary>
    /// DSL Fan-out: 失败任务数。
    /// </summary>
    public int? ParallelFailed { get; init; }

    // ─────────────────────────────────────────────────────────
    //  Cognitive DSL: LLM 对话记录（用于前端可视化）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// DSL: 系统提示词。
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// DSL: 用户提示词。
    /// </summary>
    public string? UserPrompt { get; init; }

    /// <summary>
    /// DSL: 助手响应。
    /// </summary>
    public string? AssistantResponse { get; init; }

    // ─────────────────────────────────────────────────────────
    //  Token 统计（通用）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 累计 Prompt Token 数。
    /// </summary>
    public long? TotalPromptTokens { get; init; }

    /// <summary>
    /// 累计 Completion Token 数。
    /// </summary>
    public long? TotalCompletionTokens { get; init; }

    /// <summary>
    /// 累计 LLM 调用次数。
    /// </summary>
    public int? TotalLlmCalls { get; init; }
}

/// <summary>
/// MAKER 投票进度。
/// </summary>
public sealed record VotingProgress
{
    /// <summary>投票类型（Decomposition / Solution）</summary>
    public required string Type { get; init; }

    /// <summary>当前轮次</summary>
    public int Round { get; init; }

    /// <summary>总票数</summary>
    public int TotalVotes { get; init; }

    /// <summary>达成共识所需票数差</summary>
    public int VotesNeeded { get; init; }

    /// <summary>领先者票数</summary>
    public int LeaderVotes { get; init; }

    /// <summary>第二名票数</summary>
    public int RunnerUpVotes { get; init; }

    /// <summary>聚类数量</summary>
    public int ClusterCount { get; init; }

    /// <summary>是否使用语义聚类</summary>
    public bool UsedSemanticClustering { get; init; }
}

/// <summary>
/// MAKER 提案进度。
/// </summary>
public sealed record ProposalProgress
{
    /// <summary>提案 ID</summary>
    public required string ProposalId { get; init; }

    /// <summary>提案内容</summary>
    public string? Content { get; init; }

    /// <summary>是否成功</summary>
    public bool Success { get; init; }

    /// <summary>错误信息</summary>
    public string? Error { get; init; }

    /// <summary>Prompt Token 数</summary>
    public int? PromptTokens { get; init; }

    /// <summary>Completion Token 数</summary>
    public int? CompletionTokens { get; init; }

    /// <summary>LLM 提供商名称</summary>
    public string? ProviderName { get; init; }
}

/// <summary>
/// 流式 Token 进度（实时 LLM 输出）。
/// </summary>
public sealed record StreamingTokenProgress
{
    /// <summary>Worker ID</summary>
    public required string WorkerId { get; init; }

    /// <summary>提案 ID</summary>
    public required string ProposalId { get; init; }

    /// <summary>当前 Token</summary>
    public string? Token { get; init; }

    /// <summary>累积内容</summary>
    public string? AccumulatedContent { get; init; }

    /// <summary>Token 索引</summary>
    public int TokenIndex { get; init; }

    /// <summary>是否为首个 Token</summary>
    public bool IsFirstToken { get; init; }

    /// <summary>是否为末尾 Token</summary>
    public bool IsLastToken { get; init; }

    /// <summary>系统提示词</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>用户提示词</summary>
    public string? UserPrompt { get; init; }

    /// <summary>LLM 提供商名称</summary>
    public string? ProviderName { get; init; }
}

