namespace Aevatar.Agents.Cognitive.Content;

// ============================================================
//  LOADED CONTENT
//  加载后的内容
// ============================================================

/// <summary>
/// 加载后的内容结果。
/// </summary>
public sealed record LoadedContent
{
    /// <summary>
    /// 合并后的全文。
    /// </summary>
    public required string CombinedText { get; init; }

    /// <summary>
    /// 各文件的详细信息。
    /// </summary>
    public IReadOnlyList<ContentFile> Files { get; init; } = [];

    /// <summary>
    /// 估算的 Token 数（按 4 字符 = 1 token 估算）。
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// 总字符数。
    /// </summary>
    public int TotalCharacters { get; init; }

    /// <summary>
    /// 内容类型描述。
    /// </summary>
    public string ContentType { get; init; } = "text";

    /// <summary>
    /// 内容来源描述（用于提示词）。
    /// </summary>
    public string? SourceDescription { get; init; }

    /// <summary>
    /// 加载时间。
    /// </summary>
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 是否为空内容。
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(CombinedText);

    /// <summary>
    /// 文件数量。
    /// </summary>
    public int FileCount => Files.Count;
}

/// <summary>
/// 单个内容文件。
/// </summary>
public sealed record ContentFile
{
    /// <summary>
    /// 文件完整路径。
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 文件名（不含路径）。
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// 文件内容。
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 估算的 Token 数。
    /// </summary>
    public int EstimatedTokens { get; init; }

    /// <summary>
    /// 字符数。
    /// </summary>
    public int CharacterCount { get; init; }

    /// <summary>
    /// 文件大小（字节）。
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// 文件扩展名。
    /// </summary>
    public string Extension { get; init; } = "";

    /// <summary>
    /// 文件最后修改时间。
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }
}

