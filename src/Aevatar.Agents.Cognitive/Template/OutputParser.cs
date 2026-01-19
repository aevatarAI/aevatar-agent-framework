using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aevatar.Agents.Cognitive.Template;

// ============================================================
//  Output Parser Interfaces
// ============================================================

/// <summary>
/// Output parser interface
/// </summary>
public interface IOutputParser
{
    /// <summary>Parse LLM output</summary>
    object? Parse(string content);
}

/// <summary>
/// Generic output parser interface
/// </summary>
public interface IOutputParser<out T> : IOutputParser
{
    /// <summary>Parse LLM output</summary>
    new T? Parse(string content);
    
    object? IOutputParser.Parse(string content) => Parse(content);
}

// ============================================================
//  Output Parser Factory
// ============================================================

/// <summary>
/// Output parser factory
/// </summary>
public partial class OutputParserFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    /// <summary>
    /// Create parser based on output type
    /// </summary>
    /// <param name="outputType">Output type string, e.g., "text", "json", "json_array", "regex(...)"</param>
    public IOutputParser Create(string outputType)
    {
        // Parse type string
        var trimmed = outputType.Trim().ToLowerInvariant();
        
        // text
        if (trimmed == "text")
            return new TextOutputParser();
        
        // first_line
        if (trimmed == "first_line")
            return new FirstLineOutputParser();
        
        // code_block
        if (trimmed == "code_block")
            return new CodeBlockOutputParser();
        
        // json or json<TypeName>
        if (trimmed.StartsWith("json"))
        {
            // json_array<TypeName>
            if (trimmed.StartsWith("json_array"))
                return new JsonArrayOutputParser();
            
            // json<TypeName> or json
            return new JsonOutputParser();
        }
        
        // regex("pattern")
        var regexMatch = RegexTypePattern().Match(outputType);
        if (regexMatch.Success)
        {
            var pattern = regexMatch.Groups[1].Value;
            return new RegexOutputParser(pattern);
        }
        
        // Default return text parser
        return new TextOutputParser();
    }
    
    /// <summary>
    /// Create generic parser
    /// </summary>
    public IOutputParser<T> Create<T>()
    {
        var type = typeof(T);
        
        if (type == typeof(string))
            return (IOutputParser<T>)(object)new TextOutputParser();
        
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return (IOutputParser<T>)(object)new JsonArrayOutputParser();
        
        return (IOutputParser<T>)(object)new JsonOutputParser();
    }
    
    [GeneratedRegex(@"regex\([""'](.+?)[""']\)")]
    private static partial Regex RegexTypePattern();
}

// ============================================================
//  Concrete Parser Implementations
// ============================================================

/// <summary>
/// Text parser - Return original text directly
/// </summary>
public class TextOutputParser : IOutputParser<string>
{
    public string? Parse(string content) => content?.Trim();
}

/// <summary>
/// First line parser - Return first line
/// </summary>
public class FirstLineOutputParser : IOutputParser<string>
{
    public string? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0].Trim() : null;
    }
}

/// <summary>
/// Code block parser - Extract content wrapped in ```
/// </summary>
public partial class CodeBlockOutputParser : IOutputParser<string>
{
    public string? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        var match = CodeBlockPattern().Match(content);
        return match.Success ? match.Groups[1].Value.Trim() : content.Trim();
    }
    
    [GeneratedRegex(@"```(?:\w+)?\s*([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex CodeBlockPattern();
}

/// <summary>
/// JSON object parser
/// Returns Dictionary so template engine can access properties
/// </summary>
public partial class JsonOutputParser : IOutputParser<object>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public object? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        // Try extracting JSON (may be wrapped in code block)
        var json = ExtractJson(content);
        
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json, Options);
            return ConvertJsonElement(element);
        }
        catch (JsonException)
        {
            // Parse failed: Try repairing common LLM "pseudo JSON" issues (especially LaTeX backslashes in proof)
            // NOTE:
            // - Cannot return failed JSON as string, otherwise downstream will treat state as string and continue,
            //   eventually crash when conditional accesses state.xxx (manifesting as "unexplained interruption")
            var repaired = TryRepairJson(json);
            if (repaired != null)
            {
                try
                {
                    var element2 = JsonSerializer.Deserialize<JsonElement>(repaired, Options);
                    return ConvertJsonElement(element2);
                }
                catch (JsonException)
                {
                    // fall through
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Best-effort JSON repair for LLM outputs.
    /// Handles common issues:
    /// - Unescaped backslashes inside JSON string literals (e.g. LaTeX "\mathcal{H}")
    /// - Raw newline/tab characters inside JSON string literals
    /// </summary>
    private static string? TryRepairJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // Only attempt repair for JSON objects
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            return null;

        var sb = new System.Text.StringBuilder(trimmed.Length + 32);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];

            if (!inString)
            {
                if (c == '"')
                {
                    inString = true;
                    escaped = false;
                }
                sb.Append(c);
                continue;
            }

            // in string
            if (escaped)
            {
                // preserve the escape as-is
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                // Check if this is a valid JSON escape. If not, escape the backslash itself.
                var next = i + 1 < trimmed.Length ? trimmed[i + 1] : '\0';
                var valid = next is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u';
                if (!valid)
                {
                    sb.Append("\\\\"); // turn "\" into "\\"
                }
                else
                {
                    sb.Append('\\');
                    escaped = true;
                }
                continue;
            }

            if (c == '"')
            {
                inString = false;
                sb.Append(c);
                continue;
            }

            // Raw control chars are illegal inside JSON strings; escape them.
            if (c == '\n')
            {
                sb.Append("\\n");
                continue;
            }
            if (c == '\r')
            {
                sb.Append("\\r");
                continue;
            }
            if (c == '\t')
            {
                sb.Append("\\t");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// Convert JsonElement to CLR types (dictionary/list/primitive values)
    /// </summary>
    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }
    
    private static string ExtractJson(string content)
    {
        // Try extracting JSON from code block
        var codeBlockMatch = JsonCodeBlockPattern().Match(content);
        if (codeBlockMatch.Success)
        {
            var extracted = codeBlockMatch.Groups[1].Value.Trim();
            return CleanDuplicateFragments(extracted);
        }
        
        // Try finding JSON object boundaries with proper bracket matching
        var start = content.IndexOf('{');
        if (start < 0)
            return content.Trim();
        
        // Find the matching closing brace by counting brackets
        var depth = 0;
        var inString = false;
        var escaped = false;
        var end = -1;
        
        for (var i = start; i < content.Length; i++)
        {
            var c = content[i];
            
            if (escaped)
            {
                escaped = false;
                continue;
            }
            
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            
            if (inString)
                continue;
            
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }
        }
        
        if (end > start)
        {
            // Bracket matching succeeded, extract ONLY the first complete JSON object
            var extracted = content[start..(end + 1)].Trim();
            
            // CRITICAL: After extracting the first complete object, check if there's more content
            // If there is, it's likely a duplicate - ignore it completely
            if (end + 1 < content.Length)
            {
                var afterFirstObject = content[(end + 1)..].Trim();
                
                // If remaining content contains JSON field names or looks like duplicate fragments, ignore it
                if (afterFirstObject.Length > 0)
                {
                    // Check for common patterns that indicate partial JSON fragments
                    var isPartialJson = 
                        // Starts with JSON structure characters
                        afterFirstObject.StartsWith('{') ||
                        afterFirstObject.StartsWith('}') ||
                        afterFirstObject.StartsWith('[') ||
                        afterFirstObject.StartsWith(']') ||
                        afterFirstObject.StartsWith("\":") ||
                        afterFirstObject.StartsWith("\",") ||
                        afterFirstObject.StartsWith("_") ||
                        // Contains JSON field names (indicates duplicate fields)
                        afterFirstObject.Contains("\"depends_on\"") ||
                        afterFirstObject.Contains("\"factor_sequence\"") ||
                        afterFirstObject.Contains("\"proposed_b\"") ||
                        afterFirstObject.Contains("\"worker_id\"") ||
                        afterFirstObject.Contains("\"accept\"") ||
                        afterFirstObject.Contains("\"proof\"") ||
                        afterFirstObject.Contains("\"gap_or_counterexample\"") ||
                        afterFirstObject.Contains("_counterexample\"") ||
                        // Pattern: array value followed by comma and field (e.g., ["O5"], "proposed_b": [])
                        (afterFirstObject.StartsWith('[') && afterFirstObject.Contains(',') && afterFirstObject.Contains('"')) ||
                        // Pattern: field name with colon (e.g., "proposed_b": [])
                        (afterFirstObject.StartsWith('"') && afterFirstObject.Contains(':')) ||
                        // Pattern: closing bracket/brace followed by more content
                        (afterFirstObject.StartsWith(']') && afterFirstObject.Length > 1) ||
                        (afterFirstObject.StartsWith('}') && afterFirstObject.Length > 1);
                    
                    if (isPartialJson)
                    {
                        // Likely a duplicate fragment, return only the first complete object (after cleaning)
                        return CleanDuplicateFragments(extracted);
                    }
                }
            }
            
            // Always clean the extracted JSON to remove any trailing issues
            return CleanDuplicateFragments(extracted);
        }
        
        // Fallback: use LastIndexOf if bracket matching fails
        var lastEnd = content.LastIndexOf('}');
        if (lastEnd > start)
        {
            var extracted = content[start..(lastEnd + 1)].Trim();
            return CleanDuplicateFragments(extracted);
        }
        
        return CleanDuplicateFragments(content.Trim());
    }
    
    /// <summary>
    /// Clean duplicate JSON fragments from extracted JSON string.
    /// Removes trailing extra braces, non-JSON characters, and duplicate field fragments.
    /// </summary>
    private static string CleanDuplicateFragments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;
        
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{'))
            return trimmed;
        
        // Find the first complete JSON object
        var depth = 0;
        var inString = false;
        var escaped = false;
        var firstObjectEnd = -1;
        
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            
            if (escaped)
            {
                escaped = false;
                continue;
            }
            
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            
            if (inString)
                continue;
            
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    firstObjectEnd = i;
                    break;
                }
            }
        }
        
        // If we found a complete JSON object and there's content after it, check for duplicates
        if (firstObjectEnd >= 0 && firstObjectEnd < trimmed.Length - 1)
        {
            var firstObject = trimmed[..(firstObjectEnd + 1)];
            var remaining = trimmed[(firstObjectEnd + 1)..].Trim();
            
            // If remaining content looks like a duplicate fragment, return only the first object (after cleaning)
            if (remaining.Length > 0)
            {
                // Check for common patterns that indicate partial JSON fragments
                var isPartialJson = 
                    // Starts with JSON structure characters
                    remaining.StartsWith('{') ||
                    remaining.StartsWith('}') ||
                    remaining.StartsWith('[') ||
                    remaining.StartsWith(']') ||
                    remaining.StartsWith("\":") ||
                    remaining.StartsWith("\",") ||
                    remaining.StartsWith("_") ||
                    // Contains JSON field names (indicates duplicate fields)
                    remaining.Contains("\"depends_on\"") ||
                    remaining.Contains("\"factor_sequence\"") ||
                    remaining.Contains("\"proposed_b\"") ||
                    remaining.Contains("\"worker_id\"") ||
                    remaining.Contains("\"accept\"") ||
                    remaining.Contains("\"proof\"") ||
                    remaining.Contains("\"gap_or_counterexample\"") ||
                    remaining.Contains("_counterexample\"") ||
                    // Short fragments that look like partial JSON
                    (remaining.StartsWith("[]") && remaining.Length <= 5) ||
                    (remaining.StartsWith("]") && remaining.Length <= 3) ||
                    // Pattern: array value followed by comma and field (e.g., ["O5"], "proposed_b": [])
                    (remaining.StartsWith('[') && remaining.Contains(',') && remaining.Contains('"')) ||
                    // Pattern: field name with colon (e.g., "proposed_b": [])
                    (remaining.StartsWith('"') && remaining.Contains(':')) ||
                    // Pattern: closing bracket/brace followed by more content
                    (remaining.StartsWith(']') && remaining.Length > 1) ||
                    (remaining.StartsWith('}') && remaining.Length > 1);
                
                if (isPartialJson)
                {
                    // Clean the first object before returning (remove any trailing issues)
                    firstObject = RemoveTrailingExtraBraces(firstObject);
                    firstObject = RemoveTrailingNonJsonChars(firstObject);
                    return firstObject;
                }
            }
        }
        
        // Remove trailing extra closing braces
        trimmed = RemoveTrailingExtraBraces(trimmed);
        
        // Remove trailing non-JSON characters
        trimmed = RemoveTrailingNonJsonChars(trimmed);
        
        return trimmed;
    }
    
    /// <summary>
    /// Remove trailing extra closing braces from JSON string.
    /// </summary>
    private static string RemoveTrailingExtraBraces(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;
        
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{'))
            return trimmed;
        
        // Count braces (ignoring those inside strings)
        var openCount = 0;
        var closeCount = 0;
        var inString = false;
        var escaped = false;
        
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            
            if (escaped)
            {
                escaped = false;
                continue;
            }
            
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            
            if (inString)
                continue;
            
            if (c == '{')
                openCount++;
            else if (c == '}')
                closeCount++;
        }
        
        // Remove trailing extra closing braces
        while (closeCount > openCount && trimmed.EndsWith("}"))
        {
            trimmed = trimmed[..^1].TrimEnd();
            closeCount--;
        }
        
        return trimmed;
    }
    
    /// <summary>
    /// Remove trailing non-JSON characters after a valid JSON object.
    /// </summary>
    private static string RemoveTrailingNonJsonChars(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;
        
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{'))
            return trimmed;
        
        // Find the last valid closing brace position
        var depth = 0;
        var inString = false;
        var escaped = false;
        var lastValidClose = -1;
        
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            
            if (escaped)
            {
                escaped = false;
                continue;
            }
            
            if (c == '\\')
            {
                escaped = true;
                continue;
            }
            
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
            
            if (inString)
                continue;
            
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    lastValidClose = i;
                    break;
                }
            }
        }
        
        // If we found a valid closing brace, return only up to that point
        if (lastValidClose >= 0)
        {
            if (lastValidClose < trimmed.Length - 1)
            {
                // Return only up to the valid closing brace, removing all trailing content
                return trimmed[..(lastValidClose + 1)];
            }
            return trimmed;
        }
        
        return trimmed;
    }
    
    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonCodeBlockPattern();
}

/// <summary>
/// JSON array parser
/// Returns List&lt;Dictionary&gt; so template engine can access properties (e.g., item.description)
/// </summary>
public partial class JsonArrayOutputParser : IOutputParser<List<object>>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public List<object>? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        // Try extracting JSON array
        var json = ExtractJsonArray(content);
        
        try
        {
            var array = JsonSerializer.Deserialize<JsonElement>(json, Options);
            if (array.ValueKind == JsonValueKind.Array)
            {
                // Convert to dictionary list so template engine can access properties
                return array.EnumerateArray()
                    .Select(ConvertJsonElement)
                    .ToList();
            }
            return [ConvertJsonElement(array)];
        }
        catch (JsonException)
        {
            // Parse failed: Try repairing common LLM "pseudo JSON" issues (especially LaTeX backslashes)
            // NOTE:
            // - json_array often used for proposed_b / candidate pools; these strings often contain "\mathcal{H}" etc.
            // - If not repaired, will cause strict_parse=true to directly redflag-parse-null, workflow fails "seemingly unrelatedly" in conditional
            var repaired = TryRepairJson(json);
            if (repaired != null)
            {
                try
                {
                    var array2 = JsonSerializer.Deserialize<JsonElement>(repaired, Options);
                    if (array2.ValueKind == JsonValueKind.Array)
                    {
                        return array2.EnumerateArray()
                            .Select(ConvertJsonElement)
                            .ToList();
                    }
                    return [ConvertJsonElement(array2)];
                }
                catch (JsonException)
                {
                    // fall through
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Best-effort JSON repair for LLM outputs (array/object).
    /// Handles common issues:
    /// - Unescaped backslashes inside JSON string literals (e.g. LaTeX "\mathcal{H}")
    /// - Raw newline/tab characters inside JSON string literals
    /// </summary>
    private static string? TryRepairJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        var trimmed = json.Trim();
        if (!((trimmed.StartsWith('[') && trimmed.EndsWith(']')) ||
              (trimmed.StartsWith('{') && trimmed.EndsWith('}'))))
        {
            return null;
        }

        var sb = new System.Text.StringBuilder(trimmed.Length + 32);
        var inString = false;
        var escaped = false;

        static bool IsValidJsonEscape(char c)
            => c is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u';

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];

            if (!inString)
            {
                if (c == '"')
                {
                    inString = true;
                    escaped = false;
                }
                sb.Append(c);
                continue;
            }

            // In string
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '"')
            {
                inString = false;
                sb.Append(c);
                continue;
            }

            if (c == '\\')
            {
                var next = i + 1 < trimmed.Length ? trimmed[i + 1] : '\0';
                if (IsValidJsonEscape(next))
                {
                    sb.Append(c);
                    escaped = true;
                }
                else
                {
                    // Invalid escape like "\m" → repair as "\\m"
                    sb.Append("\\\\");
                }
                continue;
            }

            // Raw control chars in string
            if (c == '\n')
            {
                sb.Append("\\n");
                continue;
            }
            if (c == '\r')
            {
                sb.Append("\\r");
                continue;
            }
            if (c == '\t')
            {
                sb.Append("\\t");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// Convert JsonElement to CLR types (dictionary/list/primitive values)
    /// </summary>
    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }
    
    private static string ExtractJsonArray(string content)
    {
        // Try extracting JSON from code block
        var codeBlockMatch = JsonCodeBlockPattern().Match(content);
        if (codeBlockMatch.Success)
            return codeBlockMatch.Groups[1].Value.Trim();
        
        // Try finding JSON array boundaries
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        
        if (start >= 0 && end > start)
            return content[start..(end + 1)];
        
        return content.Trim();
    }
    
    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonCodeBlockPattern();
}

/// <summary>
/// Regular expression parser
/// </summary>
public class RegexOutputParser : IOutputParser<string>
{
    private readonly Regex _pattern;
    
    public RegexOutputParser(string pattern)
    {
        _pattern = new Regex(pattern, RegexOptions.Compiled);
    }
    
    public string? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        var match = _pattern.Match(content);
        if (!match.Success)
            return null;
        
        // If there are capture groups, return first capture group
        // Otherwise return entire match
        return match.Groups.Count > 1 
            ? match.Groups[1].Value 
            : match.Value;
    }
}

/// <summary>
/// Fallback parser - Try multiple parsers in order
/// </summary>
public class FallbackOutputParser : IOutputParser
{
    private readonly IOutputParser[] _parsers;
    
    public FallbackOutputParser(params IOutputParser[] parsers)
    {
        _parsers = parsers;
    }
    
    public object? Parse(string content)
    {
        foreach (var parser in _parsers)
        {
            try
            {
                var result = parser.Parse(content);
                if (result != null)
                    return result;
            }
            catch
            {
                // Continue trying next parser
            }
        }
        
        return content;
    }
}
