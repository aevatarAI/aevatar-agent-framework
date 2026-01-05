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

            var outName = safeName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? safeName[..^5] + ".normalized.jsonl"
                : safeName + ".normalized.jsonl";
            var outPath = Path.Combine(dir, outName);

            var converted = 0;
            var total = 0;

            await using (var writer = new StreamWriter(outPath, append: false, Encoding.UTF8))
            {
                foreach (var line in System.IO.File.ReadLines(srcPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    total++;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
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
                        await writer.WriteLineAsync(line);
                    }
                    catch
                    {
                        // Keep the original line if parsing fails; do not lose data.
                        await writer.WriteLineAsync(line);
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
}


