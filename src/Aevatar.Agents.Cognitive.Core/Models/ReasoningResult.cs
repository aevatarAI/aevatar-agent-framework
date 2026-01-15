namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  REASONING RESULT
//  统一的推理结果模型
// ============================================================

/// <summary>
/// 推理结果。
/// 所有策略执行完成后返回的统一结果格式。
/// </summary>
public sealed record ReasoningResult
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 最终输出内容。
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// 错误信息（如果失败）。
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 执行时长。
    /// </summary>
    public TimeSpan Duration { get; init; }

    // ─────────────────────────────────────────────────────────
    //  Token 统计
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 总 LLM 调用次数。
    /// </summary>
    public int TotalLlmCalls { get; init; }

    /// <summary>
    /// Prompt Token 总数。
    /// </summary>
    public long PromptTokens { get; init; }

    /// <summary>
    /// Completion Token 总数。
    /// </summary>
    public long CompletionTokens { get; init; }

    /// <summary>
    /// Token 总数。
    /// </summary>
    public long TotalTokens => PromptTokens + CompletionTokens;

    // ─────────────────────────────────────────────────────────
    //  策略特定结果
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// MAKER: 执行追踪。
    /// </summary>
    public MakerTrace? MakerTrace { get; init; }

    /// <summary>
    /// UoT: 所有候选方案（已排序）。
    /// </summary>
    public IReadOnlyList<CandidateSolution>? UotCandidates { get; init; }

    /// <summary>
    /// UoT: 最佳方案。
    /// </summary>
    public CandidateSolution? UotBestSolution { get; init; }

    // ─────────────────────────────────────────────────────────
    //  E-UoT 特有结果
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// E-UoT: 发现的外部思想数量。
    /// </summary>
    public int? OutsideThoughtsDiscovered { get; init; }

    // ─────────────────────────────────────────────────────────
    //  T-UoT 特有结果
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// T-UoT: 暴露的显式规则数量。
    /// </summary>
    public int? RulesExposed { get; init; }

    /// <summary>
    /// T-UoT: 发现的隐藏假设数量。
    /// </summary>
    public int? HiddenAssumptionsFound { get; init; }

    /// <summary>
    /// T-UoT: 探索的规则集数量。
    /// </summary>
    public int? RuleSetsExplored { get; init; }

    /// <summary>
    /// T-UoT: 发现的隐藏假设内容（关键洞察）。
    /// </summary>
    public IReadOnlyList<string>? HiddenAssumptions { get; init; }

    // ─────────────────────────────────────────────────────────
    //  工厂方法
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 创建成功结果。
    /// </summary>
    public static ReasoningResult Succeeded(string content, TimeSpan duration, int llmCalls, long promptTokens, long completionTokens) => new()
    {
        Success = true,
        Content = content,
        Duration = duration,
        TotalLlmCalls = llmCalls,
        PromptTokens = promptTokens,
        CompletionTokens = completionTokens
    };

    /// <summary>
    /// 创建失败结果。
    /// </summary>
    public static ReasoningResult Failed(string error, TimeSpan duration = default) => new()
    {
        Success = false,
        Error = error,
        Duration = duration
    };
}

/// <summary>
/// MAKER 执行追踪。
/// </summary>
public sealed record MakerTrace
{
    /// <summary>根任务</summary>
    public TaskNode? RootTask { get; init; }

    /// <summary>最大递归深度</summary>
    public int MaxDepthReached { get; init; }

    /// <summary>总任务数</summary>
    public int TotalTasks { get; init; }

    /// <summary>原子任务数</summary>
    public int AtomicTasks { get; init; }
}

/// <summary>
/// MAKER 任务节点。
/// </summary>
public sealed record TaskNode
{
    /// <summary>任务 ID</summary>
    public required string TaskId { get; init; }

    /// <summary>任务描述</summary>
    public string? Description { get; init; }

    /// <summary>是否为原子任务</summary>
    public bool IsAtomic { get; init; }

    /// <summary>执行结果</summary>
    public string? Result { get; init; }

    /// <summary>子任务</summary>
    public IReadOnlyList<TaskNode> Children { get; init; } = [];

    /// <summary>投票会话</summary>
    public IReadOnlyList<VotingSession> VotingSessions { get; init; } = [];
}

/// <summary>
/// 投票会话。
/// </summary>
public sealed record VotingSession
{
    /// <summary>类型（Decomposition / Solution）</summary>
    public required string Type { get; init; }

    /// <summary>轮次</summary>
    public int Rounds { get; init; }

    /// <summary>获胜者</summary>
    public VotingCandidate? Winner { get; init; }

    /// <summary>所有候选</summary>
    public IReadOnlyList<VotingCandidate> Candidates { get; init; } = [];
}

/// <summary>
/// 投票候选。
/// </summary>
public sealed record VotingCandidate
{
    /// <summary>内容哈希</summary>
    public required string Hash { get; init; }

    /// <summary>票数</summary>
    public int Votes { get; init; }

    /// <summary>内容</summary>
    public string? Content { get; init; }
}

/// <summary>
/// UoT 候选方案。
/// </summary>
public sealed record CandidateSolution
{
    /// <summary>方案 ID</summary>
    public required string Id { get; init; }

    /// <summary>方案内容</summary>
    public required string Content { get; init; }

    /// <summary>评分</summary>
    public SolutionScore? Score { get; init; }

    /// <summary>来源类比</summary>
    public IReadOnlyList<string>? SourceAnalogies { get; init; }

    // ─────────────────────────────────────────────────────────
    //  T-UoT 特有字段
    // ─────────────────────────────────────────────────────────

    /// <summary>T-UoT: 违反的惯例列表</summary>
    public IReadOnlyList<string>? ViolatedConventions { get; init; }

    /// <summary>T-UoT: 源规则集 ID</summary>
    public string? RuleSetId { get; init; }
}

/// <summary>
/// 方案评分。
/// </summary>
public sealed record SolutionScore
{
    /// <summary>可行性 (0-1)</summary>
    public float Feasibility { get; init; }

    /// <summary>效用性 (0-1)</summary>
    public float Utility { get; init; }

    /// <summary>新颖性 (0-1)</summary>
    public float Novelty { get; init; }

    /// <summary>综合得分</summary>
    public float Composite { get; init; }
}

