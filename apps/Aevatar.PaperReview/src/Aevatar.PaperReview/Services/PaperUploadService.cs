using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  PAPER UPLOAD SERVICE
//  职责：文件上传、存储、解析（PDF/MD/TXT）
// ============================================================

/// <summary>
/// 论文上传服务 - 处理文件上传和文本提取。
/// </summary>
public sealed class PaperUploadService
{
    private readonly string _uploadsBasePath;
    private readonly ILogger<PaperUploadService> _logger;

    public PaperUploadService(ILogger<PaperUploadService> logger)
    {
        _logger = logger;
        _uploadsBasePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(_uploadsBasePath);
    }

    /// <summary>
    /// 上传论文文件。
    /// </summary>
    public async Task<UploadResult> UploadAsync(IFormFileCollection files, CancellationToken ct)
    {
        if (files.Count == 0)
            return UploadResult.Fail("No file uploaded");

        try
        {
            var file = files[0];
            var uploadId = Guid.NewGuid().ToString("N")[..12];
            var uploadDir = Path.Combine(_uploadsBasePath, uploadId);
            Directory.CreateDirectory(uploadDir);

            var fileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadDir, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream, ct);

            _logger.LogInformation("Uploaded paper: {FileName} ({Size} bytes)", fileName, file.Length);

            return UploadResult.Ok(uploadId, fileName, file.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload paper");
            return UploadResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 从上传 ID 加载论文内容。
    /// </summary>
    public async Task<(string Content, string FileName)> LoadContentAsync(string uploadId)
    {
        var uploadDir = Path.Combine(_uploadsBasePath, uploadId);
        if (!Directory.Exists(uploadDir))
            return ("", "");

        var files = Directory.GetFiles(uploadDir);
        if (files.Length == 0)
            return ("", "");

        var filePath = files[0];
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var fileName = Path.GetFileName(filePath);

        return ext switch
        {
            ".pdf" => (ExtractPdfText(filePath), fileName),
            _ => (await File.ReadAllTextAsync(filePath), fileName)
        };
    }

    // ─────────────────────────────────────────────────────────
    //  PDF 文本提取
    // ─────────────────────────────────────────────────────────

    private string ExtractPdfText(string filePath)
    {
        // Primary: PdfPig extraction
        try
        {
            var sb = new StringBuilder();
            using var doc = PdfDocument.Open(filePath);
            foreach (Page page in doc.GetPages())
            {
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }

            var text = sb.ToString();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PdfPig failed, falling back to regex");
        }

        // Fallback: regex-based extraction
        return ExtractPdfTextFallback(filePath);
    }

    private static string ExtractPdfTextFallback(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var raw = Encoding.Latin1.GetString(bytes);

        var matches = Regex.Matches(raw, @"\(([^\\)]*(?:\\.[^\\)]*)*)\)");
        var fallback = new StringBuilder();

        foreach (Match m in matches.Cast<Match>())
        {
            var text = m.Groups[1].Value
                .Replace("\\(", "(")
                .Replace("\\)", ")")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\\", "\\");
            fallback.Append(text);
            fallback.Append(' ');
        }

        return fallback.ToString();
    }
}

// ============================================================
//  结果类型
// ============================================================

/// <summary>
/// 上传结果。
/// </summary>
public sealed record UploadResult
{
    public bool Success { get; init; }
    public string? UploadId { get; init; }
    public string? FileName { get; init; }
    public long Size { get; init; }
    public string? Error { get; init; }

    public static UploadResult Ok(string uploadId, string fileName, long size) =>
        new() { Success = true, UploadId = uploadId, FileName = fileName, Size = size };

    public static UploadResult Fail(string error) =>
        new() { Success = false, Error = error };
}
