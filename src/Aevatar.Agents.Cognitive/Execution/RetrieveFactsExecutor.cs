using System.Text.RegularExpressions;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  RetrieveFactsExecutor (token-free default)
//
//  WHY:
//  - Extract "relevant fact selection" from LLM to reduce prompt bloat
//
//  DSL:
//    - id: retrieve_facts
//      type: retrieve_facts
//      query: "{{ candidate.statement }}"
//      source: "state.theorems"
//      text_field: "statement"
//      id_field: "id"
//      top_k: 20
//      mode: lexical   # default
//      store: relevant_facts
//
//  NOTE:
//  - lexical mode doesn't call any model, 0 tokens.
//  - embedding mode (future) can use embedding generator for semantic retrieval, but will generate additional calls.
// ============================================================

public sealed class RetrieveFactsExecutor
{
    // Unicode-aware: \p{L} letters (incl. CJK), \p{N} numbers
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);

    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;

    public RetrieveFactsExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    public PrimitiveResult Execute(StepDefinition step, Dictionary<string, object> variables)
    {
        var queryTemplate = step.Parameters.GetValueOrDefault("query")?.ToString() ?? "";
        var sourcePath = step.Parameters.GetValueOrDefault("source")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(queryTemplate) || string.IsNullOrWhiteSpace(sourcePath))
            return PrimitiveResult.Fail("retrieve_facts requires 'query' and 'source'");

        var query = _templateEngine.Render(queryTemplate, variables);

        var textField = step.Parameters.GetValueOrDefault("text_field")?.ToString() ?? "statement";
        var idField = step.Parameters.GetValueOrDefault("id_field")?.ToString() ?? "id";

        var topK = ResolveInt(step.Parameters.GetValueOrDefault("top_k"), variables, 20);
        topK = Math.Clamp(topK, 1, 200);

        var mode = (step.Parameters.GetValueOrDefault("mode")?.ToString() ?? "lexical").Trim().ToLowerInvariant();
        if (mode != "lexical")
        {
            _logger.LogWarning("retrieve_facts mode '{Mode}' not supported yet; falling back to lexical", mode);
            mode = "lexical";
        }

        var corpusObj = ResolvePathValue(variables, sourcePath);
        var corpus = ToList(corpusObj);
        if (corpus.Count == 0)
            return PrimitiveResult.Ok(new List<object>());

        var ranked = mode == "lexical"
            ? RankLexical(query, corpus, idField, textField)
            : [];

        var selected = ranked
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.id, StringComparer.Ordinal)
            .Take(topK)
            .Select(x => (object)new Dictionary<string, object>
            {
                ["id"] = x.id,
                ["statement"] = x.text,
                ["score"] = x.score
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(step.Store))
            variables[step.Store!] = selected;

        return PrimitiveResult.Ok(selected);
    }

    // ─────────────────────────────────────────────────────────
    //  Lexical ranking
    // ─────────────────────────────────────────────────────────

    private static List<(string id, string text, double score)> RankLexical(
        string query,
        List<object> corpus,
        string idField,
        string textField)
    {
        var qTokens = Tokenize(query);
        if (qTokens.Count == 0) return [];

        var results = new List<(string id, string text, double score)>(corpus.Count);
        foreach (var it in corpus)
        {
            var id = GetFieldString(it, idField) ?? "";
            var text = GetFieldString(it, textField) ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;

            var tTokens = Tokenize(text);
            if (tTokens.Count == 0) continue;

            // Jaccard similarity on token sets (cheap + stable)
            var inter = 0;
            foreach (var tok in qTokens)
                if (tTokens.Contains(tok)) inter++;

            var union = qTokens.Count + tTokens.Count - inter;
            var score = union > 0 ? (double)inter / union : 0.0;
            if (score <= 0) continue;

            results.Add((id, text, score));
        }

        return results;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in TokenRegex.Matches(text))
        {
            var s = m.Value.Trim().ToLowerInvariant();
            if (s.Length <= 1) continue;
            set.Add(s);
        }

        // Fallback for languages without whitespace (e.g., Chinese) or very short token sets:
        // Use character bigrams to get a cheap similarity signal.
        if (set.Count < 2)
        {
            var filtered = new string(text
                .ToLowerInvariant()
                .Where(ch => char.IsLetterOrDigit(ch))
                .Take(1024)
                .ToArray());

            if (filtered.Length == 1)
            {
                set.Add(filtered);
                return set;
            }

            // Cap to avoid pathological long strings.
            for (var i = 0; i < filtered.Length - 1 && set.Count < 512; i++)
            {
                set.Add(filtered.Substring(i, 2));
            }
        }

        return set;
    }

    // ─────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────

    private int ResolveInt(object? value, Dictionary<string, object> vars, int defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var p) => p,
            string s => ToInt(_templateEngine.Evaluate(s, vars), defaultValue),
            _ => defaultValue
        };
    }

    private static int ToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        string s when int.TryParse(s, out var p) => p,
        _ => defaultValue
    };

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

    private static List<object> ToList(object? items) => items switch
    {
        null => [],
        IEnumerable<object> enumerable => enumerable.ToList(),
        System.Collections.IList list => list.Cast<object>().ToList(),
        System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToList(),
        _ => [items]
    };

    private static string? GetFieldString(object item, string field)
    {
        if (string.IsNullOrWhiteSpace(field) || item == null) return null;

        if (item is IDictionary<string, object> d && d.TryGetValue(field, out var v))
            return v?.ToString();
        if (item is System.Collections.IDictionary nd && nd.Contains(field))
            return nd[field]?.ToString();

        var prop = item.GetType().GetProperty(field);
        return prop?.GetValue(item)?.ToString();
    }
}


