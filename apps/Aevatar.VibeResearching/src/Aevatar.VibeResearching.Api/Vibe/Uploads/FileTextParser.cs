using System.Text;

namespace VibeResearching.Api.Vibe.Uploads;

/// <summary>
/// Extracts text content from uploaded files (PDF, TXT, MD, JSON).
/// </summary>
public sealed class FileTextParser
{
    private static readonly HashSet<string> SupportedTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv"
    };

    private static readonly HashSet<string> SupportedBinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf"
    };

    private readonly ILogger<FileTextParser> _logger;

    public FileTextParser(ILogger<FileTextParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts text content from the uploaded file.
    /// </summary>
    public async Task<TextExtractionResult> ExtractTextAsync(
        IFormFile file,
        int maxChars = 50_000,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var fileName = file.FileName ?? "unknown";
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        _logger.LogDebug("Extracting text from file: {FileName}, extension: {Extension}", fileName, ext);

        try
        {
            string content;

            if (SupportedTextExtensions.Contains(ext))
            {
                content = await ExtractFromTextFileAsync(file, maxChars, ct);
            }
            else if (ext == ".pdf")
            {
                content = await ExtractFromPdfAsync(file, maxChars, ct);
            }
            else
            {
                return new TextExtractionResult
                {
                    Success = false,
                    ErrorMessage = $"Unsupported file type: {ext}",
                    FileName = fileName
                };
            }

            // Truncate if too long
            if (content.Length > maxChars)
            {
                content = content[..maxChars] + "\n\n[Content truncated...]";
            }

            _logger.LogInformation(
                "Successfully extracted {CharCount} characters from {FileName}",
                content.Length, fileName);

            return new TextExtractionResult
            {
                Success = true,
                Content = content,
                FileName = fileName,
                CharCount = content.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from {FileName}", fileName);
            return new TextExtractionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                FileName = fileName
            };
        }
    }

    private static async Task<string> ExtractFromTextFileAsync(
        IFormFile file,
        int maxChars,
        CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var buffer = new char[Math.Min(maxChars + 1000, 100_000)];
        var read = await reader.ReadAsync(buffer, ct);

        return new string(buffer, 0, read);
    }

    private async Task<string> ExtractFromPdfAsync(
        IFormFile file,
        int maxChars,
        CancellationToken ct)
    {
        // Simple PDF text extraction using basic parsing
        // For production, consider using a proper PDF library like PdfPig or iTextSharp
        // 
        // LIMITATIONS:
        // - Only works with simple, uncompressed PDFs
        // - Does not support encrypted PDFs
        // - Does not support image-based PDFs (scanned documents)
        // - May fail with large files (> 5MB) or complex formats
        // - Non-ASCII characters may be lost due to ASCII encoding
        
        await using var stream = file.OpenReadStream();
        
        // Check file size - warn for large files
        var fileSize = stream.Length;
        if (fileSize > 5 * 1024 * 1024) // 5MB
        {
            _logger.LogWarning("PDF file is large ({Size} bytes) - simple extraction may fail or be slow for {FileName}", 
                fileSize, file.FileName);
        }
        
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        // Validate PDF header
        if (bytes.Length < 8)
        {
            _logger.LogError("PDF file too small or invalid: {FileName}, size: {Size} bytes", file.FileName, bytes.Length);
            throw new InvalidOperationException("Invalid PDF file: file is too small or corrupted");
        }

        var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(8, bytes.Length));
        if (!header.StartsWith("%PDF", StringComparison.Ordinal))
        {
            _logger.LogError("Invalid PDF header for {FileName}: {Header}", file.FileName, header);
            throw new InvalidOperationException("Invalid PDF file format: missing PDF header");
        }

        var text = ExtractTextFromPdfBytes(bytes, maxChars);

        if (string.IsNullOrWhiteSpace(text))
        {
            var errorMsg = fileSize > 5 * 1024 * 1024
                ? "PDF file is too large for simple text extraction. The file may also be image-based, encrypted, or use unsupported compression. Please use a professional PDF library or split the file."
                : "PDF content could not be extracted. Possible reasons: 1) Image-based PDF (scanned document), 2) Encrypted PDF, 3) Unsupported compression format (FlateDecode, LZWDecode, etc.), 4) Complex font encoding, 5) Corrupted file.";
            
            _logger.LogWarning("PDF text extraction returned empty content for {FileName}. File size: {Size} bytes. {Error}", 
                file.FileName, fileSize, errorMsg);
            
            throw new InvalidOperationException(errorMsg);
        }

        return text;
    }

    /// <summary>
    /// Basic PDF text extraction. Extracts text streams from PDF.
    /// This is a simplified implementation - for production use a proper PDF library.
    /// </summary>
    private static string ExtractTextFromPdfBytes(byte[] pdfBytes, int maxChars)
    {
        var sb = new StringBuilder(maxChars);
        var content = Encoding.ASCII.GetString(pdfBytes);

        // Look for text streams in PDF (between BT and ET markers)
        var index = 0;
        while (index < content.Length && sb.Length < maxChars)
        {
            var btIndex = content.IndexOf("BT", index, StringComparison.Ordinal);
            if (btIndex < 0) break;

            var etIndex = content.IndexOf("ET", btIndex, StringComparison.Ordinal);
            if (etIndex < 0) break;

            var textBlock = content.Substring(btIndex + 2, etIndex - btIndex - 2);

            // Extract text from Tj and TJ operators
            ExtractTjText(textBlock, sb, maxChars);

            index = etIndex + 2;
        }

        // Also try to extract from stream objects
        index = 0;
        while (index < content.Length && sb.Length < maxChars)
        {
            var streamStart = content.IndexOf("stream", index, StringComparison.Ordinal);
            if (streamStart < 0) break;

            var streamEnd = content.IndexOf("endstream", streamStart, StringComparison.Ordinal);
            if (streamEnd < 0) break;

            // Try to extract readable text from stream
            var streamContent = content.Substring(streamStart + 6, streamEnd - streamStart - 6);
            foreach (var c in streamContent)
            {
                if (sb.Length >= maxChars) break;
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsPunctuation(c))
                {
                    sb.Append(c);
                }
            }

            index = streamEnd + 9;
        }

        // Clean up the result
        var result = sb.ToString();
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");
        return result.Trim();
    }

    private static void ExtractTjText(string textBlock, StringBuilder sb, int maxChars)
    {
        // Look for (text) Tj or [(text)] TJ patterns
        var i = 0;
        while (i < textBlock.Length && sb.Length < maxChars)
        {
            var parenStart = textBlock.IndexOf('(', i);
            if (parenStart < 0) break;

            var parenEnd = textBlock.IndexOf(')', parenStart);
            if (parenEnd < 0) break;

            var text = textBlock.Substring(parenStart + 1, parenEnd - parenStart - 1);

            // Decode PDF escape sequences
            text = text.Replace("\\n", "\n")
                       .Replace("\\r", "\r")
                       .Replace("\\t", "\t")
                       .Replace("\\(", "(")
                       .Replace("\\)", ")")
                       .Replace("\\\\", "\\");

            sb.Append(text);
            sb.Append(' ');

            i = parenEnd + 1;
        }
    }

    public static bool IsSupported(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return SupportedTextExtensions.Contains(ext) || SupportedBinaryExtensions.Contains(ext);
    }
}

/// <summary>
/// Result of text extraction from a file.
/// </summary>
public sealed class TextExtractionResult
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? ErrorMessage { get; init; }
    public string FileName { get; init; } = "";
    public int CharCount { get; init; }
}
