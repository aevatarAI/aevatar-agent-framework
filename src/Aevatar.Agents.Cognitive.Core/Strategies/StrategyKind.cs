namespace Aevatar.Agents.Cognitive.Core;

// ============================================================
//  STRATEGY KIND ENUMERATION
//  思维策略类型枚举
// ============================================================

/// <summary>
/// 思维策略类型。
/// 对应 Cognitive Mesh DSL 中的 strategy 字段。
/// </summary>
public enum StrategyKind
{
    /// <summary>
    /// Chain of Thought - 线性思维链。
    /// 简单的顺序推理，一步接一步。
    /// </summary>
    Cot,

    /// <summary>
    /// Tree of Thoughts - 思维树。
    /// 分支探索与剪枝，BFS/DFS 搜索。
    /// </summary>
    Tot,

    /// <summary>
    /// Graph of Thoughts - 思维图。
    /// 分支可重组，支持循环和合并。
    /// </summary>
    Got,

    /// <summary>
    /// Universe of Thoughts - Combinational - 组合式思维宇宙。
    /// 类比检索 + 思维合成，创意问题求解。
    /// </summary>
    UotCombinational,

    /// <summary>
    /// Universe of Thoughts - Exploratory - 探索式思维宇宙。
    /// 并行蒙特卡洛搜索，大规模假设探索。
    /// </summary>
    UotExploratory,

    /// <summary>
    /// Universe of Thoughts - Transformative - 变革式思维宇宙。
    /// 元提示重写，改变规则本身。
    /// </summary>
    UotTransformative,

    /// <summary>
    /// MAKER Strategy - 分解-共识-合成策略。
    /// 多 Agent 协作，通过投票达成共识。
    /// </summary>
    Maker,

    /// <summary>
    /// Direct Strategy - 直接调用 AI。
    /// 最简单的策略，直接传入 prompt 获取回复。
    /// </summary>
    Direct,

    /// <summary>
    /// Cognitive DSL Strategy - DSL 驱动的认知策略。
    /// 使用 YAML 定义工作流，Coordinator + Worker 真正并行。
    /// </summary>
    Cognitive
}

/// <summary>
/// StrategyKind 扩展方法。
/// </summary>
public static class StrategyKindExtensions
{
    /// <summary>
    /// 获取策略的人类可读名称。
    /// </summary>
    public static string GetDisplayName(this StrategyKind kind) => kind switch
    {
        StrategyKind.Cot => "Chain of Thought",
        StrategyKind.Tot => "Tree of Thoughts",
        StrategyKind.Got => "Graph of Thoughts",
        StrategyKind.UotCombinational => "UoT Combinational",
        StrategyKind.UotExploratory => "UoT Exploratory",
        StrategyKind.UotTransformative => "UoT Transformative",
        StrategyKind.Maker => "MAKER Consensus",
        StrategyKind.Direct => "Direct AI",
        StrategyKind.Cognitive => "Cognitive DSL",
        _ => kind.ToString()
    };

    /// <summary>
    /// 获取策略的简短描述。
    /// </summary>
    public static string GetDescription(this StrategyKind kind) => kind switch
    {
        StrategyKind.Cot => "线性顺序推理，一步接一步",
        StrategyKind.Tot => "分支探索与剪枝，BFS/DFS 搜索",
        StrategyKind.Got => "分支重组，支持循环和合并",
        StrategyKind.UotCombinational => "类比检索 + 思维合成，创意求解",
        StrategyKind.UotExploratory => "并行蒙特卡洛搜索，大规模探索",
        StrategyKind.UotTransformative => "元提示重写，改变规则本身",
        StrategyKind.Maker => "多 Agent 协作，投票达成共识",
        StrategyKind.Direct => "直接调用 AI，最简单快速",
        StrategyKind.Cognitive => "DSL 定义工作流，Actor 真正并行",
        _ => "Unknown strategy"
    };

    /// <summary>
    /// 是否为 UoT 系列策略。
    /// </summary>
    public static bool IsUoT(this StrategyKind kind) =>
        kind is StrategyKind.UotCombinational
            or StrategyKind.UotExploratory
            or StrategyKind.UotTransformative;
}

