using System.Text;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  WorkspaceReadFileExecutor
//
//  Responsibility:
//  - Read a file under workspace root with bounded output size (max_chars)
//
//  Output (Protobuf-Value friendly Dictionary):
//  {
//    ok: bool,
//    path: string,          // input path (rendered)
//    content: string,       // bounded excerpt
//    truncated: bool,
//    total_chars: int,      // exact if small; -1 if unknown (large file shortcut)
//    kept_chars: int,
//    error: string?         // bounded
//  }
// ============================================================

public sealed class WorkspaceReadFileExecutor
{
    private const int DefaultMaxChars = 16_000;
    private const int MaxMaxChars = 200_000;

    // If the file is very large, we avoid counting total chars (still return bounded excerpt).
    private const long MaxBytesForTotalCharCount = 8L * 1024 * 1024; // 8MB

    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;

    public WorkspaceReadFileExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    public PrimitiveResult Execute(StepDefinition step, Dictionary<string, object> variables, string workspaceRoot)
    {
        var pathTemplate = step.Parameters.GetValueOrDefault("path")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(pathTemplate))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["path"] = "",
                ["content"] = "",
                ["truncated"] = false,
                ["total_chars"] = 0,
                ["kept_chars"] = 0,
                ["error"] = "missing_required_param:path"
            });
        }

        var renderedPath = _templateEngine.Render(pathTemplate, variables).Replace("\r", "").Trim();
        var maxChars = ResolveInt(step.Parameters.GetValueOrDefault("max_chars"), variables, DefaultMaxChars);
        maxChars = Math.Clamp(maxChars, 1, MaxMaxChars);

        if (!WorkspacePathGuard.TryResolvePathWithinRoot(workspaceRoot, renderedPath, out var fullPath, out var pathError))
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["path"] = renderedPath,
                ["content"] = "",
                ["truncated"] = false,
                ["total_chars"] = 0,
                ["kept_chars"] = 0,
                ["error"] = Bound(pathError, 400)
            }) with
            {
                UserPrompt = $"workspace_read_file: {renderedPath}"
            };
        }

        try
        {
            var fileInfo = new FileInfo(fullPath!);
            if (!fileInfo.Exists)
            {
                return PrimitiveResult.Ok(new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["path"] = renderedPath,
                    ["content"] = "",
                    ["truncated"] = false,
                    ["total_chars"] = 0,
                    ["kept_chars"] = 0,
                    ["error"] = "file_not_found"
                }) with
                {
                    UserPrompt = $"workspace_read_file: {renderedPath}"
                };
            }

            var shouldCountTotalChars = fileInfo.Length <= MaxBytesForTotalCharCount;

            var sb = new StringBuilder(capacity: Math.Min(maxChars, 32_768));
            var totalChars = 0;
            var truncated = false;

            using var fs = File.OpenRead(fullPath!);
            using var reader = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);

            var buffer = new char[4096];

            if (shouldCountTotalChars)
            {
                // Read to EOF, but store only first maxChars.
                int n;
                while ((n = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalChars += n;
                    if (sb.Length < maxChars)
                    {
                        var toTake = Math.Min(n, maxChars - sb.Length);
                        sb.Append(buffer, 0, toTake);
                    }
                }

                truncated = totalChars > maxChars;
            }
            else
            {
                // Large file shortcut: read only until we fill maxChars (+1 probe to know truncated).
                while (sb.Length < maxChars)
                {
                    var remaining = maxChars - sb.Length;
                    var toRead = Math.Min(buffer.Length, remaining);
                    var n = reader.Read(buffer, 0, toRead);
                    if (n <= 0) break;
                    sb.Append(buffer, 0, n);
                }

                // Probe one more char to determine truncation without scanning the whole file.
                truncated = reader.Read() != -1;
                totalChars = -1; // unknown
            }

            var content = sb.ToString();

            // Basic binary-ish guard: reject NUL-heavy payloads (still deterministic).
            if (content.IndexOf('\0') >= 0)
            {
                return PrimitiveResult.Ok(new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["path"] = renderedPath,
                    ["content"] = "",
                    ["truncated"] = false,
                    ["total_chars"] = 0,
                    ["kept_chars"] = 0,
                    ["error"] = "file_not_text"
                }) with
                {
                    UserPrompt = $"workspace_read_file: {renderedPath}"
                };
            }

            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["path"] = renderedPath,
                ["content"] = content,
                ["truncated"] = truncated,
                ["total_chars"] = totalChars,
                ["kept_chars"] = content.Length
            }) with
            {
                UserPrompt = $"workspace_read_file: {renderedPath}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[workspace_read_file] read failed.");
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["path"] = renderedPath,
                ["content"] = "",
                ["truncated"] = false,
                ["total_chars"] = 0,
                ["kept_chars"] = 0,
                ["error"] = Bound(ex.Message, 400)
            }) with
            {
                UserPrompt = $"workspace_read_file: {renderedPath}"
            };
        }
    }

    private int ResolveInt(object? value, Dictionary<string, object> vars, int defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => ConvertToInt(_templateEngine.Evaluate(s, vars), defaultValue),
            _ => defaultValue
        };
    }

    private static int ConvertToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    private static string Bound(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "...(truncated)";
    }
}


