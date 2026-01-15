namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  REASONING STRATEGY ABSTRACTION
//  认知网格的核心抽象：可插拔的思维策略
// ============================================================

/// <summary>
/// 推理策略接口。
/// 每种思维策略（MAKER、UoT、ToT 等）实现此接口，
/// 使 CognitiveMesh 能够以统一方式调度不同的认知过程。
/// </summary>
public interface IReasoningStrategy
{
    /// <summary>
    /// 策略类型标识。
    /// </summary>
    StrategyKind Kind { get; }

    /// <summary>
    /// 人类可读的策略名称。
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 策略描述。
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 执行推理任务。
    /// </summary>
    /// <param name="problem">问题描述</param>
    /// <param name="options">执行选项</param>
    /// <param name="progress">进度报告器</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>推理结果</returns>
    Task<ReasoningResult> ExecuteAsync(
        string problem,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// 验证选项是否有效。
    /// </summary>
    ValidationResult ValidateOptions(ReasoningOptions options);
}

