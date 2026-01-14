using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aevatar.Trade.Api.Controllers;

/// <summary>
/// Trade audit artifact endpoints (read-only).
///
/// Purpose:
/// - Frontend dashboard needs to render "human readable" strategy logs and link JSONL artifacts.
/// - Keep it simple: read from local disk (TradeAudit:OutputDir).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly TradeAuditConfig _audit;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AuditController> _logger;

    public AuditController(IOptions<TradeAuditConfig> audit, IHostEnvironment env, ILogger<AuditController> logger)
    {
        _audit = audit.Value;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// List audit files in output directory (md/jsonl).
    /// </summary>
    [HttpGet("files")]
    public IActionResult ListFiles()
    {
        var dir = ResolveAuditDir();
        if (dir == null || !Directory.Exists(dir))
            return Ok(new { directory = dir, files = Array.Empty<object>() });

        var files = Directory.EnumerateFiles(dir, "trade_audit_*.*", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new
            {
                name = f.Name,
                sizeBytes = f.Length,
                lastWriteTimeUtc = f.LastWriteTimeUtc
            })
            .ToArray();

        return Ok(new { directory = dir.Replace("\\", "/"), files });
    }

    /// <summary>
    /// Get latest markdown log (tail).
    /// </summary>
    [HttpGet("latest")]
    public IActionResult GetLatestMarkdown([FromQuery] int maxBytes = 200_000)
    {
        try
        {
            var dir = ResolveAuditDir();
            if (dir == null || !Directory.Exists(dir))
                return Ok(new { directory = dir, file = (string?)null, runId = (string?)null, updatedAtUtc = (DateTime?)null, content = "" });

            var latest = Directory.EnumerateFiles(dir, "trade_audit_*.md", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest == null)
                return Ok(new { directory = dir.Replace("\\", "/"), file = (string?)null, runId = (string?)null, updatedAtUtc = (DateTime?)null, content = "" });

            var content = ReadTailUtf8(latest.FullName, maxBytes);
            return Ok(new
            {
                directory = dir.Replace("\\", "/"),
                file = latest.Name,
                runId = TryParseRunId(latest.Name),
                updatedAtUtc = latest.LastWriteTimeUtc,
                content
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read latest audit markdown");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Read an audit file tail (md/jsonl).
    /// </summary>
    [HttpGet("tail")]
    public IActionResult Tail([FromQuery] string name, [FromQuery] int maxBytes = 200_000)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { error = "Missing query: name" });

            // Prevent path traversal; only allow file name (no separators).
            var safeName = Path.GetFileName(name);
            if (!string.Equals(safeName, name, StringComparison.Ordinal))
                return BadRequest(new { error = "Invalid file name." });

            var dir = ResolveAuditDir();
            if (dir == null || !Directory.Exists(dir))
                return NotFound(new { error = "Audit directory not found." });

            var fullPath = Path.Combine(dir, safeName);
            if (!System.IO.File.Exists(fullPath))
                return NotFound(new { error = $"File not found: {safeName}" });

            var content = ReadTailUtf8(fullPath, maxBytes);
            var fi = new FileInfo(fullPath);
            return Ok(new
            {
                directory = dir.Replace("\\", "/"),
                file = fi.Name,
                sizeBytes = fi.Length,
                updatedAtUtc = fi.LastWriteTimeUtc,
                content
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read audit file tail: {Name}", name);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// List AI Wars upload receipt files.
    ///
    /// Receipts are written by AiWarsLogUploaderAgent:
    ///   {TradeAudit:OutputDir}/ai-wars/receipts/aiwars_receipt_{requestId}.json
    /// </summary>
    [HttpGet("aiwars/receipts")]
    public IActionResult ListAiWarsReceipts()
    {
        var dir = ResolveAuditDir();
        if (dir == null || !Directory.Exists(dir))
            return Ok(new { directory = dir, receiptsDir = (string?)null, files = Array.Empty<object>() });

        var receiptsDir = Path.Combine(dir, "ai-wars", "receipts");
        if (!Directory.Exists(receiptsDir))
            return Ok(new { directory = dir.Replace("\\", "/"), receiptsDir = receiptsDir.Replace("\\", "/"), files = Array.Empty<object>() });

        var files = Directory.EnumerateFiles(receiptsDir, "aiwars_receipt_*.json", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(200)
            .Select(f => new
            {
                name = f.Name,
                sizeBytes = f.Length,
                lastWriteTimeUtc = f.LastWriteTimeUtc
            })
            .ToArray();

        return Ok(new
        {
            directory = dir.Replace("\\", "/"),
            receiptsDir = receiptsDir.Replace("\\", "/"),
            files
        });
    }

    /// <summary>
    /// Get latest AI Wars upload receipt (tail).
    /// </summary>
    [HttpGet("aiwars/receipt/latest")]
    public IActionResult GetLatestAiWarsReceipt([FromQuery] int maxBytes = 200_000)
    {
        try
        {
            var dir = ResolveAuditDir();
            if (dir == null || !Directory.Exists(dir))
                return Ok(new { directory = dir, receiptsDir = (string?)null, file = (string?)null, updatedAtUtc = (DateTime?)null, content = "" });

            var receiptsDir = Path.Combine(dir, "ai-wars", "receipts");
            if (!Directory.Exists(receiptsDir))
                return Ok(new { directory = dir.Replace("\\", "/"), receiptsDir = receiptsDir.Replace("\\", "/"), file = (string?)null, updatedAtUtc = (DateTime?)null, content = "" });

            var latest = Directory.EnumerateFiles(receiptsDir, "aiwars_receipt_*.json", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest == null)
                return Ok(new { directory = dir.Replace("\\", "/"), receiptsDir = receiptsDir.Replace("\\", "/"), file = (string?)null, updatedAtUtc = (DateTime?)null, content = "" });

            var content = ReadTailUtf8(latest.FullName, maxBytes);
            return Ok(new
            {
                directory = dir.Replace("\\", "/"),
                receiptsDir = receiptsDir.Replace("\\", "/"),
                file = latest.Name,
                updatedAtUtc = latest.LastWriteTimeUtc,
                content
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read latest AI Wars receipt");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Normalize a legacy JSONL audit file that stored "envelopeJson" as a double-encoded string (full of \u0022).
    /// Produces a new file with "envelope" as a real JSON object.
    ///
    /// Usage:
    ///   POST /api/audit/normalize?name=trade_audit_xxx.jsonl
    /// </summary>
    [HttpPost("normalize")]
    public async Task<IActionResult> Normalize([FromQuery] string name)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { error = "Missing query: name" });

            var safeName = Path.GetFileName(name);
            if (!string.Equals(safeName, name, StringComparison.Ordinal))
                return BadRequest(new { error = "Invalid file name." });

            var dir = ResolveAuditDir();
            if (dir == null || !Directory.Exists(dir))
                return NotFound(new { error = "Audit directory not found." });

            var srcPath = Path.Combine(dir, safeName);
            if (!System.IO.File.Exists(srcPath))
                return NotFound(new { error = $"File not found: {safeName}" });

            // Produce: <base>.normalized.jsonl
            // NOTE: ".jsonl" includes the dot; avoid the classic "..normalized" mistake.
            var baseName = safeName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(safeName)
                : safeName;
            var outName = baseName + ".normalized.jsonl";
            var outPath = Path.Combine(dir, outName);

            var converted = 0;
            var total = 0;

            await using (var writer = new StreamWriter(outPath, append: false, Utf8NoBom))
            {
                // Read using utf-8-sig to tolerate BOM (some old files were created with Encoding.UTF8).
                foreach (var line in System.IO.File.ReadLines(srcPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    total++;
                    try
                    {
                        // Strip BOM if it appears at the beginning of the line (very first line).
                        // This keeps JSON parsing strict and prevents tooling from failing.
                        var normalizedLine = line.Length > 0 && line[0] == '\uFEFF'
                            ? line[1..]
                            : line;

                        using var doc = JsonDocument.Parse(normalizedLine);
                        var root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                            continue;

                        if (root.TryGetProperty("envelopeJson", out var envelopeJsonEl) &&
                            envelopeJsonEl.ValueKind == JsonValueKind.String)
                        {
                            var envelopeJson = envelopeJsonEl.GetString() ?? "";
                            using var envDoc = JsonDocument.Parse(envelopeJson);
                            var envObj = envDoc.RootElement.Clone();

                            var normalized = new
                            {
                                auditRunId = root.TryGetProperty("auditRunId", out var run) ? run.GetString() : null,
                                auditAgentId = root.TryGetProperty("auditAgentId", out var agent) ? agent.GetString() : null,
                                envelope = (JsonElement?)envObj
                            };

                            await writer.WriteLineAsync(JsonSerializer.Serialize(normalized));
                            converted++;
                            continue;
                        }

                        // Already normalized or unknown shape: keep line as is.
                        await writer.WriteLineAsync(normalizedLine);
                    }
                    catch
                    {
                        // Keep the original line if parsing fails; do not lose data.
                        var normalizedLine = line.Length > 0 && line[0] == '\uFEFF'
                            ? line[1..]
                            : line;
                        await writer.WriteLineAsync(normalizedLine);
                    }
                }
            }

            return Ok(new
            {
                directory = dir.Replace("\\", "/"),
                source = safeName,
                output = outName,
                totalLines = total,
                convertedLines = converted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to normalize audit file: {Name}", name);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Server-Sent Events (SSE) stream of audit JSONL lines (tail -f).
    ///
    /// Why:
    /// - Frontend wants a "streaming" view (like AxiomReasoning cards) without heavy polling.
    /// - TradeAuditAgent already writes append-only JSONL; SSE can follow the file.
    ///
    /// Usage:
    ///   GET /api/audit/stream?name=trade_audit_xxx.jsonl&replayLines=200
    ///   GET /api/audit/stream?runId=xxxx&replayLines=200
    ///   GET /api/audit/stream  (auto-picks latest jsonl)
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(
        [FromQuery] string? name = null,
        [FromQuery] string? runId = null,
        [FromQuery] int replayLines = 200,
        CancellationToken ct = default)
    {
        // SSE requires direct Response write (do not use Ok()).
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        replayLines = Math.Clamp(replayLines, 0, 2000);

        var dir = ResolveAuditDir();
        if (dir == null || !Directory.Exists(dir))
        {
            await Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = "Audit directory not found.", directory = dir })}\n\n", ct);
            await Response.Body.FlushAsync(ct);
            return;
        }

        var filePath = ResolveAuditJsonlPath(dir, name, runId);
        if (filePath == null)
        {
            await Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = "Audit JSONL file not found.", directory = dir, name, runId })}\n\n", ct);
            await Response.Body.FlushAsync(ct);
            return;
        }

        // Initial replay (best-effort, bounded).
        if (replayLines > 0)
        {
            try
            {
                foreach (var line in ReadTailLinesUtf8(filePath, maxBytes: 512_000, maxLines: replayLines))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var normalizedLine = line.Length > 0 && line[0] == '\uFEFF' ? line[1..] : line;
                    await Response.WriteAsync($"data: {normalizedLine}\n\n", ct);
                }
                await Response.Body.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SSE replay failed for {File}", filePath);
            }
        }

        // Follow (tail -f).
        // - Keep the file open with FileShare.ReadWrite so TradeAuditAgent can append.
        // - When we hit EOF, wait a bit and retry.
        long pos = 0;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            pos = fs.Length;
            fs.Seek(pos, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024, leaveOpen: true);

            while (!ct.IsCancellationRequested && !HttpContext.RequestAborted.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null)
                {
                    // EOF: wait for new content.
                    await Task.Delay(350, ct);

                    // If file truncated/replaced, reopen.
                    try
                    {
                        var len = new FileInfo(filePath).Length;
                        if (len < pos)
                        {
                            break;
                        }
                    }
                    catch { /* ignore */ }
                    continue;
                }

                pos += Encoding.UTF8.GetByteCount(line) + 1;
                var normalizedLine = line.Length > 0 && line[0] == '\uFEFF' ? line[1..] : line;
                await Response.WriteAsync($"data: {normalizedLine}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected / request aborted
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSE stream failed for {File}", filePath);
            try
            {
                await Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = ex.Message, file = Path.GetFileName(filePath) })}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
            catch { /* ignore */ }
        }
    }

    private string? ResolveAuditDir()
    {
        var raw = string.IsNullOrWhiteSpace(_audit.OutputDir) ? "trade-audit" : _audit.OutputDir.Trim();
        if (Path.IsPathRooted(raw))
            return raw;

        // Prefer ASP.NET content root (project folder in dev).
        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, raw));
    }

    private static string? TryParseRunId(string fileName)
    {
        // trade_audit_<runId>.md
        const string prefix = "trade_audit_";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var rest = fileName[prefix.Length..];
        var dot = rest.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0) return null;
        return rest[..dot];
    }

    private static string ReadTailUtf8(string fullPath, int maxBytes)
    {
        maxBytes = Math.Clamp(maxBytes, 1_000, 2_000_000);

        // Read last maxBytes bytes; tolerate concurrent append.
        using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var len = fs.Length;
        var start = Math.Max(0, len - maxBytes);
        fs.Seek(start, SeekOrigin.Begin);

        var buf = new byte[len - start];
        _ = fs.Read(buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf);
    }

    private static string? ResolveAuditJsonlPath(string dir, string? name, string? runId)
    {
        // Prefer explicit file name.
        if (!string.IsNullOrWhiteSpace(name))
        {
            var safeName = Path.GetFileName(name);
            if (!string.Equals(safeName, name, StringComparison.Ordinal))
                return null;
            var p = Path.Combine(dir, safeName);
            return System.IO.File.Exists(p) ? p : null;
        }

        // Prefer runId.
        if (!string.IsNullOrWhiteSpace(runId))
        {
            var p = Path.Combine(dir, $"trade_audit_{runId}.jsonl");
            if (System.IO.File.Exists(p))
                return p;
        }

        // Fallback: latest jsonl.
        var latest = Directory.EnumerateFiles(dir, "trade_audit_*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        return latest?.FullName;
    }

    private static IEnumerable<string> ReadTailLinesUtf8(string fullPath, int maxBytes, int maxLines)
    {
        if (maxLines <= 0) yield break;
        var tail = ReadTailUtf8(fullPath, maxBytes);
        var lines = tail.Replace("\r\n", "\n").Split('\n');
        var start = Math.Max(0, lines.Length - maxLines - 1);
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            yield return line;
        }
    }
}


