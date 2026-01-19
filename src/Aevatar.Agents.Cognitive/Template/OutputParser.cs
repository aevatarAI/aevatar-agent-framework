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
        
        // Ensure JSON is cleaned (remove trailing extra braces, non-JSON chars, and duplicate fragments)
        json = RemoveTrailingExtraBraces(json);
        json = RemoveTrailingNonJsonChars(json);
        json = RemoveDuplicateJsonFragments(json);
        
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
                // Also clean the repaired JSON
                repaired = RemoveTrailingExtraBraces(repaired);
                repaired = RemoveTrailingNonJsonChars(repaired);
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
            return codeBlockMatch.Groups[1].Value.Trim();
        
        // Try extracting JSON from <json>...</json> tags
        var jsonTagMatch = JsonTagPattern().Match(content);
        if (jsonTagMatch.Success)
            return jsonTagMatch.Groups[1].Value.Trim();
        
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
            // This ensures we never return duplicate JSON objects or fragments
            var extracted = content[start..(end + 1)].Trim();
            
            // CRITICAL: After extracting the first complete object, check if there's more content
            // If there is, it's likely a duplicate - ignore it completely
            if (end + 1 < content.Length)
            {
                var afterFirstObject = content[(end + 1)..].Trim();
                
                // Check if the remaining content looks like a duplicate fragment
                // Common patterns:
                // 1. }": ["O2", "O3", "O4"], (incomplete field repetition)
                // 2. }"factor_sequence": [...] (field repetition)
                // 3. }{...} (another JSON object)
                if (afterFirstObject.Length > 0)
                {
                    // If it starts with } followed by quote/colon or field name, it's a duplicate fragment
                    if (afterFirstObject.StartsWith("\":") || 
                        afterFirstObject.StartsWith("\",") ||
                        afterFirstObject.StartsWith("\"depends_on") ||
                        afterFirstObject.StartsWith("\"factor_sequence") ||
                        afterFirstObject.StartsWith("\"proposed_b") ||
                        afterFirstObject.StartsWith("}") ||
                        (afterFirstObject.StartsWith("[") && afterFirstObject.Contains("\"")))
                    {
                        // Likely a duplicate fragment, return only the first complete object
                        return extracted;
                    }
                }
            }
            
            // Final cleanup: remove any trailing extra closing braces
            extracted = RemoveTrailingExtraBraces(extracted);
            // Additional cleanup: remove any trailing non-JSON characters (e.g., "0}", "}", numbers, etc.)
            extracted = RemoveTrailingNonJsonChars(extracted);
            // Remove any duplicate JSON fragments that might appear after the first valid JSON object
            extracted = RemoveDuplicateJsonFragments(extracted);
            return extracted;
        }
        
        // Fallback: use LastIndexOf if bracket matching fails
        // But try to find the FIRST complete object by looking for the first balanced brace pair
        end = content.LastIndexOf('}');
        if (end > start)
        {
            // Try to find the first balanced JSON object by working backwards
            // Find the first { before the last }
            var firstOpenBeforeLastClose = content.LastIndexOf('{', end);
            if (firstOpenBeforeLastClose >= start && firstOpenBeforeLastClose < end)
            {
                // Check if this forms a balanced pair (simple heuristic)
                var potentialJson = content[firstOpenBeforeLastClose..(end + 1)].Trim();
                var openCount = potentialJson.Count(c => c == '{');
                var closeCount = potentialJson.Count(c => c == '}');
                
                // If roughly balanced, use this as the first object
                if (Math.Abs(openCount - closeCount) <= 2) // Allow some tolerance for nested objects
                {
                    var extracted = potentialJson;
                    extracted = RemoveTrailingExtraBraces(extracted);
                    extracted = RemoveTrailingNonJsonChars(extracted);
                    extracted = RemoveDuplicateJsonFragments(extracted);
                    return extracted;
                }
            }
            
            var extracted2 = content[start..(end + 1)].Trim();
            // Remove trailing extra closing braces (common LLM mistake: }} instead of })
            extracted2 = RemoveTrailingExtraBraces(extracted2);
            // Additional cleanup: remove any trailing non-JSON characters
            extracted2 = RemoveTrailingNonJsonChars(extracted2);
            extracted2 = RemoveDuplicateJsonFragments(extracted2);
            return extracted2;
        }
        
        var trimmed = content.Trim();
        // Even if bracket matching failed, try to clean trailing non-JSON chars
        trimmed = RemoveTrailingNonJsonChars(trimmed);
        return trimmed;
    }
    
    /// <summary>
    /// Remove trailing extra closing braces from JSON string.
    /// Ensures the JSON has balanced braces.
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
    /// Remove duplicate JSON fragments that appear after a valid JSON object.
    /// Handles cases like: 
    /// - {"all_proven": true}all_proven": true}
    /// - {"key": "value"}{"key": "value"}
    /// - Partial duplicates like: }"key": "value"} or }``` the exclusion of 1.", "depends_on": ["O1"], "factor_sequence": ["O1"]}
    /// </summary>
    private static string RemoveDuplicateJsonFragments(string json)
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
            
            // CRITICAL: If remaining content contains JSON field names that appear in the first object,
            // it's almost certainly a duplicate fragment - return immediately
            if (remaining.Length > 0 && 
                (remaining.Contains("\"depends_on\"") || 
                 remaining.Contains("\"factor_sequence\"") ||
                 remaining.Contains("\"proposed_b\"") ||
                 remaining.Contains("\"worker_id\"") ||
                 remaining.Contains("\"accept\"") ||
                 remaining.Contains("\"proof\"") ||
                 remaining.Contains("\"gap_or_counterexample\"") ||
                 remaining.Contains("_counterexample\"") ||  // Partial field name (e.g., _counterexample": "")
                 remaining.Contains("\"statement\"") ||
                 remaining.Contains("\"motivation\"")))
            {
                // Likely a duplicate fragment with JSON field names, return only the first complete object
                return firstObject;
            }
            
            // Check for patterns like: }_counterexample": "", (partial field name continuation)
            // This indicates a field name fragment is being repeated
            var trimmedRemaining = remaining.Trim();
            if (trimmedRemaining.StartsWith("_"))
            {
                // Pattern: }_fieldname": ... - partial field name fragment
                // This is almost certainly a duplicate fragment
                return firstObject;
            }
            
            // Check for patterns where a field name fragment appears after the closing brace
            // Pattern: }_counterexample": "", "depends_on": ...
            if (trimmedRemaining.Contains("_counterexample\"") || 
                trimmedRemaining.Contains("_sequence\"") ||
                trimmedRemaining.Contains("_on\""))
            {
                // Likely a duplicate fragment with partial field names
                return firstObject;
            }
            
            // If remaining content starts with {, it's likely a second JSON object (duplicate)
            // Extract and validate it, but always return only the first complete object
            if (remaining.StartsWith('{'))
            {
                // Likely a duplicate JSON object, return only the first complete object
                return firstObject;
            }
            
            // Check if remaining content looks like a duplicate JSON fragment
            // Common patterns: 
            // 1. }key": value} or {"key": value}
            // 2. }``` text...", "key": value} (partial duplicate with markdown code block)
            // 3. }"key": value} (partial duplicate starting with closing brace and quote)
            if (remaining.StartsWith('}'))
            {
                // Likely a duplicate fragment, return only the first complete object
                return firstObject;
            }
            
            // Check for partial duplicates that start with closing brace and quote or markdown
            // Pattern: }"key": value} or }``` text...", "key": value}
            if (remaining.Length > 2 && 
                (remaining.StartsWith("}\"") || 
                 remaining.StartsWith("}```") ||
                 remaining.StartsWith("}`")))
            {
                // Likely a partial duplicate fragment, return only the first complete object
                return firstObject;
            }
            
            // Check for patterns like: } the exclusion of 1.", "depends_on": ["O1"], "factor_sequence": ["O1"]}
            // Or: }": ["O2", "O3", "O4"], "factor_sequence": ["O2", "O3", "O4"], "proposed_b": []}
            // Or: } ["O5"], "proposed_b": [] }
            // This indicates a partial duplicate where the end of a string field or last fields are repeated
            if (remaining.Contains("\"depends_on\"") || 
                remaining.Contains("\"factor_sequence\"") ||
                remaining.Contains("\"proposed_b\"") ||
                remaining.Contains("\"statement\"") ||
                remaining.Contains("\"motivation\""))
            {
                // Likely a partial duplicate with JSON field names, return only the first complete object
                return firstObject;
            }
            
            // Check for patterns like: } ["O5"], "proposed_b": [] }
            // This indicates the last field values (array and empty array) are being repeated
            var trimmedRemaining = remaining.Trim();
            if (trimmedRemaining.StartsWith("[") && trimmedRemaining.Contains("\"proposed_b\""))
            {
                // Pattern: } [array], "proposed_b": [] }
                return firstObject;
            }
            
            // Check for patterns like: } ["O5"], (array value followed by comma and field)
            // This indicates a field value (array) is being repeated
            if (trimmedRemaining.StartsWith("[") && trimmedRemaining.Contains(",") && trimmedRemaining.Contains("\""))
            {
                // Check if it looks like a field continuation (has comma and quote after array)
                var commaIndex = trimmedRemaining.IndexOf(',');
                var afterComma = trimmedRemaining.Substring(commaIndex + 1).Trim();
                if (afterComma.StartsWith("\""))
                {
                    // Pattern: } [array], "field": ...
                    return firstObject;
                }
            }
            
            // Check for patterns like: }": ["O2", "O3", "O4"], (field value continuation after closing brace)
            // This indicates the last field's value is being repeated
            if (remaining.StartsWith("\":") || 
                remaining.StartsWith("\","))
            {
                // Likely a duplicate field value fragment, return only the first complete object
                return firstObject;
            }
            
            // Check for array patterns that might be duplicate field values
            // Pattern: }["O2", "O3", "O4"], or } ["O2", "O3", "O4"], or }[]}
            if (remaining.StartsWith("[") || remaining.TrimStart().StartsWith("["))
            {
                // Check if it looks like a JSON array (field value)
                if (remaining.Contains("]"))
                {
                    // Likely a duplicate array value, return only the first complete object
                    return firstObject;
                }
            }
            
            // Check for patterns like: }[]} or } [] } (empty array followed by closing brace)
            // This indicates the last field's value (empty array) and closing brace are being repeated
            var trimmedRemaining = remaining.Trim();
            if (trimmedRemaining.StartsWith("[]"))
            {
                var afterBrackets = trimmedRemaining.Substring(2).Trim();
                if (afterBrackets.StartsWith("}") || afterBrackets == "}")
                {
                    // Pattern: }[]} - duplicate empty array and closing brace
                    return firstObject;
                }
            }
            
            // Check for patterns like: }]} or } ] } (array closing bracket followed by object closing brace)
            // This indicates a duplicate array closing and object closing
            if (trimmedRemaining.StartsWith("]"))
            {
                var afterBracket = trimmedRemaining.Substring(1).Trim();
                if (afterBracket.StartsWith("}") || afterBracket == "}")
                {
                    // Pattern: }]} - duplicate array closing and object closing
                    return firstObject;
                }
            }
            
            // Check for patterns like: }]} or }[]} with whitespace variations
            // Also handle cases where there might be multiple closing brackets/braces
            if (trimmedRemaining.Length <= 5) // Short fragments like "]}", "[]}", "]}", etc.
            {
                // Check if it's just closing brackets/braces (likely duplicate)
                var allClosing = trimmedRemaining.All(c => c == ']' || c == '}' || char.IsWhiteSpace(c));
                if (allClosing && (trimmedRemaining.Contains(']') || trimmedRemaining.Contains('}')))
                {
                    // Likely duplicate closing brackets/braces
                    return firstObject;
                }
            }
            
            // Check for patterns like: }"status": "running"} or }status": "running"}
            // This indicates the last field of the JSON object is being repeated
            if (remaining.Contains("\"status\"") || 
                remaining.Contains("status\"") ||
                remaining.Contains("\"done\"") ||
                remaining.Contains("\"iteration\"") ||
                remaining.Contains("\"history\"") ||
                remaining.Contains("\"last_judgement\"") ||
                remaining.Contains("\"last_b_pool\"") ||
                remaining.Contains("\"last_worker_verdicts\"") ||
                remaining.Contains("\"last_candidate\""))
            {
                // Likely a partial duplicate with the last field(s) of the JSON object, return only the first complete object
                return firstObject;
            }
            
            // Check for patterns like: }"running"} or }"completed"}
            // This indicates just the value of the last field is being repeated
            if (remaining.Contains("\"running\"") || 
                remaining.Contains("\"completed\"") ||
                remaining.Contains("\"failed\"") ||
                remaining.Contains("\"limit\""))
            {
                // Likely a partial duplicate with status values, return only the first complete object
                return firstObject;
            }
            
            // Check for common text fragments that appear after JSON (LLM commentary)
            // Patterns like: "is an unproven claim.", "This is...", etc.
            if (remaining.Length > 0 && 
                (remaining.StartsWith("is ") ||
                 remaining.StartsWith("This ") ||
                 remaining.StartsWith("The ") ||
                 remaining.StartsWith("It ") ||
                 remaining.StartsWith("Note:") ||
                 remaining.StartsWith("Note ") ||
                 remaining.Contains("unproven") ||
                 remaining.Contains("claim") ||
                 remaining.Contains("theorem") ||
                 remaining.Contains("proof") ||
                 remaining.Contains("derived")))
            {
                // Likely commentary or explanation text after JSON, return only the first complete object
                return firstObject;
            }
            
            // If remaining content is non-empty and doesn't look like valid JSON continuation,
            // it's likely trailing text - return only the first complete object
            if (remaining.Length > 0 && !remaining.StartsWith('{') && !remaining.StartsWith('['))
            {
                // Check if it contains any JSON-like structure (quotes, colons, etc.)
                var hasJsonStructure = remaining.Contains('"') || remaining.Contains(':');
                if (!hasJsonStructure || remaining.Length < 10)
                {
                    // Likely trailing text, return only the first complete object
                    return firstObject;
                }
            }
        }
        
        return trimmed;
    }
    
    /// <summary>
    /// Remove trailing non-JSON characters after a valid JSON object.
    /// Handles cases like: {"key": "value"}0} or {"key": "value"}abc
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
            // Check if there's any content after the valid closing brace
            if (lastValidClose < trimmed.Length - 1)
            {
                // Return only up to the valid closing brace, removing all trailing content
                return trimmed[..(lastValidClose + 1)];
            }
            // If the closing brace is at the end, return as-is
            return trimmed;
        }
        
        return trimmed;
    }
    
    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonCodeBlockPattern();
    
    [GeneratedRegex(@"<json>\s*([\s\S]*?)\s*</json>", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex JsonTagPattern();
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

