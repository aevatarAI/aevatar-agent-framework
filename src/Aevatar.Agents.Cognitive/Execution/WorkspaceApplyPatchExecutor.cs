using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  WorkspaceApplyPatchExecutor
//
//  Responsibility:
//  - Apply a bounded set of deterministic patches under workspace root
//
//  Supported ops (MVP):
//  - create_or_replace: write entire file content
//  - replace_span: replace line span [start_line, end_line_exclusive) (1-based)
//
//  Input:
//  - step.Parameters["patch"]   : single patch object (optional)
//  - step.Parameters["patches"] : array of patch objects (optional)
//
//  Output (Protobuf-Value friendly Dictionary):
//  {
//    ok: bool,
//    files_changed: int,
//    patches_applied: int,
//    error: string?,
//    errors: [string]          // bounded (best-effort)
//  }
// ============================================================

public sealed class WorkspaceApplyPatchExecutor
{
    private const int MaxPatches = 10;
    private const int MaxErrorItems = 10;

    private const int MaxTextCharsPerPatch = 120_000;

    private static readonly Regex PurePathTemplateRegex = new(@"^\s*\{\{\s*([a-zA-Z_][\w\.]*)\s*\}\}\s*$", RegexOptions.Compiled);

    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;

    public WorkspaceApplyPatchExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    public PrimitiveResult Execute(StepDefinition step, Dictionary<string, object> variables, string workspaceRoot)
    {
        var patchesObj = step.Parameters.GetValueOrDefault("patches");
        var patchObj = step.Parameters.GetValueOrDefault("patch");

        var resolved = ResolveValue(patchesObj ?? patchObj, variables);
        var patches = NormalizePatchList(resolved);

        if (patches.Count == 0)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["files_changed"] = 0,
                ["patches_applied"] = 0,
                ["error"] = "no_patches"
            }) with
            {
                UserPrompt = "workspace_apply_patch: (no patches)"
            };
        }

        if (patches.Count > MaxPatches)
        {
            patches = patches.Take(MaxPatches).ToList();
        }

        var errors = new List<object>(capacity: Math.Min(MaxErrorItems, patches.Count));
        var changedFiles = new HashSet<string>(StringComparer.Ordinal);
        var applied = 0;

        foreach (var p in patches)
        {
            if (p == null)
                continue;

            if (!TryApplySinglePatch(workspaceRoot, p, out var changedRelPath, out var error))
            {
                if (!string.IsNullOrWhiteSpace(error) && errors.Count < MaxErrorItems)
                    errors.Add(Bound(error!, 400));
                continue;
            }

            applied++;
            if (!string.IsNullOrWhiteSpace(changedRelPath))
                changedFiles.Add(changedRelPath!);
        }

        var ok = applied > 0 && errors.Count == 0;
        var result = new Dictionary<string, object?>
        {
            ["ok"] = ok,
            ["files_changed"] = changedFiles.Count,
            ["patches_applied"] = applied
        };

        if (errors.Count > 0)
        {
            result["errors"] = errors;
            result["error"] = errors[0]?.ToString() ?? "patch_failed";
        }

        return PrimitiveResult.Ok(result) with
        {
            UserPrompt = $"workspace_apply_patch: {applied} applied, {errors.Count} errors"
        };
    }

    private bool TryApplySinglePatch(
        string workspaceRoot,
        Dictionary<string, object?> patch,
        out string? changedRelPath,
        out string? error)
    {
        changedRelPath = null;
        error = null;

        var op = (patch.GetValueOrDefault("op")?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(op))
        {
            error = "patch_missing_op";
            return false;
        }

        var path = (patch.GetValueOrDefault("path")?.ToString() ?? string.Empty).Replace("\r", "").Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "patch_missing_path";
            return false;
        }

        // Resolve and validate workspace path
        if (!WorkspacePathGuard.TryResolvePathWithinRoot(workspaceRoot, path, out var fullPath, out var pathError))
        {
            error = pathError;
            return false;
        }

        var relPath = Path.GetRelativePath(workspaceRoot, fullPath!).Replace('\\', '/');

        try
        {
            switch (op)
            {
                case "create_or_replace":
                    return ApplyCreateOrReplace(fullPath!, patch, relPath, out changedRelPath, out error);
                case "replace_span":
                    return ApplyReplaceSpan(fullPath!, patch, relPath, out changedRelPath, out error);
                default:
                    error = $"unsupported_op:{op}";
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[workspace_apply_patch] patch apply failed.");
            error = ex.Message;
            return false;
        }
    }

    private bool ApplyCreateOrReplace(
        string fullPath,
        Dictionary<string, object?> patch,
        string relPath,
        out string? changedRelPath,
        out string? error)
    {
        changedRelPath = null;
        error = null;

        var text = (patch.GetValueOrDefault("text")?.ToString() ?? string.Empty).Replace("\r", "");
        if (text.Length == 0)
        {
            error = "create_or_replace_missing_text";
            return false;
        }

        if (text.Length > MaxTextCharsPerPatch)
        {
            error = "patch_text_too_large";
            return false;
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, text);
        changedRelPath = relPath;
        return true;
    }

    private bool ApplyReplaceSpan(
        string fullPath,
        Dictionary<string, object?> patch,
        string relPath,
        out string? changedRelPath,
        out string? error)
    {
        changedRelPath = null;
        error = null;

        if (!File.Exists(fullPath))
        {
            error = "replace_span_target_missing";
            return false;
        }

        var start = ToInt(patch.GetValueOrDefault("start_line"));
        var endExcl = ToInt(patch.GetValueOrDefault("end_line_exclusive"));
        var replaceText = (patch.GetValueOrDefault("replace_text")?.ToString() ?? string.Empty).Replace("\r", "");

        if (start <= 0 || endExcl <= 0 || endExcl < start)
        {
            error = "replace_span_invalid_range";
            return false;
        }

        if (replaceText.Length == 0)
        {
            error = "replace_span_missing_replace_text";
            return false;
        }

        if (replaceText.Length > MaxTextCharsPerPatch)
        {
            error = "patch_text_too_large";
            return false;
        }

        var original = File.ReadAllText(fullPath).Replace("\r", "");
        var lines = original.Split('\n');

        var startIdx = start - 1;
        var endIdxExcl = endExcl - 1;
        if (startIdx < 0 || startIdx > lines.Length)
        {
            error = "replace_span_start_oob";
            return false;
        }

        if (endIdxExcl < 0 || endIdxExcl > lines.Length)
        {
            error = "replace_span_end_oob";
            return false;
        }

        var replacementLines = replaceText.Split('\n');

        var outLines = new List<string>(capacity: lines.Length - (endIdxExcl - startIdx) + replacementLines.Length);
        outLines.AddRange(lines.Take(startIdx));
        outLines.AddRange(replacementLines);
        outLines.AddRange(lines.Skip(endIdxExcl));

        var next = string.Join('\n', outLines);
        File.WriteAllText(fullPath, next);

        changedRelPath = relPath;
        return true;
    }

    private static int ToInt(object? value)
    {
        try
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                float f => (int)f,
                decimal m => (int)m,
                string s when int.TryParse(s, out var p) => p,
                _ => Convert.ToInt32(value)
            };
        }
        catch
        {
            return 0;
        }
    }

    private object? ResolveValue(object? value, Dictionary<string, object> vars)
    {
        if (value is not string template)
            return value;

        var match = PurePathTemplateRegex.Match(template);
        if (match.Success)
        {
            var path = match.Groups[1].Value.Trim();
            return ResolvePathValue(vars, path);
        }

        return _templateEngine.Render(template, vars);
    }

    private static object? ResolvePathValue(Dictionary<string, object> variables, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        if (!variables.TryGetValue(parts[0], out var current) || current == null)
            return null;

        for (var i = 1; i < parts.Length; i++)
        {
            var key = parts[i];
            current = current switch
            {
                IDictionary<string, object> dict => dict.TryGetValue(key, out var v) ? v : null,
                System.Collections.IDictionary nd => nd.Contains(key) ? nd[key] : null,
                _ => null
            };
            if (current == null) return null;
        }

        return current;
    }

    private static List<Dictionary<string, object?>> NormalizePatchList(object? resolved)
    {
        if (resolved == null) return [];

        // Single patch object
        if (resolved is Dictionary<string, object?> d)
            return [d];
        if (resolved is Dictionary<string, object> d2)
            return [d2.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)];
        if (resolved is System.Collections.IDictionary nd)
        {
            var dd = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry e in nd)
                dd[e.Key?.ToString() ?? ""] = e.Value;
            return [dd];
        }

        // List of patches
        if (resolved is IEnumerable<object> list)
        {
            var outList = new List<Dictionary<string, object?>>();
            foreach (var it in list)
            {
                if (it is Dictionary<string, object?> dd) outList.Add(dd);
                else if (it is Dictionary<string, object> dd2) outList.Add(dd2.ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
                else if (it is System.Collections.IDictionary nd2)
                {
                    var dd3 = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (System.Collections.DictionaryEntry e in nd2)
                        dd3[e.Key?.ToString() ?? ""] = e.Value;
                    outList.Add(dd3);
                }
            }

            return outList;
        }

        if (resolved is System.Collections.IEnumerable e2)
        {
            var outList = new List<Dictionary<string, object?>>();
            foreach (var it in e2)
            {
                if (it is Dictionary<string, object?> dd) outList.Add(dd);
            }

            return outList;
        }

        return [];
    }

    private static string Bound(string? s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= maxChars) return s;
        return s[..maxChars] + "...(truncated)";
    }
}


