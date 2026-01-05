using System.Text.RegularExpressions;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Cognitive.Primitives;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  TransformExecutor (token-free)
//
//  WHY:
//  - Avoid letting LLM do deterministic data processing like map/filter/reduce (pure token waste)
//
//  DSL:
//    - id: aggregate
//      type: transform
//      ops:
//        - op: count_where
//          from: worker_verdicts
//          where: { field: "accept", equals: true }
//          store: accept_count
//        - op: select_many
//          from: worker_verdicts
//          field: "proposed_b"
//          store: b_candidates_raw
//        - op: distinct
//          from: b_candidates_raw
//          key: "statement"
//          store: b_candidates
//
//  Design principles:
//  - Only do deterministic, testable transformations
//  - Keep parameters minimal: op + from + store + few fields
// ============================================================

public sealed class TransformExecutor
{
    private static readonly Regex WsRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PurePathTemplateRegex = new(@"^\s*\{\{\s*([a-zA-Z_][\w\.]*)\s*\}\}\s*$", RegexOptions.Compiled);

    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;

    public TransformExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    public PrimitiveResult Execute(StepDefinition step, Dictionary<string, object> variables)
    {
        if (!step.Parameters.TryGetValue("ops", out var opsObj) || opsObj is not List<Dictionary<string, object?>> ops)
            return PrimitiveResult.Fail("transform requires 'ops' (list)");

        object? last = null;

        foreach (var op in ops)
        {
            var opName = (op.GetValueOrDefault("op")?.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(opName))
                return PrimitiveResult.Fail("transform op missing 'op' name");

            last = ExecuteOne(opName, op, variables);

            // store result if requested
            var store = op.GetValueOrDefault("store")?.ToString();
            if (!string.IsNullOrWhiteSpace(store))
            {
                variables[store!] = last!;
            }
        }

        // If step has store, also store last result.
        if (!string.IsNullOrWhiteSpace(step.Store) && last != null)
            variables[step.Store!] = last;

        return PrimitiveResult.Ok(last);
    }

    private object? ExecuteOne(string opName, Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        switch (opName.ToLowerInvariant())
        {
            // ----------------------------------------------------
            //  General helpers
            // ----------------------------------------------------
            case "eval":
                return Eval(op, vars);

            case "set":
                return ResolveValue(op.GetValueOrDefault("value"), vars);

            // ----------------------------------------------------
            //  State / object patching (token-free)
            // ----------------------------------------------------
            case "set_path":
                return SetPath(op, vars);

            case "set_path_if":
                return SetPathIf(op, vars);

            case "inc_path":
                return IncPath(op, vars);

            case "append_path":
                return AppendPath(op, vars, unique: false);

            case "append_unique_path":
                return AppendPath(op, vars, unique: true);

            // ----------------------------------------------------
            //  List helpers
            // ----------------------------------------------------
            case "take":
                return Take(op, vars, fromKey: "from");

            case "take_last":
                return TakeLast(op, vars);

            case "project_fields":
                return ProjectFields(op, vars);

            case "make_workers":
                return MakeWorkers(op, vars);

            case "count_where":
                return CountWhere(op, vars);

            case "any_where":
                return CountWhere(op, vars) is int n && n > 0;

            case "all_where":
                return AllWhere(op, vars);

            case "filter_where":
                return FilterWhere(op, vars);

            case "filter_not_in":
                return FilterNotIn(op, vars);

            case "select":
                return Select(op, vars);

            case "select_many":
                return SelectMany(op, vars);

            case "distinct":
                return Distinct(op, vars);

            case "normalize":
                return NormalizeText(ResolveValue(op.GetValueOrDefault("value"), vars)?.ToString());

            case "append":
                return Append(op, vars);

            case "lookup_by_id":
                return LookupById(op, vars);

            default:
                _logger.LogWarning("Unknown transform op: {Op}", opName);
                return null;
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Ops
    // ─────────────────────────────────────────────────────────

    private int CountWhere(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var where = op.GetValueOrDefault("where") as Dictionary<string, object?>;
        if (where == null) return items.Count;

        var field = where.GetValueOrDefault("field")?.ToString() ?? "";
        var expected = where.GetValueOrDefault("equals");

        var count = 0;
        foreach (var it in items)
        {
            if (TryGetField(it, field, out var actual) && EqualsLoose(actual, expected))
                count++;
        }
        return count;
    }

    private bool AllWhere(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var where = op.GetValueOrDefault("where") as Dictionary<string, object?>;
        if (where == null) return items.Count > 0;

        var field = where.GetValueOrDefault("field")?.ToString() ?? "";
        var expected = where.GetValueOrDefault("equals");

        if (items.Count == 0) return false;
        foreach (var it in items)
        {
            if (!TryGetField(it, field, out var actual) || !EqualsLoose(actual, expected))
                return false;
        }
        return true;
    }

    private List<object> FilterWhere(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var where = op.GetValueOrDefault("where") as Dictionary<string, object?>;
        if (where == null) return items;

        var field = where.GetValueOrDefault("field")?.ToString() ?? "";
        var expected = where.GetValueOrDefault("equals");

        var outList = new List<object>();
        foreach (var it in items)
        {
            if (it == null) continue;
            if (TryGetField(it, field, out var actual) && EqualsLoose(actual, expected))
                outList.Add(it);
        }
        return outList;
    }

    private List<object> FilterNotIn(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var denyItems = GetList(op, vars, "not_in");

        var keyField = op.GetValueOrDefault("key")?.ToString();
        var denyKeyField = op.GetValueOrDefault("not_in_key")?.ToString();

        var deny = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in denyItems)
        {
            if (it == null) continue;
            if (!string.IsNullOrWhiteSpace(denyKeyField) && TryGetField(it, denyKeyField!, out var v))
                deny.Add(NormalizeText(v?.ToString()));
            else
                deny.Add(NormalizeText(it.ToString()));
        }

        var outList = new List<object>();
        foreach (var it in items)
        {
            if (it == null) continue;

            string key;
            if (!string.IsNullOrWhiteSpace(keyField) && TryGetField(it, keyField!, out var v))
                key = NormalizeText(v?.ToString());
            else
                key = NormalizeText(it.ToString());

            if (!deny.Contains(key))
                outList.Add(it);
        }

        return outList;
    }

    private List<object> Select(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var field = op.GetValueOrDefault("field")?.ToString() ?? "";
        var outList = new List<object>(items.Count);
        foreach (var it in items)
        {
            if (TryGetField(it, field, out var v) && v != null)
                outList.Add(v);
        }
        return outList;
    }

    private List<object> SelectMany(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var field = op.GetValueOrDefault("field")?.ToString() ?? "";
        var outList = new List<object>();
        foreach (var it in items)
        {
            if (!TryGetField(it, field, out var v) || v == null) continue;
            foreach (var x in ToList(v))
                outList.Add(x);
        }
        return outList;
    }

    private List<object> Distinct(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var keyField = op.GetValueOrDefault("key")?.ToString();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outList = new List<object>();

        foreach (var it in items)
        {
            string key;
            if (!string.IsNullOrWhiteSpace(keyField) && TryGetField(it, keyField!, out var v))
                key = NormalizeText(v?.ToString());
            else
                key = NormalizeText(it?.ToString());

            if (it != null && seen.Add(key))
                outList.Add(it);
        }

        return outList;
    }

    private List<object> Append(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var list = GetList(op, vars, "to");
        var value = ResolveValue(op.GetValueOrDefault("value"), vars);
        if (value != null) list.Add(value);
        return list;
    }

    private List<object> LookupById(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var idsRaw = ResolveValue(op.GetValueOrDefault("ids"), vars);
        var idField = op.GetValueOrDefault("id_field")?.ToString() ?? "id";

        var ids = new HashSet<string>(ToList(idsRaw).Select(x => NormalizeText(x?.ToString())), StringComparer.Ordinal);
        var outList = new List<object>();

        foreach (var it in items)
        {
            if (TryGetField(it, idField, out var idObj) && idObj != null)
            {
                var id = NormalizeText(idObj.ToString());
                if (ids.Contains(id)) outList.Add(it);
            }
        }

        return outList;
    }

    // ─────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────

    private object? ResolveValue(object? value, Dictionary<string, object> vars)
    {
        if (value == null) return null;

        // string: template / pure variable-path
        if (value is string s)
            return ResolveStringValue(s, vars);

        // list: render strings recursively (YAML literal objects)
        if (value is System.Collections.IList list)
        {
            var outList = new List<object?>(list.Count);
            foreach (var it in list)
                outList.Add(ResolveValue(it, vars));
            return outList;
        }

        // dict: render strings recursively (YAML literal objects)
        if (value is System.Collections.IDictionary nd)
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry e in nd)
            {
                var key = e.Key?.ToString() ?? "";
                dict[key] = ResolveValue(e.Value, vars);
            }
            return dict;
        }

        return value;
    }

    private object? ResolveStringValue(string template, Dictionary<string, object> vars)
    {
        // Pure variable reference with dotted path: "{{ state.iteration }}"
        var match = PurePathTemplateRegex.Match(template);
        if (match.Success)
        {
            var path = match.Groups[1].Value.Trim();
            var resolved = ResolvePathValue(vars, path);
            return resolved ?? template;
        }

        // Otherwise treat as Scriban template.
        return _templateEngine.Render(template, vars);
    }

    // ---------------------------------------------------------
    //  Op: eval
    // ---------------------------------------------------------

    private object? Eval(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var expr = op.GetValueOrDefault("expr")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(expr)) return null;
        return _templateEngine.Evaluate(expr, vars);
    }

    // ---------------------------------------------------------
    //  Ops: state patching (set/inc/append)
    // ---------------------------------------------------------

    private object? SetPath(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var targetName = op.GetValueOrDefault("target")?.ToString() ?? "";
        var path = op.GetValueOrDefault("path")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(targetName) || string.IsNullOrWhiteSpace(path))
            return null;

        if (!vars.TryGetValue(targetName, out var targetObj) || targetObj == null)
            return null;

        var root = AsDictionary(targetObj);
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;

        var value = ResolveValue(op.GetValueOrDefault("value"), vars);
        var container = GetOrCreateContainer(root, segments, segments.Length - 1);
        container[segments[^1]] = value;
        return value;
    }

    private object? SetPathIf(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var condObj = op.GetValueOrDefault("if");
        if (!ResolveBool(condObj, vars, defaultValue: false))
            return null;

        return SetPath(op, vars);
    }

    private object? IncPath(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var targetName = op.GetValueOrDefault("target")?.ToString() ?? "";
        var path = op.GetValueOrDefault("path")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(targetName) || string.IsNullOrWhiteSpace(path))
            return null;

        if (!vars.TryGetValue(targetName, out var targetObj) || targetObj == null)
            return null;

        var root = AsDictionary(targetObj);
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;

        var container = GetOrCreateContainer(root, segments, segments.Length - 1);
        var last = segments[^1];

        var oldVal = container.Contains(last) ? container[last] : 0L;
        var oldNum = ConvertToLong(oldVal, 0L);

        var byRaw = op.GetValueOrDefault("by") ?? 1;
        var byResolved = ResolveValue(byRaw, vars);
        var by = ConvertToLong(byResolved, 1L);

        var next = oldNum + by;
        container[last] = next;
        return next;
    }

    private object? AppendPath(Dictionary<string, object?> op, Dictionary<string, object> vars, bool unique)
    {
        var targetName = op.GetValueOrDefault("target")?.ToString() ?? "";
        var path = op.GetValueOrDefault("path")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(targetName) || string.IsNullOrWhiteSpace(path))
            return null;

        if (!vars.TryGetValue(targetName, out var targetObj) || targetObj == null)
            return null;

        var root = AsDictionary(targetObj);
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;

        var container = GetOrCreateContainer(root, segments, segments.Length - 1);
        var last = segments[^1];

        if (!container.Contains(last) || container[last] is not System.Collections.IList list)
        {
            list = new List<object>();
            container[last] = list;
        }

        var value = ResolveValue(op.GetValueOrDefault("value"), vars);
        if (value == null) return list;

        if (!unique)
        {
            list.Add(value);
            return list;
        }

        var needle = NormalizeText(value.ToString());
        foreach (var it in list)
        {
            if (it == null) continue;
            if (NormalizeText(it.ToString()) == needle)
                return list;
        }

        list.Add(value);
        return list;
    }

    // ---------------------------------------------------------
    //  Ops: list helpers
    // ---------------------------------------------------------

    private List<object> Take(Dictionary<string, object?> op, Dictionary<string, object> vars, string fromKey)
    {
        var items = GetList(op, vars, fromKey);
        var n = ResolveInt(op.GetValueOrDefault("n"), vars, 0);
        if (n <= 0) return [];
        if (n >= items.Count) return items;
        return items.Take(n).ToList();
    }

    private List<object> TakeLast(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var n = ResolveInt(op.GetValueOrDefault("n"), vars, 0);
        if (n <= 0) return [];
        if (n >= items.Count) return items;
        return items.Skip(Math.Max(0, items.Count - n)).ToList();
    }

    private List<object> ProjectFields(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var items = GetList(op, vars, "from");
        var fieldsRaw = ResolveValue(op.GetValueOrDefault("fields"), vars);
        var fields = ToList(fieldsRaw)
            .Select(x => x?.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

        if (fields.Count == 0) return items;

        var outList = new List<object>(items.Count);
        foreach (var it in items)
        {
            if (it == null) continue;

            var dict = new Dictionary<string, object>();
            foreach (var f in fields)
            {
                if (TryGetField(it, f, out var v) && v != null)
                    dict[f] = v;
            }

            outList.Add(dict);
        }

        return outList;
    }

    private List<object> MakeWorkers(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var n = ResolveInt(op.GetValueOrDefault("n"), vars, 0);
        if (n <= 0) return [];
        n = Math.Clamp(n, 1, 256);

        var idPrefix = op.GetValueOrDefault("id_prefix")?.ToString() ?? "worker-";

        // profiles: [{ role: "...", angle: "..." }, ...]
        var profilesObj = ResolveValue(op.GetValueOrDefault("profiles"), vars);
        var profiles = ParseProfiles(profilesObj);
        if (profiles.Count == 0)
        {
            profiles = DefaultWorkerProfiles();
        }

        var outList = new List<object>(n);
        for (var i = 0; i < n; i++)
        {
            var profile = profiles[i % profiles.Count];
            outList.Add(new Dictionary<string, object>
            {
                ["id"] = $"{idPrefix}{i}",
                ["role"] = profile.role,
                ["angle"] = profile.angle
            });
        }

        return outList;
    }

    private static List<(string role, string angle)> ParseProfiles(object? profilesObj)
    {
        var list = ToList(profilesObj);
        var outList = new List<(string role, string angle)>();
        foreach (var it in list)
        {
            if (it == null) continue;
            if (it is IDictionary<string, object> d)
            {
                var role = d.TryGetValue("role", out var r) ? r?.ToString() ?? "" : "";
                var angle = d.TryGetValue("angle", out var a) ? a?.ToString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(angle))
                    outList.Add((role, angle));
            }
            else if (it is System.Collections.IDictionary nd)
            {
                var role = nd.Contains("role") ? nd["role"]?.ToString() ?? "" : "";
                var angle = nd.Contains("angle") ? nd["angle"]?.ToString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(role) || !string.IsNullOrWhiteSpace(angle))
                    outList.Add((role, angle));
            }
        }
        return outList;
    }

    private static List<(string role, string angle)> DefaultWorkerProfiles() =>
    [
        ("Direct prover", "Try to construct the shortest proof path."),
        ("Counterexample hunter", "Try to refute quickly with a fatal counterexample or contradiction."),
        ("Assumption checker", "Try to find the minimal missing assumption or implicit premise."),
        ("Dependency minimalist", "Try to reduce dependency set; prefer proofs close to axioms."),
        ("Case-split specialist", "Try a structured case analysis; look for missing branches."),
        ("Algebraic manipulator", "Try algebraic/rewriting transformations; simplify aggressively."),
        ("Induction specialist", "Try induction or iterative argument patterns if applicable."),
        ("Model builder", "Try to build a small concrete model to test plausibility."),
        ("Proof auditor", "Try to audit for hidden leaps; insist on explicit justification.")
    ];

    // ---------------------------------------------------------
    //  Path helpers (Dictionary<string, object> & IDictionary)
    // ---------------------------------------------------------

    private static System.Collections.IDictionary AsDictionary(object obj) =>
        obj as System.Collections.IDictionary
        ?? throw new InvalidOperationException("target must be a dictionary-like object");

    private static System.Collections.IDictionary GetOrCreateContainer(
        System.Collections.IDictionary root,
        string[] segments,
        int depth)
    {
        System.Collections.IDictionary current = root;

        for (var i = 0; i < depth; i++)
        {
            var key = segments[i];
            if (current.Contains(key) && current[key] is System.Collections.IDictionary existing)
            {
                current = existing;
                continue;
            }

            var created = new Dictionary<string, object>();
            current[key] = created;
            current = (System.Collections.IDictionary)created;
        }

        return current;
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

    private bool ResolveBool(object? value, Dictionary<string, object> vars, bool defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => Math.Abs(d) > double.Epsilon,
            float f => Math.Abs(f) > float.Epsilon,
            string s when bool.TryParse(s, out var parsed) => parsed,
            string s => ConvertToBool(_templateEngine.Evaluate(s, vars), defaultValue),
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

    private static bool ConvertToBool(object? value, bool defaultValue) => value switch
    {
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => Math.Abs(d) > double.Epsilon,
        float f => Math.Abs(f) > float.Epsilon,
        decimal m => m != 0,
        string s when bool.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    private static long ConvertToLong(object? value, long defaultValue) => value switch
    {
        long l => l,
        int i => i,
        double d => (long)d,
        float f => (long)f,
        decimal m => (long)m,
        string s when long.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };

    private static List<object> GetList(Dictionary<string, object?> op, Dictionary<string, object> vars, string key)
    {
        var from = op.GetValueOrDefault(key);
        if (from == null) return [];

        if (from is string s)
        {
            // support dotted paths: "a.b.c"
            var v = ResolvePathValue(vars, s);
            return ToList(v);
        }

        return ToList(from);
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

    private static List<object> ToList(object? items) => items switch
    {
        null => [],
        IEnumerable<object> enumerable => enumerable.ToList(),
        System.Collections.IList list => list.Cast<object>().ToList(),
        System.Collections.IEnumerable enumerable => enumerable.Cast<object>().ToList(),
        _ => [items]
    };

    private static bool TryGetField(object item, string field, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(field) || item == null) return false;

        switch (item)
        {
            case IDictionary<string, object> d when d.TryGetValue(field, out var v):
                value = v;
                return true;
            case System.Collections.IDictionary nd when nd.Contains(field):
                value = nd[field];
                return true;
        }

        // Try reflection fallback (camelCase in TemplateEngine for complex objects)
        var prop = item.GetType().GetProperty(field);
        if (prop != null)
        {
            value = prop.GetValue(item);
            return true;
        }

        // Try camelCase conversion
        var camel = char.ToUpperInvariant(field[0]) + field[1..];
        prop = item.GetType().GetProperty(camel);
        if (prop != null)
        {
            value = prop.GetValue(item);
            return true;
        }

        return false;
    }

    private static bool EqualsLoose(object? actual, object? expected)
    {
        if (expected == null) return actual == null;
        if (actual == null) return false;

        // bool
        if (expected is bool eb)
        {
            if (actual is bool ab) return ab == eb;
            if (bool.TryParse(actual.ToString(), out var p)) return p == eb;
        }

        // numeric
        if (expected is int ei)
        {
            if (actual is int ai) return ai == ei;
            if (int.TryParse(actual.ToString(), out var p)) return p == ei;
        }

        // string
        return string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var trimmed = s.Trim();
        trimmed = WsRegex.Replace(trimmed, " ");
        return trimmed.ToLowerInvariant();
    }
}


