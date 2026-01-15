using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  REASONING OPTIONS
//  推理执行选项（策略无关的通用配置）
// ============================================================

/// <summary>
/// 推理执行选项。
/// 包含所有策略通用的配置，以及策略特定配置的容器。
/// </summary>
public sealed record ReasoningOptions
{
    // ─────────────────────────────────────────────────────────
    //  通用配置
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// LLM 提供商名称（如 "deepseek", "claude", "openai"）。
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// 最大 LLM 调用次数（预算控制）。
    /// </summary>
    public int MaxLlmCalls { get; init; } = 500;

    /// <summary>
    /// 最大 Token 消耗（预算控制）。
    /// </summary>
    public long MaxTokens { get; init; } = 2_000_000;

    /// <summary>
    /// 最大执行时长。
    /// </summary>
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 单步超时时间。
    /// </summary>
    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 上下文键值对（传递给策略的额外信息）。
    /// </summary>
    public IReadOnlyDictionary<string, string>? Context { get; init; }

    // ─────────────────────────────────────────────────────────
    //  DIRECT 策略特定配置
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// DIRECT: 自定义系统提示词。
    /// </summary>
    public string? DirectSystemPrompt { get; init; }

    // ─────────────────────────────────────────────────────────
    //  MAKER 策略特定配置
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// MAKER: 可靠性级别。
    /// </summary>
    public MakerReliability MakerReliability { get; init; } = MakerReliability.Medium;

    /// <summary>
    /// MAKER: 共识阈值 K（领先票数需超过 K）。
    /// </summary>
    public int MakerConsensusK { get; init; } = 2;

    /// <summary>
    /// MAKER: 每轮采样数。
    /// </summary>
    public int MakerSamplesPerRound { get; init; } = 5;

    /// <summary>
    /// MAKER: 是否使用多 LLM 提供商。
    /// </summary>
    public bool MakerUseMultipleProviders { get; init; }

    /// <summary>
    /// MAKER: 执行模式。
    /// </summary>
    public MakerExecutionMode MakerExecutionMode { get; init; } = MakerExecutionMode.Production;

    // ─────────────────────────────────────────────────────────
    //  UoT 通用配置 (C-UoT, E-UoT, T-UoT)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// UoT: 领域提示（帮助类比检索）。
    /// </summary>
    public string? UotDomainHint { get; init; }

    /// <summary>
    /// UoT: 最大类比数量。
    /// </summary>
    public int UotMaxAnalogies { get; init; } = 5;

    /// <summary>
    /// UoT: 最大候选方案数。
    /// </summary>
    public int UotMaxCandidates { get; init; } = 10;

    /// <summary>
    /// UoT: 可行性阈值（0-1，低于此值的方案被过滤）。
    /// </summary>
    public float UotFeasibilityThreshold { get; init; } = 0.6f;

    /// <summary>
    /// UoT: 效用权重（评分时）。
    /// </summary>
    public float UotUtilityWeight { get; init; } = 0.5f;

    /// <summary>
    /// UoT: 新颖性权重（评分时）。
    /// </summary>
    public float UotNoveltyWeight { get; init; } = 0.5f;

    // ─────────────────────────────────────────────────────────
    //  E-UoT 特定配置 (探索式)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// E-UoT: 最大外部思想数量。
    /// 外部思想是从未探索领域发现的新概念。
    /// </summary>
    public int EUotMaxOutsideThoughts { get; init; } = 10;

    /// <summary>
    /// E-UoT: 探索方向数量。
    /// 系统会在多个方向上搜索外部思想。
    /// </summary>
    public int EUotExplorationDirections { get; init; } = 3;

    /// <summary>
    /// E-UoT: 外部思想最低相关性。
    /// </summary>
    public float EUotOutsideThoughtRelevance { get; init; } = 0.4f;

    // ─────────────────────────────────────────────────────────
    //  T-UoT 特定配置 (变革式)
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// T-UoT: 最大规则集数量。
    /// 每个规则集代表一个被挑战后的新规则空间。
    /// </summary>
    public int TUotMaxRuleSets { get; init; } = 3;

    /// <summary>
    /// T-UoT: 每个规则集的突变数。
    /// </summary>
    public int TUotMutationsPerSet { get; init; } = 3;

    /// <summary>
    /// T-UoT: 最低激进度。
    /// 只有足够激进的方案才会被保留。
    /// </summary>
    public float TUotMinRadicality { get; init; } = 0.5f;

    /// <summary>
    /// T-UoT: 是否允许违反物理规则。
    /// 通常为 false，除非是纯理论探索。
    /// </summary>
    public bool TUotAllowPhysicalViolation { get; init; } = false;

    // ─────────────────────────────────────────────────────────
    //  COGNITIVE DSL 策略特定配置
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Cognitive: 工作流名称（如 "direct", "maker-v2", "uot-combinational-v2"）。
    /// </summary>
    public string? CognitiveWorkflow { get; init; }

    /// <summary>
    /// Cognitive: Worker 数量（并行执行的 Worker Agent 数）。
    /// </summary>
    public int? CognitiveWorkerCount { get; init; }

    /// <summary>
    /// Cognitive: 共识阈值 K（投票时领先票数需超过 K）。
    /// </summary>
    public int? CognitiveConsensusK { get; init; }

    /// <summary>
    /// Cognitive: 最大投票轮数。
    /// </summary>
    public int? CognitiveMaxRounds { get; init; }

    /// <summary>
    /// Cognitive: 递归工作流最大深度。
    /// </summary>
    public int? CognitiveMaxDepth { get; init; }

    /// <summary>
    /// Cognitive: 语义相似度阈值（用于投票聚类）。
    /// </summary>
    public float? CognitiveSemanticSimilarity { get; init; }

    /// <summary>
    /// Cognitive: 执行超时时间（分钟）。
    /// </summary>
    public int? CognitiveTimeoutMinutes { get; init; }

    // ─────────────────────────────────────────────────────────
    //  工厂方法
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 创建默认 MAKER 策略选项。
    /// </summary>
    public static ReasoningOptions ForMaker(
        MakerReliability reliability = MakerReliability.Medium,
        int maxLlmCalls = 500,
        long maxTokens = 2_000_000,
        string providerName = AevatarAgentsConstants.DefaultProviderName) => new()
    {
        ProviderName = providerName,
        MakerReliability = reliability,
        MakerConsensusK = reliability switch
        {
            MakerReliability.Low => 1,
            MakerReliability.Medium => 2,
            MakerReliability.High => 3,
            MakerReliability.Critical => 4,
            _ => 2
        },
        MaxLlmCalls = maxLlmCalls,
        MaxTokens = maxTokens
    };

    /// <summary>
    /// 创建默认 UoT 组合式策略选项 (C-UoT)。
    /// 重组已有解决方案中的思想单元。
    /// </summary>
    public static ReasoningOptions ForUotCombinational(
        string? domainHint = null,
        int maxAnalogies = 5,
        int maxCandidates = 10,
        string providerName = AevatarAgentsConstants.DefaultProviderName) => new()
    {
        ProviderName = providerName,
        UotDomainHint = domainHint,
        UotMaxAnalogies = maxAnalogies,
        UotMaxCandidates = maxCandidates
    };

    /// <summary>
    /// 创建默认 UoT 探索式策略选项 (E-UoT)。
    /// 在 C-UoT 基础上探索域外思想。
    /// </summary>
    public static ReasoningOptions ForUotExploratory(
        string? domainHint = null,
        int maxOutsideThoughts = 10,
        int explorationDirections = 3,
        int maxAnalogies = 5,
        int maxCandidates = 10,
        string providerName = AevatarAgentsConstants.DefaultProviderName) => new()
    {
        ProviderName = providerName,
        UotDomainHint = domainHint,
        UotMaxAnalogies = maxAnalogies,
        UotMaxCandidates = maxCandidates,
        EUotMaxOutsideThoughts = maxOutsideThoughts,
        EUotExplorationDirections = explorationDirections
    };

    /// <summary>
    /// 创建默认 UoT 变革式策略选项 (T-UoT)。
    /// 挑战规则本身，暴露隐藏假设，探索新规则空间。
    /// </summary>
    public static ReasoningOptions ForUotTransformative(
        string? domainHint = null,
        int maxRuleSets = 3,
        int mutationsPerSet = 3,
        float minRadicality = 0.5f,
        string providerName = AevatarAgentsConstants.DefaultProviderName) => new()
    {
        ProviderName = providerName,
        UotDomainHint = domainHint,
        TUotMaxRuleSets = maxRuleSets,
        TUotMutationsPerSet = mutationsPerSet,
        TUotMinRadicality = minRadicality,
        UotFeasibilityThreshold = 0.5f  // T-UoT 降低可行性门槛
    };

    /// <summary>
    /// 创建默认 Cognitive DSL 策略选项。
    /// 使用 YAML 定义工作流，Coordinator + Worker 真正并行。
    /// </summary>
    public static ReasoningOptions ForCognitive(
        string workflow = "direct",
        int workerCount = 5,
        int consensusK = 2,
        int maxRounds = 10,
        int maxDepth = 10,
        float semanticSimilarity = 0.85f,
        int timeoutMinutes = 30,
        string providerName = AevatarAgentsConstants.DefaultProviderName) => new()
    {
        ProviderName = providerName,
        CognitiveWorkflow = workflow,
        CognitiveWorkerCount = workerCount,
        CognitiveConsensusK = consensusK,
        CognitiveMaxRounds = maxRounds,
        CognitiveMaxDepth = maxDepth,
        CognitiveSemanticSimilarity = semanticSimilarity,
        CognitiveTimeoutMinutes = timeoutMinutes
    };
}

/// <summary>
/// MAKER 可靠性级别。
/// </summary>
public enum MakerReliability
{
    /// <summary>低可靠性 (K=1)</summary>
    Low,

    /// <summary>中等可靠性 (K=2)</summary>
    Medium,

    /// <summary>高可靠性 (K=3)</summary>
    High,

    /// <summary>关键任务 (K=4)</summary>
    Critical
}

/// <summary>
/// MAKER 执行模式。
/// </summary>
public enum MakerExecutionMode
{
    /// <summary>生产模式（省钱）</summary>
    Production,

    /// <summary>学术模式（论文复现）</summary>
    Academic
}

