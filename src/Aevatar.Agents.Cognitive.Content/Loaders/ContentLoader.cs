using Aevatar.Agents.Cognitive.Content;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Content;

// ============================================================
//  CONTENT LOADER
//  内容加载器实现
// ============================================================

/// <summary>
/// 内容加载器实现。
/// 支持从文件、文件夹、直接文本等来源加载内容。
/// </summary>
public sealed class ContentLoader : IContentLoader
{
    private readonly ILogger<ContentLoader> _logger;
    private readonly string _uploadsBasePath;

    public ContentLoader(ILogger<ContentLoader> logger)
    {
        _logger = logger;
        _uploadsBasePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
    }

    /// <inheritdoc />
    public async Task<LoadedContent> LoadAsync(ContentSource source, CancellationToken ct = default)
    {
        var validation = Validate(source);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Invalid content source: {string.Join(", ", validation.Errors)}");
        }

        var files = new List<ContentFile>();

        // ─────────────────────────────────────────────────────
        //  直接文本
        // ─────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(source.DirectContent))
        {
            var tokens = EstimateTokens(source.DirectContent);
            files.Add(new ContentFile
            {
                Path = "direct-input",
                FileName = "直接输入",
                Content = source.DirectContent,
                EstimatedTokens = tokens,
                CharacterCount = source.DirectContent.Length
            });
        }

        // ─────────────────────────────────────────────────────
        //  单个文件
        // ─────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(source.FilePath))
        {
            var file = await LoadFileAsync(source.FilePath, source.MaxFileSizeBytes, ct);
            if (file != null) files.Add(file);
        }

        // ─────────────────────────────────────────────────────
        //  多个文件
        // ─────────────────────────────────────────────────────
        if (source.FilePaths is { Count: > 0 })
        {
            foreach (var path in source.FilePaths.Take(source.MaxFiles))
            {
                var file = await LoadFileAsync(path, source.MaxFileSizeBytes, ct);
                if (file != null) files.Add(file);
            }
        }

        // ─────────────────────────────────────────────────────
        //  文件夹
        // ─────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(source.DirectoryPath))
        {
            var dirFiles = await LoadDirectoryAsync(
                source.DirectoryPath,
                source.Extensions,
                source.Recursive,
                source.MaxFiles,
                source.MaxFileSizeBytes,
                ct);
            files.AddRange(dirFiles);
        }

        // ─────────────────────────────────────────────────────
        //  上传文件
        // ─────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(source.UploadId))
        {
            var uploadDir = Path.Combine(_uploadsBasePath, source.UploadId);
            if (Directory.Exists(uploadDir))
            {
                var uploadFiles = await LoadDirectoryAsync(
                    uploadDir,
                    source.Extensions,
                    true,
                    source.MaxFiles,
                    source.MaxFileSizeBytes,
                    ct);
                files.AddRange(uploadFiles);
            }
        }

        // ─────────────────────────────────────────────────────
        //  构建结果
        // ─────────────────────────────────────────────────────
        var combinedText = string.Join("\n\n", files.Select(f => f.Content));
        var totalTokens = files.Sum(f => f.EstimatedTokens);
        var totalChars = files.Sum(f => f.CharacterCount);

        _logger.LogInformation(
            "Loaded {FileCount} files, {TotalChars} chars, ~{TotalTokens} tokens",
            files.Count, totalChars, totalTokens);

        return new LoadedContent
        {
            CombinedText = combinedText,
            Files = files,
            EstimatedTokens = totalTokens,
            TotalCharacters = totalChars,
            SourceDescription = source.ContentDescription,
            ContentType = DetermineContentType(files)
        };
    }

    /// <inheritdoc />
    public ContentSourceValidation Validate(ContentSource source)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 检查是否有任何来源
        var hasSource =
            !string.IsNullOrEmpty(source.FilePath) ||
            !string.IsNullOrEmpty(source.DirectoryPath) ||
            source.FilePaths is { Count: > 0 } ||
            !string.IsNullOrEmpty(source.DirectContent) ||
            !string.IsNullOrEmpty(source.UploadId);

        if (!hasSource)
        {
            errors.Add("未指定任何内容来源");
        }

        // 检查文件路径
        if (!string.IsNullOrEmpty(source.FilePath) && !File.Exists(source.FilePath))
        {
            errors.Add($"文件不存在: {source.FilePath}");
        }

        // 检查文件夹路径
        if (!string.IsNullOrEmpty(source.DirectoryPath) && !Directory.Exists(source.DirectoryPath))
        {
            errors.Add($"文件夹不存在: {source.DirectoryPath}");
        }

        // 检查多文件
        if (source.FilePaths != null)
        {
            var missing = source.FilePaths.Where(p => !File.Exists(p)).ToList();
            if (missing.Any())
            {
                errors.Add($"以下文件不存在: {string.Join(", ", missing.Take(5))}");
            }
        }

        // 检查上传 ID
        if (!string.IsNullOrEmpty(source.UploadId))
        {
            var uploadDir = Path.Combine(_uploadsBasePath, source.UploadId);
            if (!Directory.Exists(uploadDir))
            {
                errors.Add($"上传不存在: {source.UploadId}");
            }
        }

        // 警告
        if (source.MaxFiles < 1)
        {
            warnings.Add("MaxFiles 应大于 0");
        }

        if (errors.Any())
        {
            return ContentSourceValidation.Invalid(errors.ToArray());
        }

        if (warnings.Any())
        {
            return ContentSourceValidation.ValidWithWarnings(warnings.ToArray());
        }

        return ContentSourceValidation.Valid();
    }

    /// <inheritdoc />
    public async Task<ContentPreview> PreviewAsync(ContentSource source, CancellationToken ct = default)
    {
        var files = new List<string>();
        long totalSize = 0;

        // 收集所有文件路径
        if (!string.IsNullOrEmpty(source.FilePath) && File.Exists(source.FilePath))
        {
            files.Add(source.FilePath);
            totalSize += new FileInfo(source.FilePath).Length;
        }

        if (source.FilePaths != null)
        {
            foreach (var path in source.FilePaths.Where(File.Exists))
            {
                files.Add(path);
                totalSize += new FileInfo(path).Length;
            }
        }

        if (!string.IsNullOrEmpty(source.DirectoryPath) && Directory.Exists(source.DirectoryPath))
        {
            var option = source.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var ext in source.Extensions)
            {
                var pattern = $"*{ext}";
                foreach (var file in Directory.EnumerateFiles(source.DirectoryPath, pattern, option))
                {
                    if (files.Count >= source.MaxFiles) break;
                    files.Add(file);
                    totalSize += new FileInfo(file).Length;
                }
            }
        }

        if (!string.IsNullOrEmpty(source.DirectContent))
        {
            totalSize += source.DirectContent.Length;
        }

        // 估算 tokens（按平均 4 字符 = 1 token）
        var estimatedTokens = (int)(totalSize / 4);

        // 检查限制
        var exceedsLimits = files.Count > source.MaxFiles;
        string? limitWarning = null;

        if (exceedsLimits)
        {
            limitWarning = $"文件数 ({files.Count}) 超过限制 ({source.MaxFiles})";
        }

        return new ContentPreview
        {
            FileCount = Math.Min(files.Count, source.MaxFiles),
            FilePaths = files.Take(source.MaxFiles).ToList(),
            TotalSizeBytes = totalSize,
            EstimatedTokens = estimatedTokens,
            ExceedsLimits = exceedsLimits,
            LimitWarning = limitWarning
        };
    }

    // ─────────────────────────────────────────────────────────
    //  私有方法
    // ─────────────────────────────────────────────────────────

    private async Task<ContentFile?> LoadFileAsync(string path, long maxSize, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("File not found: {Path}", path);
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length > maxSize)
            {
                _logger.LogWarning("File too large: {Path} ({Size} bytes)", path, info.Length);
                return null;
            }

            var content = await File.ReadAllTextAsync(path, ct);
            var tokens = EstimateTokens(content);

            return new ContentFile
            {
                Path = path,
                FileName = info.Name,
                Content = content,
                EstimatedTokens = tokens,
                CharacterCount = content.Length,
                FileSizeBytes = info.Length,
                Extension = info.Extension,
                LastModified = info.LastWriteTimeUtc
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load file: {Path}", path);
            return null;
        }
    }

    private async Task<List<ContentFile>> LoadDirectoryAsync(
        string directory,
        IReadOnlyList<string> extensions,
        bool recursive,
        int maxFiles,
        long maxFileSize,
        CancellationToken ct)
    {
        var files = new List<ContentFile>();
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var ext in extensions)
        {
            if (files.Count >= maxFiles) break;

            var pattern = $"*{ext}";
            foreach (var filePath in Directory.EnumerateFiles(directory, pattern, option))
            {
                if (files.Count >= maxFiles) break;

                var file = await LoadFileAsync(filePath, maxFileSize, ct);
                if (file != null) files.Add(file);
            }
        }

        return files;
    }

    private static int EstimateTokens(string text)
    {
        // 简单估算：中文约 2 字符 = 1 token，英文约 4 字符 = 1 token
        // 这里用保守估计
        if (string.IsNullOrEmpty(text)) return 0;

        var chineseCount = text.Count(c => c >= 0x4E00 && c <= 0x9FFF);
        var otherCount = text.Length - chineseCount;

        return (chineseCount / 2) + (otherCount / 4);
    }

    private static string DetermineContentType(IReadOnlyList<ContentFile> files)
    {
        if (files.Count == 0) return "empty";
        if (files.Count == 1)
        {
            return files[0].Extension.ToLowerInvariant() switch
            {
                ".md" or ".markdown" => "markdown",
                ".txt" => "text",
                ".cs" or ".js" or ".ts" or ".py" => "code",
                ".json" => "json",
                ".xml" or ".html" => "markup",
                _ => "text"
            };
        }

        return "multiple";
    }
}

