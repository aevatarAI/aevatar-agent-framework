namespace Aevatar.Agents.Cognitive.Content;

// ============================================================
//  CONTENT SOURCE
//  内容来源 - 描述要加载的内容
// ============================================================

/// <summary>
/// 内容来源定义。
/// 支持单文件、多文件、文件夹、直接文本等多种来源。
/// </summary>
public sealed record ContentSource
{
    /// <summary>
    /// 单个文件路径。
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// 文件夹路径。
    /// 配合 Extensions 和 Recursive 使用。
    /// </summary>
    public string? DirectoryPath { get; init; }

    /// <summary>
    /// 多个文件路径。
    /// </summary>
    public IReadOnlyList<string>? FilePaths { get; init; }

    /// <summary>
    /// 上传文件的 ID（对应上传接口返回的 uploadId）。
    /// </summary>
    public string? UploadId { get; init; }

    /// <summary>
    /// 文件扩展名过滤（仅文件夹模式）。
    /// 默认支持 .md 和 .txt。
    /// </summary>
    public IReadOnlyList<string> Extensions { get; init; } = [".md", ".txt", ".markdown"];

    /// <summary>
    /// 是否递归搜索子目录（仅文件夹模式）。
    /// </summary>
    public bool Recursive { get; init; } = false;

    /// <summary>
    /// 直接传入的文本内容（无需文件）。
    /// </summary>
    public string? DirectContent { get; init; }

    /// <summary>
    /// 内容描述（用于提示词，如"这是一篇学术论文"）。
    /// </summary>
    public string? ContentDescription { get; init; }

    /// <summary>
    /// 最大文件数限制（防止加载过多文件）。
    /// </summary>
    public int MaxFiles { get; init; } = 100;

    /// <summary>
    /// 单个文件最大大小（字节）。
    /// 默认 10MB。
    /// </summary>
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;

    // ─────────────────────────────────────────────────────────
    //  工厂方法
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 从单个文件创建。
    /// </summary>
    public static ContentSource FromFile(string path, string? description = null) => new()
    {
        FilePath = path,
        ContentDescription = description
    };

    /// <summary>
    /// 从多个文件创建。
    /// </summary>
    public static ContentSource FromFiles(IEnumerable<string> paths, string? description = null) => new()
    {
        FilePaths = paths.ToList(),
        ContentDescription = description
    };

    /// <summary>
    /// 从文件夹创建。
    /// </summary>
    public static ContentSource FromDirectory(
        string path,
        bool recursive = false,
        IEnumerable<string>? extensions = null,
        string? description = null) => new()
    {
        DirectoryPath = path,
        Recursive = recursive,
        Extensions = extensions?.ToList() ?? [".md", ".txt", ".markdown"],
        ContentDescription = description
    };

    /// <summary>
    /// 从直接文本创建。
    /// </summary>
    public static ContentSource FromText(string content, string? description = null) => new()
    {
        DirectContent = content,
        ContentDescription = description
    };

    /// <summary>
    /// 从上传 ID 创建。
    /// </summary>
    public static ContentSource FromUpload(string uploadId, string? description = null) => new()
    {
        UploadId = uploadId,
        ContentDescription = description
    };
}

