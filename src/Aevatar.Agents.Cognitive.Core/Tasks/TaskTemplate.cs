namespace Aevatar.Agents.Cognitive.Core.Tasks;

// ============================================================
//  TASK TEMPLATE
//  任务模板枚举
// ============================================================

/// <summary>
/// 预定义的任务模板。
/// </summary>
public enum TaskTemplate
{
    /// <summary>
    /// 总结要点 - 提取核心观点和关键信息。
    /// 适用于：文章、报告、会议记录等。
    /// </summary>
    Summarize,

    /// <summary>
    /// 深度分析 - 结构化分析内容。
    /// 适用于：研究报告、市场分析、技术文档等。
    /// </summary>
    Analyze,

    /// <summary>
    /// 学术评审 - 论文/报告评审。
    /// 适用于：学术论文、技术报告等。
    /// </summary>
    Review,

    /// <summary>
    /// 批评评价 - 文学作品/创意评价。
    /// 适用于：小说、剧本、创意作品等。
    /// </summary>
    Critique,

    /// <summary>
    /// 改写优化 - 重写/优化内容。
    /// 适用于：任何需要改进的文本。
    /// </summary>
    Rewrite,

    /// <summary>
    /// 续写推演 - 续写故事/推演剧情。
    /// 适用于：小说、故事、剧本等。
    /// </summary>
    Continue,

    /// <summary>
    /// 信息提取 - 提取特定类型信息。
    /// 适用于：从文档中提取结构化数据。
    /// </summary>
    Extract,

    /// <summary>
    /// 对比分析 - 对比多个文档。
    /// 适用于：比较不同方案、观点、版本等。
    /// </summary>
    Compare,

    /// <summary>
    /// 问答 - 基于内容回答问题。
    /// 适用于：知识库问答、文档查询等。
    /// </summary>
    QA,

    /// <summary>
    /// 翻译 - 翻译内容。
    /// 适用于：多语言翻译。
    /// </summary>
    Translate,

    /// <summary>
    /// 代码审查 - 审查代码质量。
    /// 适用于：源代码文件。
    /// </summary>
    CodeReview,

    /// <summary>
    /// 大纲生成 - 生成内容大纲。
    /// 适用于：长文档的结构梳理。
    /// </summary>
    Outline,

    /// <summary>
    /// 自定义 - 用户自定义提示词。
    /// </summary>
    Custom
}

/// <summary>
/// TaskTemplate 扩展方法。
/// </summary>
public static class TaskTemplateExtensions
{
    /// <summary>
    /// 获取任务模板的显示名称。
    /// </summary>
    public static string GetDisplayName(this TaskTemplate template) => template switch
    {
        TaskTemplate.Summarize => "总结要点",
        TaskTemplate.Analyze => "深度分析",
        TaskTemplate.Review => "学术评审",
        TaskTemplate.Critique => "批评评价",
        TaskTemplate.Rewrite => "改写优化",
        TaskTemplate.Continue => "续写推演",
        TaskTemplate.Extract => "信息提取",
        TaskTemplate.Compare => "对比分析",
        TaskTemplate.QA => "问答",
        TaskTemplate.Translate => "翻译",
        TaskTemplate.CodeReview => "代码审查",
        TaskTemplate.Outline => "大纲生成",
        TaskTemplate.Custom => "自定义",
        _ => template.ToString()
    };

    /// <summary>
    /// 获取任务模板的描述。
    /// </summary>
    public static string GetDescription(this TaskTemplate template) => template switch
    {
        TaskTemplate.Summarize => "提取核心观点和关键信息，生成结构化总结",
        TaskTemplate.Analyze => "深入分析内容结构、论点、证据和逻辑",
        TaskTemplate.Review => "学术论文/报告评审，提供修改建议",
        TaskTemplate.Critique => "文学作品/创意评价，从多个维度评估",
        TaskTemplate.Rewrite => "重写/优化内容，提升表达质量",
        TaskTemplate.Continue => "续写故事/推演剧情，保持风格一致",
        TaskTemplate.Extract => "从文档中提取特定类型的结构化信息",
        TaskTemplate.Compare => "对比分析多个文档的异同",
        TaskTemplate.QA => "基于内容回答特定问题",
        TaskTemplate.Translate => "将内容翻译为目标语言",
        TaskTemplate.CodeReview => "审查代码质量、风格、潜在问题",
        TaskTemplate.Outline => "生成内容的结构化大纲",
        TaskTemplate.Custom => "使用自定义提示词处理内容",
        _ => "处理内容"
    };

    /// <summary>
    /// 获取推荐的默认策略。
    /// </summary>
    public static StrategyKind GetRecommendedStrategy(this TaskTemplate template) => template switch
    {
        TaskTemplate.Review => StrategyKind.Maker,      // 高可靠性需求
        TaskTemplate.Analyze => StrategyKind.Maker,     // 需要多角度分析
        TaskTemplate.Continue => StrategyKind.UotCombinational, // 需要创意
        TaskTemplate.Compare => StrategyKind.Maker,     // 需要系统性比较
        _ => StrategyKind.Maker                          // 默认使用 MAKER（可降级为 Simple）
    };

    /// <summary>
    /// 是否需要额外参数。
    /// </summary>
    public static bool RequiresAdditionalParameters(this TaskTemplate template) => template switch
    {
        TaskTemplate.QA => true,        // 需要 Question
        TaskTemplate.Translate => true, // 需要 TargetLanguage
        TaskTemplate.Extract => true,   // 需要 ExtractionTarget
        TaskTemplate.Custom => true,    // 需要 CustomInstruction
        _ => false
    };
}

