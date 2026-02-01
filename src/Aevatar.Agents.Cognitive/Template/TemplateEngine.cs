using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scriban;
using Scriban.Runtime;

namespace Aevatar.Agents.Cognitive.Template;

// ============================================================
//  Template Engine - Based on Scriban
// ============================================================

/// <summary>
/// Template engine for rendering prompt templates
/// Supports Mustache/Handlebars-style syntax
/// </summary>
public partial class TemplateEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Human-readable JSON in prompts:
        // - Avoid \uXXXX for non-ASCII (Chinese / math symbols like ℋ, Φ, ⊗)
        // - Safe here because this JSON is used for LLM prompts, not for HTML/JS embedding.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    /// <summary>
    /// Render template
    /// </summary>
    public string Render(string template, Dictionary<string, object> variables)
    {
        // Preprocess: Convert {{var}} to Scriban's {{ var }}
        var processedTemplate = PreprocessTemplate(template);

        var hasAttachmentsToken = processedTemplate.Contains("attachments", StringComparison.OrdinalIgnoreCase);
        if (hasAttachmentsToken && !variables.ContainsKey("attachments"))
        {
            var sessionId = variables.TryGetValue("session_id", out var sid) ? sid?.ToString() ?? string.Empty : string.Empty;
            var runId = variables.TryGetValue("run_id", out var rid)
                ? rid?.ToString() ?? string.Empty
                : variables.TryGetValue("request_id", out var req)
                    ? req?.ToString() ?? string.Empty
                    : string.Empty;

            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId,
                    runId,
                    hypothesisId = "H3",
                    location = "TemplateEngine.cs:Render",
                    message = "missing_attachments_variable",
                    data = new
                    {
                        templateLength = template.Length,
                        processedLength = processedTemplate.Length,
                        variableKeys = variables.Keys
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion
        }
        
        // Parse template
        var scribanTemplate = Scriban.Template.Parse(processedTemplate);
        if (scribanTemplate.HasErrors)
        {
            var sessionId = variables.TryGetValue("session_id", out var sid) ? sid?.ToString() ?? string.Empty : string.Empty;
            var runId = variables.TryGetValue("run_id", out var rid)
                ? rid?.ToString() ?? string.Empty
                : variables.TryGetValue("request_id", out var req)
                    ? req?.ToString() ?? string.Empty
                    : string.Empty;
            var errorText = string.Join(", ", scribanTemplate.Messages);

            #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId,
                    runId,
                    hypothesisId = "H5",
                    location = "TemplateEngine.cs:Render",
                    message = "template_parse_error",
                    data = new
                    {
                        error = errorText,
                        templateLength = template.Length
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            #endregion

            throw new TemplateParseException(
                $"Template parse error: {string.Join(", ", scribanTemplate.Messages)}");
        }
        
        // Create context
        var scriptObject = new ScriptObject();
        
        // Inject variables
        foreach (var (key, value) in variables)
        {
            scriptObject[key] = ConvertValue(value);
        }
        
        // Inject built-in functions
        RegisterBuiltinFunctions(scriptObject);
        
        var context = new TemplateContext();
        context.PushGlobal(scriptObject);
        
        // Render
        string result;
        try
        {
            result = scribanTemplate.Render(context);
        }
        catch (Exception ex)
        {
            var sessionId = variables.TryGetValue("session_id", out var sid) ? sid?.ToString() ?? string.Empty : string.Empty;
            var runId = variables.TryGetValue("run_id", out var rid)
                ? rid?.ToString() ?? string.Empty
                : variables.TryGetValue("request_id", out var req)
                    ? req?.ToString() ?? string.Empty
                    : string.Empty;

            var attachmentLine = string.Empty;
            var lines = processedTemplate.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("attachments", StringComparison.OrdinalIgnoreCase))
                {
                    attachmentLine = $"{i + 1}:{lines[i].Trim()}";
                    break;
                }
            }

            variables.TryGetValue("attachments", out var attachmentsValue);
            // #region agent log
            System.IO.File.AppendAllText("/Users/zhaoyiqi/Code/aevatar-agent-framework/.cursor/debug.log",
                JsonSerializer.Serialize(new
                {
                    sessionId,
                    runId,
                    hypothesisId = "H8",
                    location = "TemplateEngine.cs:Render",
                    message = "template_render_exception",
                    data = new
                    {
                        exception = ex.Message,
                        attachmentLine,
                        attachmentsType = attachmentsValue?.GetType().Name ?? "missing"
                    },
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }) + Environment.NewLine);
            // #endregion

            throw;
        }
        
        // DEBUG: If rendering result contains empty Task, output debug info
        if (result.Contains("Task:") && result.Contains("Task: \n"))
        {
            System.Diagnostics.Debug.WriteLine($"[TemplateEngine] Empty Task detected!");
            System.Diagnostics.Debug.WriteLine($"[TemplateEngine] Variables: {string.Join(", ", variables.Keys)}");
            System.Diagnostics.Debug.WriteLine($"[TemplateEngine] task in vars: {variables.ContainsKey("task")}");
            if (variables.ContainsKey("task"))
            {
                var taskVal = variables["task"];
                System.Diagnostics.Debug.WriteLine($"[TemplateEngine] task type: {taskVal?.GetType().Name ?? "null"}");
                System.Diagnostics.Debug.WriteLine($"[TemplateEngine] task length: {taskVal?.ToString()?.Length ?? 0}");
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Evaluate expression (for conditional evaluation)
    /// </summary>
    public object? Evaluate(string expression, Dictionary<string, object> variables)
    {
        // Check if expression is already wrapped in {{ }}
        var trimmed = expression.Trim();
        var template = trimmed.StartsWith("{{") && trimmed.EndsWith("}}")
            ? trimmed                              // Already wrapped, use directly
            : $"{{{{ {expression} }}}}";           // Not wrapped, add wrapping
        
        var result = Render(template, variables);
        
        // Try parsing as bool
        if (bool.TryParse(result.Trim(), out var boolResult))
            return boolResult;
        
        // Try parsing as number
        if (double.TryParse(result.Trim(), out var numResult))
            return numResult;
        
        return result;
    }
    
    /// <summary>
    /// Resolve variable reference (e.g., {{items}} → variable name "items")
    /// </summary>
    public object? ResolveValue(object? value, Dictionary<string, object> variables)
    {
        if (value is not string strValue)
            return value;
        
        // Check if it's a simple variable reference
        var match = SimpleVariableRegex().Match(strValue);
        if (match.Success)
        {
            var varName = match.Groups[1].Value.Trim();
            if (variables.TryGetValue(varName, out var resolved))
                return resolved;
        }
        
        // Otherwise render as template
        return Render(strValue, variables);
    }
    
    // ============================================================
    //  Private Methods
    // ============================================================
    
    /// <summary>
    /// Preprocess template: Convert various template syntaxes to Scriban's unified syntax
    /// Supports: Mustache ({{var}}), Handlebars ({{#if}}), Liquid/Jinja2 ({% if %})
    /// </summary>
    private static string PreprocessTemplate(string template)
    {
        var result = template;
        
        // ─────────────────────────────────────────────────────────
        //  1. Liquid/Jinja2 style {% if %} / {% endif %}
        //     Convert to Scriban's {{ if }} / {{ end }}
        // ─────────────────────────────────────────────────────────
        result = LiquidIfRegex().Replace(result, "{{ if $1 }}");
        result = LiquidElseRegex().Replace(result, "{{ else }}");
        result = LiquidEndIfRegex().Replace(result, "{{ end }}");
        result = LiquidForRegex().Replace(result, "{{ for $1 }}");
        result = LiquidEndForRegex().Replace(result, "{{ end }}");
        
        // ─────────────────────────────────────────────────────────
        //  2. Handlebars style {{#if}} / {{/if}}
        // ─────────────────────────────────────────────────────────
        result = IfBlockRegex().Replace(result, "{{ if $1 }}");
        result = EachBlockRegex().Replace(result, "{{ for item in $1 }}");
        result = EndBlockRegex().Replace(result, "{{ end }}");
        result = ElseBlockRegex().Replace(result, "{{ else }}");
        
        // ─────────────────────────────────────────────────────────
        //  3. Liquid filter argument syntax `| filter: arg` → `| filter arg`
        // ─────────────────────────────────────────────────────────
        result = LiquidFilterArgRegex().Replace(result, "$1 ");
        
        // ─────────────────────────────────────────────────────────
        //  4. Pipe filters and simple variables
        // ─────────────────────────────────────────────────────────
        result = PipeFilterRegex().Replace(result, "{{ $1 | $2 }}");
        result = SimpleVarRegex().Replace(result, "{{ $1 }}");
        
        return result;
    }
    
    /// <summary>
    /// Convert value to Scriban-compatible format
    /// Key: Dictionaries must be converted to ScriptObject to support item.property syntax
    /// </summary>
    private static object? ConvertValue(object? value)
    {
        if (value == null) return null;
        
        // Check dictionary type first (must be before IEnumerable, because dictionaries also implement IEnumerable)
        if (value is IDictionary<string, object> dict)
            return ConvertDictToScriptObject(dict);
        
        // Check non-generic dictionary (compatible with different dictionary types)
        if (value is System.Collections.IDictionary nonGenericDict)
            return ConvertNonGenericDictToScriptObject(nonGenericDict);
        
        return value switch
        {
            string s => s,
            bool b => b,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal m => (double)m,
            DateTime dt => dt,
            ScriptObject so => so, // Already ScriptObject, return directly
            IEnumerable<object> list => list.Select(ConvertValue).ToList(),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Select(ConvertValue).ToList(),
            _ => ConvertComplexObject(value)
        };
    }
    
    /// <summary>
    /// Convert non-generic dictionary to ScriptObject
    /// </summary>
    private static ScriptObject ConvertNonGenericDictToScriptObject(System.Collections.IDictionary dict)
    {
        var scriptObj = new ScriptObject();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var key = entry.Key?.ToString() ?? "";
            scriptObj[key] = ConvertValue(entry.Value);
        }
        return scriptObj;
    }
    
    /// <summary>
    /// Convert dictionary to Scriban ScriptObject, supports item.property syntax
    /// </summary>
    private static ScriptObject ConvertDictToScriptObject(IDictionary<string, object> dict)
    {
        var scriptObj = new ScriptObject();
        foreach (var (key, val) in dict)
        {
            scriptObj[key] = ConvertValue(val);
        }
        return scriptObj;
    }
    
    /// <summary>
    /// Convert complex object to ScriptObject
    /// </summary>
    private static ScriptObject ConvertComplexObject(object value)
    {
        var type = value.GetType();
        var scriptObj = new ScriptObject();
        
        foreach (var prop in type.GetProperties())
        {
            if (prop.CanRead)
            {
                var propValue = prop.GetValue(value);
                scriptObj[ToCamelCase(prop.Name)] = ConvertValue(propValue);
            }
        }
        
        return scriptObj;
    }
    
    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
    
    /// <summary>
    /// Register built-in functions
    /// </summary>
    private static void RegisterBuiltinFunctions(ScriptObject scriptObject)
    {
        // json filter
        scriptObject.Import("json", new Func<object?, string>(obj => 
            JsonSerializer.Serialize(obj, JsonOptions)));
        
        // length filter
        scriptObject.Import("length", new Func<object?, int>(obj => obj switch
        {
            string s => s.Length,
            ICollection<object> c => c.Count,
            IEnumerable<object> e => e.Count(),
            _ => 0
        }));
        
        // truncate filter
        scriptObject.Import("truncate", new Func<string?, int, string>((s, len) => 
            s == null ? "" : s.Length <= len ? s : s[..len] + "..."));
        
        // uppercase/lowercase filter
        scriptObject.Import("uppercase", new Func<string?, string>(s => s?.ToUpperInvariant() ?? ""));
        scriptObject.Import("lowercase", new Func<string?, string>(s => s?.ToLowerInvariant() ?? ""));
        
        // is_atomic function (for MAKER to determine if task is atomic)
        scriptObject.Import("is_atomic", new Func<string?, bool>(task => 
            task != null && task.Length < 200 && !task.Contains("and") && !task.Contains("then")));
        
        // consensus_k function (returns K value based on reliability)
        // Avoid overwriting workflow variable `consensus_k` when provided.
        if (!scriptObject.ContainsKey("consensus_k"))
        {
            scriptObject.Import("consensus_k", new Func<string?, int>(reliability => reliability?.ToLowerInvariant() switch
            {
                "low" => 1,
                "medium" => 2,
                "high" => 3,
                _ => 2
            }));
        }
        
        // contains function - string contains check
        // Usage: {{ atomic_check | contains 'ATOMIC' }}
        scriptObject.Import("contains", new Func<string?, string?, bool>((str, substring) => 
            str != null && substring != null && str.Contains(substring, StringComparison.OrdinalIgnoreCase)));
        
        // default function - default value
        // Usage: {{ value | default 'fallback' }}
        scriptObject.Import("default", new Func<object?, object?, object?>((value, defaultValue) => 
            value is null or "" ? defaultValue : value));
        
        // size function - collection size
        // Usage: {{ items | size }}
        scriptObject.Import("size", new Func<object?, int>(obj => obj switch
        {
            string s => s.Length,
            System.Collections.ICollection c => c.Count,
            System.Collections.IEnumerable e => e.Cast<object>().Count(),
            _ => 0
        }));
    }
    
    // ============================================================
    //  Regular Expressions
    // ============================================================
    
    // Liquid/Jinja2 style: {% if %}, {% else %}, {% endif %}, {% for %}, {% endfor %}
    [GeneratedRegex(@"\{%\s*if\s+(.+?)\s*%\}")]
    private static partial Regex LiquidIfRegex();
    
    [GeneratedRegex(@"\{%\s*else\s*%\}")]
    private static partial Regex LiquidElseRegex();
    
    [GeneratedRegex(@"\{%\s*endif\s*%\}")]
    private static partial Regex LiquidEndIfRegex();
    
    [GeneratedRegex(@"\{%\s*for\s+(.+?)\s*%\}")]
    private static partial Regex LiquidForRegex();
    
    [GeneratedRegex(@"\{%\s*endfor\s*%\}")]
    private static partial Regex LiquidEndForRegex();
    
    // Handlebars style: {{#if}}, {{/if}}
    [GeneratedRegex(@"\{\{#if\s+(.+?)\}\}")]
    private static partial Regex IfBlockRegex();
    
    [GeneratedRegex(@"\{\{#each\s+(.+?)\}\}")]
    private static partial Regex EachBlockRegex();
    
    [GeneratedRegex(@"\{\{/(if|each)\}\}")]
    private static partial Regex EndBlockRegex();
    
    [GeneratedRegex(@"\{\{else\}\}")]
    private static partial Regex ElseBlockRegex();
    
    // NOTE:
    // - `|` in Scriban is used for filter / pipe expressions (value | filter).
    // - `||` is a boolean operator, and MUST NOT be treated as a pipe.
    // - Our previous regex matched `||` accidentally (because it allowed 0 whitespace), which
    //   caused expressions like `a || b` to be rewritten and crash with errors like:
    //   "Invalid target function `True` (bool)".
    [GeneratedRegex(@"\{\{(.+?)\s*(?<!\|)\|(?!\|)\s*(.+?)\}\}")]
    private static partial Regex PipeFilterRegex();
    
    // Convert Liquid-style filter arguments: `filter: arg` → `filter arg`
    // Matches: `contains: 'ATOMIC'` → `contains 'ATOMIC'`
    [GeneratedRegex(@"(\|\s*\w+):\s*")]
    private static partial Regex LiquidFilterArgRegex();
    
    [GeneratedRegex(@"\{\{([^#/][^}]*?)\}\}")]
    private static partial Regex SimpleVarRegex();
    
    [GeneratedRegex(@"^\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}$")]
    private static partial Regex SimpleVariableRegex();
}

// ============================================================
//  Exceptions
// ============================================================

public class TemplateParseException : Exception
{
    public TemplateParseException(string message) : base(message) { }
}

