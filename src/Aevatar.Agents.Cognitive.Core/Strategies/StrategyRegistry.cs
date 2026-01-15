using Aevatar.Agents.Cognitive.Core.Strategies;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  STRATEGY REGISTRY
//  策略注册表 - 管理所有可用的思维策略
// ============================================================

/// <summary>
/// 策略注册表。
/// 管理所有可用的 IReasoningStrategy 实现。
/// </summary>
public sealed class StrategyRegistry
{
    private readonly Dictionary<StrategyKind, IReasoningStrategy> _strategies = new();
    private readonly ILogger<StrategyRegistry> _logger;

    public StrategyRegistry(
        DirectStrategy direct,
        MakerStrategy maker,
        UoTStrategy uot,
        EUoTStrategy euot,
        TUoTStrategy tuot,
        CognitiveStrategy cognitive,
        ILogger<StrategyRegistry> logger)
    {
        _logger = logger;

        // 注册策略
        Register(direct);    // Direct: 最简单的直接调用
        Register(maker);     // MAKER: 多 Agent 共识
        Register(uot);       // C-UoT: 组合式
        Register(euot);      // E-UoT: 探索式
        Register(tuot);      // T-UoT: 变革式
        Register(cognitive); // Cognitive DSL: 工作流驱动

        _logger.LogInformation("StrategyRegistry initialized with {Count} strategies: {Strategies}",
            _strategies.Count,
            string.Join(", ", _strategies.Keys));
    }

    private void Register(IReasoningStrategy strategy)
    {
        _strategies[strategy.Kind] = strategy;
    }

    /// <summary>
    /// 获取指定类型的策略。
    /// </summary>
    public IReasoningStrategy? Get(StrategyKind kind) =>
        _strategies.GetValueOrDefault(kind);

    /// <summary>
    /// 获取所有策略。
    /// </summary>
    public IEnumerable<IReasoningStrategy> GetAll() => _strategies.Values;

    /// <summary>
    /// 检查策略是否可用。
    /// </summary>
    public bool IsAvailable(StrategyKind kind) => _strategies.ContainsKey(kind);
}


