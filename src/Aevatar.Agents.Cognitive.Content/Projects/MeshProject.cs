using Aevatar.Agents.Cognitive.Core;
using Aevatar.Agents.Cognitive.Content;
using Aevatar.Agents.Cognitive.Core.Tasks;

namespace Aevatar.Agents.Cognitive.Content;

// ============================================================
//  MESH PROJECT
//  项目定义模型
// ============================================================

/// <summary>
/// 网格项目定义。
/// </summary>
public sealed class MeshProject : IMeshProject
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public required string Description { get; init; }

    /// <inheritdoc />
    public required string Icon { get; init; }

    /// <inheritdoc />
    public required StrategyKind Strategy { get; init; }

    /// <inheritdoc />
    public required string Task { get; init; }

    /// <summary>
    /// 预配置的选项。
    /// </summary>
    public required ReasoningOptions Options { get; init; }

    /// <summary>
    /// 内容来源（可选）。
    /// </summary>
    public ContentSource? ContentSource { get; init; }

    /// <summary>
    /// 任务定义（可选，如果提供则覆盖 Task）。
    /// </summary>
    public TaskDefinition? TaskDefinition { get; init; }

    /// <inheritdoc />
    public ReasoningOptions BuildOptions() => Options;
}

/// <summary>
/// 项目配置（从 JSON 创建）。
/// </summary>
public sealed class ProjectConfig
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Strategy { get; set; }
    public string? Task { get; set; }

    // ─────────────────────────────────────────────────────────
    //  内容来源配置
    // ─────────────────────────────────────────────────────────
    public ContentSourceConfig? Content { get; set; }

    // ─────────────────────────────────────────────────────────
    //  任务模板配置
    // ─────────────────────────────────────────────────────────
    public TaskConfig? TaskConfig { get; set; }

    // ─────────────────────────────────────────────────────────
    //  通用选项
    // ─────────────────────────────────────────────────────────
    public string? ProviderName { get; set; }
    public int? MaxLlmCalls { get; set; }
    public long? MaxTokens { get; set; }
    public int? MaxDurationMinutes { get; set; }

    // MAKER 选项
    public string? Reliability { get; set; }

    // UoT 选项
    public string? DomainHint { get; set; }
    public int? MaxAnalogies { get; set; }
    public int? MaxCandidates { get; set; }

    // E-UoT 选项
    public int? MaxOutsideThoughts { get; set; }
    public int? ExplorationDirections { get; set; }

    // T-UoT 选项
    public int? MaxRuleSets { get; set; }
    public float? MinRadicality { get; set; }

    // 上下文
    public Dictionary<string, string>? Context { get; set; }
}

/// <summary>
/// 内容来源配置（JSON 格式）。
/// </summary>
public sealed class ContentSourceConfig
{
    /// <summary>单个文件路径</summary>
    public string? FilePath { get; set; }

    /// <summary>文件夹路径</summary>
    public string? DirectoryPath { get; set; }

    /// <summary>多个文件路径</summary>
    public List<string>? FilePaths { get; set; }

    /// <summary>上传 ID</summary>
    public string? UploadId { get; set; }

    /// <summary>直接文本内容</summary>
    public string? DirectContent { get; set; }

    /// <summary>内容描述</summary>
    public string? ContentDescription { get; set; }

    /// <summary>文件扩展名过滤</summary>
    public List<string>? Extensions { get; set; }

    /// <summary>是否递归搜索</summary>
    public bool Recursive { get; set; } = false;

    /// <summary>
    /// 转换为 ContentSource。
    /// </summary>
    public ContentSource ToContentSource() => new()
    {
        FilePath = FilePath,
        DirectoryPath = DirectoryPath,
        FilePaths = FilePaths,
        UploadId = UploadId,
        DirectContent = DirectContent,
        ContentDescription = ContentDescription,
        Extensions = Extensions ?? [".md", ".txt", ".markdown"],
        Recursive = Recursive
    };
}

/// <summary>
/// 任务配置（JSON 格式）。
/// </summary>
public sealed class TaskConfig
{
    /// <summary>任务模板</summary>
    public string? Template { get; set; }

    /// <summary>自定义指令</summary>
    public string? CustomInstruction { get; set; }

    /// <summary>详细程度 (0-1)</summary>
    public float? DetailLevel { get; set; }

    /// <summary>是否包含原文引用</summary>
    public bool? IncludeQuotes { get; set; }

    /// <summary>目标语言（翻译任务）</summary>
    public string? TargetLanguage { get; set; }

    /// <summary>问题（QA任务）</summary>
    public string? Question { get; set; }

    /// <summary>提取目标（Extract任务）</summary>
    public string? ExtractionTarget { get; set; }

    /// <summary>续写提示（Continue任务）</summary>
    public string? ContinuationHint { get; set; }

    /// <summary>输出格式</summary>
    public string? OutputFormat { get; set; }

    /// <summary>
    /// 转换为 TaskDefinition。
    /// </summary>
    public TaskDefinition ToTaskDefinition()
    {
        var template = Enum.TryParse<TaskTemplate>(Template, true, out var t)
            ? t
            : TaskTemplate.Custom;

        var outputFormat = Enum.TryParse<OutputFormat>(OutputFormat, true, out var f)
            ? f
            : Aevatar.Agents.Cognitive.Core.Tasks.OutputFormat.Markdown;

        return new TaskDefinition
        {
            Template = template,
            CustomInstruction = CustomInstruction,
            Parameters = new TaskParameters
            {
                DetailLevel = DetailLevel ?? 0.5f,
                IncludeQuotes = IncludeQuotes ?? true
            },
            OutputFormat = outputFormat,
            TargetLanguage = TargetLanguage,
            Question = Question,
            ExtractionTarget = ExtractionTarget,
            ContinuationHint = ContinuationHint
        };
    }
}

