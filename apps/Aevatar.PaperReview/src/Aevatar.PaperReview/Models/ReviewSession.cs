using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.Agents.Cognitive.Core;

namespace Aevatar.PaperReview.Models;

// ============================================================
//  REVIEW SESSION
//  论文评审会话模型
// ============================================================

/// <summary>
/// 评审会话状态。
/// </summary>
public enum ReviewStatus
{
    /// <summary>待开始</summary>
    Pending,
    /// <summary>评审中</summary>
    Reviewing,
    /// <summary>已完成</summary>
    Completed,
    /// <summary>已失败</summary>
    Failed,
    /// <summary>已取消</summary>
    Cancelled
}

// ============================================================
//  REVIEW PHASE - 评审阶段（替代 MakerPhase）
// ============================================================

/// <summary>
/// 评审阶段枚举。
/// </summary>
public enum ReviewPhase
{
    /// <summary>启动中</summary>
    Starting,
    /// <summary>分析任务</summary>
    Assessing,
    /// <summary>分解任务</summary>
    Decomposing,
    /// <summary>求解中</summary>
    Solving,
    /// <summary>执行子任务</summary>
    Executing,
    /// <summary>投票共识</summary>
    Voting,
    /// <summary>合成结果</summary>
    Composing,
    /// <summary>已完成</summary>
    Completed,
    /// <summary>失败</summary>
    Failed
}

// ============================================================
//  REVIEW TYPE - MAKER 共识难度等级
//  基于论文公式: K = 领先票数, N = 2K - 1 = 采样数
//  参考: https://arxiv.org/html/2511.09030v1
// ============================================================

/// <summary>
/// 评审类型 - 基于 MAKER 论文的共识难度等级。
/// 从易到难: Quick → Standard → Detailed → Rigorous → Critical
/// </summary>
public enum ReviewType
{
    /// <summary>
    /// 快速评审 (K=1, N=1)
    /// - 单次采样，无投票
    /// - 最快最便宜，但可能有错误
    /// - 适合：探索性评审、初筛
    /// </summary>
    Quick,
    
    /// <summary>
    /// 标准评审 (K=2, N=3)
    /// - 3 个 Worker 并行
    /// - 需要领先 2 票达成共识
    /// - 适合：一般论文评审
    /// </summary>
    Standard,
    
    /// <summary>
    /// 详细评审 (K=3, N=5)
    /// - 5 个 Worker 并行
    /// - 需要领先 3 票达成共识
    /// - 适合：重要论文、投稿前检查
    /// </summary>
    Detailed,
    
    /// <summary>
    /// 严格评审 (K=4, N=7)
    /// - 7 个 Worker 并行
    /// - 需要领先 4 票达成共识
    /// - 适合：顶会论文、高风险决策
    /// </summary>
    Rigorous,
    
    /// <summary>
    /// 关键评审 (K=5, N=9)
    /// - 9 个 Worker 并行
    /// - 需要领先 5 票达成共识
    /// - 适合：关键决策、需要最高可靠性
    /// </summary>
    Critical
}

/// <summary>
/// MAKER 参数配置。
/// </summary>
public static class MakerParameters
{
    /// <summary>
    /// 获取评审类型对应的 MAKER 参数。
    /// </summary>
    public static (int K, int N, string Description) GetParams(ReviewType type) => type switch
    {
        ReviewType.Quick => (1, 1, "Single shot, no voting"),
        ReviewType.Standard => (2, 3, "3 workers, 2-vote lead"),
        ReviewType.Detailed => (3, 5, "5 workers, 3-vote lead"),
        ReviewType.Rigorous => (4, 7, "7 workers, 4-vote lead"),
        ReviewType.Critical => (5, 9, "9 workers, 5-vote lead"),
        _ => (2, 3, "Default: Standard")
    };
}

/// <summary>
/// 论文评审会话。
/// </summary>
public sealed class ReviewSession
{
    /// <summary>会话ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    
    /// <summary>论文标题</summary>
    public string Title { get; set; } = "";
    
    /// <summary>论文作者</summary>
    public string Authors { get; set; } = "";
    
    /// <summary>评审类型</summary>
    public ReviewType Type { get; set; } = ReviewType.Standard;
    
    /// <summary>会议/期刊类型</summary>
    public string VenueType { get; set; } = "AI Conference";
    
    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    
    /// <summary>评审状态</summary>
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    
    /// <summary>论文内容</summary>
    public string PaperContent { get; set; } = "";
    
    /// <summary>上传ID</summary>
    public string? UploadId { get; set; }
    
    /// <summary>评审结果</summary>
    public ReasoningResult? Result { get; set; }
    
    /// <summary>错误信息</summary>
    public string? Error { get; set; }
    
    /// <summary>执行耗时</summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>LLM 调用次数</summary>
    public int TotalLlmCalls { get; set; }
    
    /// <summary>总 Token 数</summary>
    public long TotalTokens { get; set; }
    
    /// <summary>当前进度</summary>
    public int ProgressPercent { get; set; }
    
    /// <summary>当前阶段</summary>
    public string CurrentPhase { get; set; } = "";
    
    /// <summary>时间线</summary>
    public List<TimelineEntry> Timeline { get; } = [];
    
    /// <summary>SSE 事件通道</summary>
    public Channel<ReviewEvent> EventChannel { get; } = Channel.CreateUnbounded<ReviewEvent>();
    
    /// <summary>取消令牌源</summary>
    public CancellationTokenSource CancellationTokenSource { get; } = new();
    
    /// <summary>生成的文件</summary>
    public ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Files { get; } = new();
    
    /// <summary>输出目录</summary>
    public string? OutputDir { get; set; }
}

/// <summary>
/// 时间线条目。
/// </summary>
public record TimelineEntry(string Phase, string Message, DateTimeOffset Timestamp);

// ============================================================
//  REVIEW EVENTS (SSE 事件)
// ============================================================

/// <summary>
/// 评审事件基类。
/// </summary>
public abstract record ReviewEvent
{
    public string SessionId { get; init; } = "";
    public string Type => GetType().Name;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 进度事件。
/// </summary>
public record ProgressEvent : ReviewEvent
{
    public string Phase { get; init; } = "";
    public string? Message { get; init; }
    public int ProgressPercent { get; init; }
    public int? Depth { get; init; }
    
    // DSL 步骤信息
    public string? StepId { get; init; }
    public string? StepType { get; init; }
    public string? StepStatus { get; init; }
    
    // Atomic Point（可选）：用于 UI 绑定“论证点/子任务”树
    public string? PointId { get; init; }
    public string? PointTitle { get; init; }
    
    // Vote 相关
    public int VoteRound { get; init; }
    public int VoteMaxRounds { get; init; }
    public int VoteK { get; init; }
    public int VoteCurrentVotes { get; init; }
    
    // Parallel 相关
    public int ParallelTotal { get; init; }
    public int ParallelCompleted { get; init; }
    public int ParallelFailed { get; init; }
    
    // 统计信息
    public int TotalLlmCalls { get; init; }
    public long TotalTokens { get; init; }
}

/// <summary>
/// 评审员事件（Worker 提交评审意见）。
/// </summary>
public record ReviewerEvent : ReviewEvent
{
    public string ReviewerId { get; init; } = "";
    public string ReviewerRole { get; init; } = "";
    public string Content { get; init; } = "";
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
}

/// <summary>
/// 共识事件（投票结果）。
/// </summary>
public record ConsensusEvent : ReviewEvent
{
    public int Round { get; init; }
    public int TotalVotes { get; init; }
    public int VotesNeeded { get; init; }
    public int LeaderVotes { get; init; }
    public bool Reached { get; init; }
}

/// <summary>
/// 结果事件。
/// </summary>
public record ResultEvent : ReviewEvent
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int TotalLlmCalls { get; init; }
    public long TotalTokens { get; init; }
}

/// <summary>
/// 错误事件。
/// </summary>
public record ErrorEvent : ReviewEvent
{
    public string Message { get; init; } = "";
    public string? StackTrace { get; init; }
}

// ============================================================
//  MAKER FLOW EVENTS (MAKER 流程事件)
// ============================================================

/// <summary>
/// 任务分解事件 - 展示任务如何被拆解。
/// </summary>
public record TaskDecomposedEvent : ReviewEvent
{
    /// <summary>父任务 ID</summary>
    public string ParentTaskId { get; init; } = "";
    
    /// <summary>
    /// 父 atomic point（可选）。
    /// - 空表示根任务（整篇论文评审）
    /// - 非空表示某个 point 被进一步拆分
    /// </summary>
    public string? ParentPointId { get; init; }
    
    /// <summary>父 atomic point 标题/描述（可选）</summary>
    public string? ParentPointTitle { get; init; }
    
    /// <summary>子任务列表</summary>
    public List<SubTaskInfo> SubTasks { get; init; } = [];
    /// <summary>分解深度</summary>
    public int Depth { get; init; }
    /// <summary>分解原因</summary>
    public string Reason { get; init; } = "";
}

/// <summary>
/// Atomic Point 共识事件（每个子任务/论证点的最终共识结论）。
/// </summary>
public record AtomicPointConsensusEvent : ReviewEvent
{
    /// <summary>Point ID（建议使用可追踪的 StepId，例如 execute_subtasks[0]）</summary>
    public string PointId { get; init; } = "";
    /// <summary>Point 标题/描述（来自 decompose 的 subtask.description）</summary>
    public string? PointTitle { get; init; }
    /// <summary>产生共识的步骤（solve_atomic / compose 等）</summary>
    public string SourceStepId { get; init; } = "";
    /// <summary>递归深度（用于区分顶层/子任务）</summary>
    public int Depth { get; init; }
    /// <summary>共识是否成功（目前默认 true；失败场景可扩展）</summary>
    public bool Success { get; init; } = true;
    /// <summary>最终共识结论正文</summary>
    public string Conclusion { get; init; } = "";
}

/// <summary>
/// 子任务信息。
/// </summary>
public record SubTaskInfo
{
    public string TaskId { get; init; } = "";
    public string Description { get; init; } = "";
    public string Status { get; init; } = "pending";
}

/// <summary>
/// Worker 开始工作事件。
/// </summary>
public record WorkerStartedEvent : ReviewEvent
{
    /// <summary>Worker ID</summary>
    public string WorkerId { get; init; } = "";
    /// <summary>任务 ID</summary>
    public string TaskId { get; init; } = "";
    /// <summary>展示名称（友好名）</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>Worker 角色</summary>
    public string Role { get; init; } = "";
    /// <summary>LLM 提供商</summary>
    public string? ProviderName { get; init; }
    /// <summary>温度参数</summary>
    public float Temperature { get; init; }
    
    /// <summary>
    /// 当前 Worker 所在的 atomic point（可选）。
    /// 用于 UI 把对话绑定到 “论证点/子任务” 上。
    /// </summary>
    public string? PointId { get; init; }
    
    /// <summary>atomic point 标题/描述（可选）</summary>
    public string? PointTitle { get; init; }
}

/// <summary>
/// Worker 完成工作事件。
/// </summary>
public record WorkerCompletedEvent : ReviewEvent
{
    /// <summary>Worker ID</summary>
    public string WorkerId { get; init; } = "";
    /// <summary>任务 ID</summary>
    public string TaskId { get; init; } = "";
    /// <summary>展示名称（友好名）</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>是否成功</summary>
    public bool Success { get; init; }
    /// <summary>输出内容摘要</summary>
    public string ContentPreview { get; init; } = "";
    /// <summary>完整内容</summary>
    public string Content { get; init; } = "";
    /// <summary>延迟(ms)</summary>
    public long LatencyMs { get; init; }
    /// <summary>Token 使用</summary>
    public int TotalTokens { get; init; }
    
    /// <summary>atomic point（可选）</summary>
    public string? PointId { get; init; }
    
    /// <summary>atomic point 标题/描述（可选）</summary>
    public string? PointTitle { get; init; }
}

/// <summary>
/// 投票轮次事件。
/// </summary>
public record VotingRoundEvent : ReviewEvent
{
    /// <summary>任务 ID</summary>
    public string TaskId { get; init; } = "";
    /// <summary>投票类型</summary>
    public string VotingType { get; init; } = "";
    /// <summary>当前轮次</summary>
    public int Round { get; init; }
    /// <summary>候选方案列表</summary>
    public List<VoteCandidateInfo> Candidates { get; init; } = [];
    /// <summary>需要的票数 K</summary>
    public int VotesNeeded { get; init; }
    /// <summary>是否达成共识</summary>
    public bool ConsensusReached { get; init; }
}

/// <summary>
/// 投票候选信息。
/// </summary>
public record VoteCandidateInfo
{
    public string CandidateId { get; init; } = "";
    public string ContentPreview { get; init; } = "";
    public int Votes { get; init; }
    public bool IsLeader { get; init; }
}

/// <summary>
/// 任务合成事件。
/// </summary>
public record TaskComposedEvent : ReviewEvent
{
    /// <summary>任务 ID</summary>
    public string TaskId { get; init; } = "";
    /// <summary>子任务结果列表</summary>
    public List<string> SubTaskResults { get; init; } = [];
    /// <summary>合成后的结果摘要</summary>
    public string ComposedPreview { get; init; } = "";
    /// <summary>完整合成结果</summary>
    public string ComposedContent { get; init; } = "";
}

/// <summary>
/// 阶段变更事件。
/// </summary>
public record PhaseChangeEvent : ReviewEvent
{
    /// <summary>任务 ID</summary>
    public string TaskId { get; init; } = "";
    /// <summary>旧阶段</summary>
    public string OldPhase { get; init; } = "";
    /// <summary>新阶段</summary>
    public string NewPhase { get; init; } = "";
    /// <summary>消息</summary>
    public string Message { get; init; } = "";
}

// ============================================================
//  STAGE LOG EVENT (阶段日志事件)
//  每完成一个阶段，输出带时间戳的总结
// ============================================================

/// <summary>
/// 阶段完成日志事件 - 带时间戳的阶段总结。
/// </summary>
public record StageLogEvent : ReviewEvent
{
    /// <summary>阶段名称</summary>
    public string Stage { get; init; } = "";
    /// <summary>阶段状态 (started/completed/failed)</summary>
    public string Status { get; init; } = "";
    /// <summary>总结摘要</summary>
    public string Summary { get; init; } = "";
    /// <summary>详细统计</summary>
    public StageStats? Stats { get; init; }
    /// <summary>阶段耗时(ms)</summary>
    public long DurationMs { get; init; }
    /// <summary>开始时间</summary>
    public DateTimeOffset StartTime { get; init; }
    /// <summary>结束时间</summary>
    public DateTimeOffset EndTime { get; init; }
    /// <summary>阶段详情 (投票候选、Worker 输出等)</summary>
    public StageDetails? Details { get; init; }
}

/// <summary>
/// 阶段统计信息。
/// </summary>
public record StageStats
{
    /// <summary>LLM 调用次数</summary>
    public int LlmCalls { get; init; }
    /// <summary>Token 数</summary>
    public long Tokens { get; init; }
    /// <summary>Worker 数量</summary>
    public int WorkerCount { get; init; }
    /// <summary>投票轮次</summary>
    public int VotingRounds { get; init; }
    /// <summary>是否达成共识</summary>
    public bool ConsensusReached { get; init; }
    /// <summary>子任务数</summary>
    public int SubTaskCount { get; init; }
}

/// <summary>
/// 阶段详情 - 可点击查看。
/// </summary>
public record StageDetails
{
    /// <summary>投票候选方案列表</summary>
    public List<CandidateDetail>? Candidates { get; init; }
    /// <summary>Worker 输出列表</summary>
    public List<WorkerOutput>? WorkerOutputs { get; init; }
    /// <summary>分解后的子任务</summary>
    public List<string>? SubTasks { get; init; }
    /// <summary>合成后的结果</summary>
    public string? ComposedResult { get; init; }
    /// <summary>获胜方案内容</summary>
    public string? WinnerContent { get; init; }
    /// <summary>错误信息</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 投票候选方案详情。
/// </summary>
public record CandidateDetail
{
    /// <summary>候选 ID</summary>
    public string Id { get; init; } = "";
    /// <summary>内容摘要</summary>
    public string Preview { get; init; } = "";
    /// <summary>完整内容</summary>
    public string Content { get; init; } = "";
    /// <summary>得票数</summary>
    public int Votes { get; init; }
    /// <summary>是否获胜</summary>
    public bool IsWinner { get; init; }
    /// <summary>提供商</summary>
    public string? Provider { get; init; }
}

/// <summary>
/// Worker 输出详情。
/// </summary>
public record WorkerOutput
{
    /// <summary>Worker ID</summary>
    public string WorkerId { get; init; } = "";
    /// <summary>内容摘要</summary>
    public string Preview { get; init; } = "";
    /// <summary>完整内容</summary>
    public string Content { get; init; } = "";
    /// <summary>延迟(ms)</summary>
    public long LatencyMs { get; init; }
    /// <summary>Token 数</summary>
    public int Tokens { get; init; }
    /// <summary>是否成功</summary>
    public bool Success { get; init; }
}

// ============================================================
//  LLM TRACE EVENTS (LLM 调用追踪事件)
// ============================================================

/// <summary>
/// LLM 调用开始事件。
/// </summary>
public record LlmCallStartEvent : ReviewEvent
{
    /// <summary>调用 ID</summary>
    public string CallId { get; init; } = "";
    /// <summary>Worker ID</summary>
    public string WorkerId { get; init; } = "";
    /// <summary>展示名称（友好名）</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>LLM 提供商</summary>
    public string? ProviderName { get; init; }
    /// <summary>System Prompt</summary>
    public string? SystemPrompt { get; init; }
    /// <summary>User Prompt</summary>
    public string? UserPrompt { get; init; }
    /// <summary>调用阶段</summary>
    public string Phase { get; init; } = "";
    
    /// <summary>atomic point（可选）</summary>
    public string? PointId { get; init; }
    
    /// <summary>atomic point 标题/描述（可选）</summary>
    public string? PointTitle { get; init; }
}

/// <summary>
/// LLM Streaming Token 事件（实时推送）。
/// </summary>
public record LlmStreamingEvent : ReviewEvent
{
    /// <summary>调用 ID</summary>
    public string CallId { get; init; } = "";
    /// <summary>Worker ID</summary>
    public string WorkerId { get; init; } = "";
    /// <summary>展示名称（友好名）</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>当前 Token 块</summary>
    public string Token { get; init; } = "";
    /// <summary>累积内容</summary>
    public string AccumulatedContent { get; init; } = "";
    /// <summary>Token 序号</summary>
    public int TokenIndex { get; init; }
    /// <summary>是否首个 Token (TTFT)</summary>
    public bool IsFirstToken { get; init; }
    /// <summary>是否最后 Token</summary>
    public bool IsLastToken { get; init; }
    
    /// <summary>atomic point（可选）</summary>
    public string? PointId { get; init; }
    
    /// <summary>atomic point 标题/描述（可选）</summary>
    public string? PointTitle { get; init; }
}

/// <summary>
/// LLM 调用完成事件。
/// </summary>
public record LlmCallCompleteEvent : ReviewEvent
{
    /// <summary>调用 ID</summary>
    public string CallId { get; init; } = "";
    /// <summary>Worker ID</summary>
    public string WorkerId { get; init; } = "";
    /// <summary>展示名称（友好名）</summary>
    public string DisplayName { get; init; } = "";
    /// <summary>是否成功</summary>
    public bool Success { get; init; }
    /// <summary>完整响应内容</summary>
    public string Content { get; init; } = "";
    /// <summary>错误信息</summary>
    public string? Error { get; init; }
    /// <summary>Prompt Tokens</summary>
    public int PromptTokens { get; init; }
    /// <summary>Completion Tokens</summary>
    public int CompletionTokens { get; init; }
    /// <summary>总 Tokens</summary>
    public int TotalTokens => PromptTokens + CompletionTokens;
    /// <summary>延迟(ms)</summary>
    public long LatencyMs { get; init; }
    /// <summary>LLM 提供商</summary>
    public string? ProviderName { get; init; }
    /// <summary>调用阶段</summary>
    public string Phase { get; init; } = "";
    
    /// <summary>atomic point（可选）</summary>
    public string? PointId { get; init; }
    
    /// <summary>atomic point 标题/描述（可选）</summary>
    public string? PointTitle { get; init; }
}

// ============================================================
//  REQUEST MODELS
// ============================================================

/// <summary>
/// 创建会话请求。
/// </summary>
public record CreateSessionRequest
{
    public string? Title { get; init; }
    public string? Authors { get; init; }
    public string? ReviewType { get; init; }
    public string? VenueType { get; init; }
    public string? UploadId { get; init; }
    public string? PaperContent { get; init; }
    /// <summary>从老 session 复制论文内容</summary>
    public string? CopyFromSessionId { get; init; }
}
