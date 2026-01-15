namespace Aevatar.Agents.Cognitive.Content;

// ============================================================
//  CONTENT LOADER INTERFACE
//  内容加载器接口
// ============================================================

/// <summary>
/// 内容加载器接口。
/// 负责从各种来源加载内容。
/// </summary>
public interface IContentLoader
{
    /// <summary>
    /// 从内容来源加载内容。
    /// </summary>
    /// <param name="source">内容来源定义</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>加载后的内容</returns>
    Task<LoadedContent> LoadAsync(ContentSource source, CancellationToken ct = default);

    /// <summary>
    /// 验证内容来源是否有效。
    /// </summary>
    /// <param name="source">内容来源定义</param>
    /// <returns>验证结果</returns>
    ContentSourceValidation Validate(ContentSource source);

    /// <summary>
    /// 预览内容来源（不加载完整内容）。
    /// </summary>
    /// <param name="source">内容来源定义</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预览结果</returns>
    Task<ContentPreview> PreviewAsync(ContentSource source, CancellationToken ct = default);
}

/// <summary>
/// 内容来源验证结果。
/// </summary>
public sealed record ContentSourceValidation
{
    /// <summary>
    /// 是否有效。
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// 错误信息列表。
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// 警告信息列表。
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// 创建有效结果。
    /// </summary>
    public static ContentSourceValidation Valid() => new() { IsValid = true };

    /// <summary>
    /// 创建有效但带警告的结果。
    /// </summary>
    public static ContentSourceValidation ValidWithWarnings(params string[] warnings) => new()
    {
        IsValid = true,
        Warnings = warnings
    };

    /// <summary>
    /// 创建无效结果。
    /// </summary>
    public static ContentSourceValidation Invalid(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}

/// <summary>
/// 内容预览结果。
/// </summary>
public sealed record ContentPreview
{
    /// <summary>
    /// 预计文件数。
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// 文件列表（路径）。
    /// </summary>
    public IReadOnlyList<string> FilePaths { get; init; } = [];

    /// <summary>
    /// 预计总大小（字节）。
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// 预计 Token 数。
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// 是否超出限制。
    /// </summary>
    public bool ExceedsLimits { get; init; }

    /// <summary>
    /// 限制警告。
    /// </summary>
    public string? LimitWarning { get; init; }
}

