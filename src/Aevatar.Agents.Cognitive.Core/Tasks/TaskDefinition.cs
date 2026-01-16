namespace Aevatar.Agents.Cognitive.Core.Tasks;

// ============================================================
//  TASK DEFINITION
//  任务定义
// ============================================================

/// <summary>
/// 完整的任务定义。
/// 描述要对内容执行的操作。
/// </summary>
public sealed record TaskDefinition
{
    /// <summary>
    /// 任务模板。
    /// </summary>
    public TaskTemplate Template { get; init; } = TaskTemplate.Custom;

    /// <summary>
    /// 自定义指令（覆盖或补充模板）。
    /// </summary>
    public string? CustomInstruction { get; init; }

    /// <summary>
    /// 任务特定参数。
    /// </summary>
    public TaskParameters Parameters { get; init; } = new();

    /// <summary>
    /// 输出格式要求。
    /// </summary>
    public OutputFormat OutputFormat { get; init; } = OutputFormat.Markdown;

    /// <summary>
    /// 目标语言（翻译任务）。
    /// </summary>
    public string? TargetLanguage { get; init; }

    /// <summary>
    /// 要回答的问题（QA 任务）。
    /// </summary>
    public string? Question { get; init; }

    /// <summary>
    /// 提取目标（Extract 任务）。
    /// </summary>
    public string? ExtractionTarget { get; init; }

    /// <summary>
    /// 续写长度提示（Continue 任务）。
    /// </summary>
    public string? ContinuationHint { get; init; }

    // ─────────────────────────────────────────────────────────
    //  工厂方法
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 创建总结任务。
    /// </summary>
    public static TaskDefinition Summarize(float detailLevel = 0.5f) => new()
    {
        Template = TaskTemplate.Summarize,
        Parameters = new TaskParameters { DetailLevel = detailLevel }
    };

    /// <summary>
    /// 创建评审任务。
    /// </summary>
    public static TaskDefinition Review(bool includeQuotes = true) => new()
    {
        Template = TaskTemplate.Review,
        Parameters = new TaskParameters { IncludeQuotes = includeQuotes, DetailLevel = 0.9f }
    };

    /// <summary>
    /// 创建批评评价任务。
    /// </summary>
    public static TaskDefinition Critique() => new()
    {
        Template = TaskTemplate.Critique,
        Parameters = new TaskParameters { DetailLevel = 0.7f }
    };

    /// <summary>
    /// 创建续写任务。
    /// </summary>
    public static TaskDefinition Continue(string? hint = null) => new()
    {
        Template = TaskTemplate.Continue,
        ContinuationHint = hint
    };

    /// <summary>
    /// 创建问答任务。
    /// </summary>
    public static TaskDefinition QA(string question) => new()
    {
        Template = TaskTemplate.QA,
        Question = question
    };

    /// <summary>
    /// 创建翻译任务。
    /// </summary>
    public static TaskDefinition Translate(string targetLanguage) => new()
    {
        Template = TaskTemplate.Translate,
        TargetLanguage = targetLanguage
    };

    /// <summary>
    /// 创建提取任务。
    /// </summary>
    public static TaskDefinition Extract(string target) => new()
    {
        Template = TaskTemplate.Extract,
        ExtractionTarget = target
    };

    /// <summary>
    /// 创建自定义任务。
    /// </summary>
    public static TaskDefinition Custom(string instruction) => new()
    {
        Template = TaskTemplate.Custom,
        CustomInstruction = instruction
    };
}

/// <summary>
/// 任务参数。
/// </summary>
public sealed record TaskParameters
{
    /// <summary>
    /// 最大输出长度（Token 数）。
    /// null 表示不限制。
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// 详细程度 (0-1)。
    /// 0 = 极简摘要，1 = 详尽分析。
    /// </summary>
    public float DetailLevel { get; init; } = 0.5f;

    /// <summary>
    /// 是否保留原文引用。
    /// </summary>
    public bool IncludeQuotes { get; init; } = true;

    /// <summary>
    /// 是否分章节/文件处理。
    /// 适用于长文档。
    /// </summary>
    public bool ProcessBySection { get; init; } = false;

    /// <summary>
    /// 是否生成结构化输出（JSON）。
    /// </summary>
    public bool StructuredOutput { get; init; } = false;

    /// <summary>
    /// 重点关注的方面（可选）。
    /// </summary>
    public string? FocusAreas { get; init; }
}

/// <summary>
/// 输出格式。
/// </summary>
public enum OutputFormat
{
    /// <summary>Markdown 格式</summary>
    Markdown,

    /// <summary>纯文本</summary>
    PlainText,

    /// <summary>JSON 结构化</summary>
    JSON,

    /// <summary>HTML</summary>
    HTML
}

