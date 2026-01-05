using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using Aevatar.Agents.Cognitive.Hpa;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Cognitive.Execution;

// ============================================================
//  HpaExecutor (token-free, coordinator-only)
//
//  WHY:
//  - Extract HPA's "geometry/phase/scan" calculations from LLM (0 tokens)
//  - Provide verifiable numerical metrics for workflow: scan_target / embedding / gap / associator etc.
// ============================================================

public sealed class HpaExecutor
{
    // If a parameter is exactly "{{ path.to.value }}", resolve to the raw object (not string render).
    private static readonly Regex PurePathTemplateRegex = new(@"^\s*\{\{\s*([a-zA-Z_][\w\.]*)\s*\}\}\s*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    // Golden slope α = φ^{-1} = 0.618...
    private const double GoldenAlpha = 0.6180339887498948482;

    private readonly TemplateEngine _templateEngine;
    private readonly ILogger _logger;

    public HpaExecutor(TemplateEngine templateEngine, ILogger logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    public PrimitiveResult Execute(StepDefinition step, Dictionary<string, object> variables)
    {
        if (!step.Parameters.TryGetValue("ops", out var opsObj) || opsObj is not List<Dictionary<string, object?>> ops)
            return PrimitiveResult.Fail("hpa requires 'ops' (list)");

        object? last = null;
        var lastPreview = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var op in ops)
        {
            var opName = (op.GetValueOrDefault("op")?.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(opName))
                return PrimitiveResult.Fail("hpa op missing 'op' name");

            try
            {
                last = ExecuteOne(opName, op, variables);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "hpa op '{Op}' failed", opName);
                return PrimitiveResult.Fail($"hpa op '{opName}' failed: {ex.Message}");
            }

            // store result if requested
            var store = op.GetValueOrDefault("store")?.ToString();
            if (!string.IsNullOrWhiteSpace(store))
            {
                variables[store!] = last!;
                // For UI: keep only last few keys (avoid huge SSE payload)
                lastPreview["op"] = opName;
                lastPreview["store"] = store!;
                lastPreview["value"] = last;
            }
        }

        if (!string.IsNullOrWhiteSpace(step.Store) && last != null)
            variables[step.Store!] = last;

        // Provide a short assistantResponse for SSE/debugging (deterministic JSON)
        var assistant = lastPreview.Count == 0
            ? null
            : JsonSerializer.Serialize(lastPreview, JsonOptions);

        return new PrimitiveResult
        {
            Success = true,
            Value = last,
            AssistantResponse = assistant,
            TokensUsed = 0,
            LlmCalls = 0,
            Duration = TimeSpan.Zero
        };
    }

    private object? ExecuteOne(string opName, Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        switch (opName.Trim().ToLowerInvariant())
        {
            case "scan_target":
                return ScanTarget(op, vars);

            case "embed_node":
                return EmbedNode(op, vars);

            case "embed_list":
                return EmbedList(op, vars);

            case "evidence_synthesize":
                return EvidenceSynthesize(op, vars);

            case "associator_stats":
                return AssociatorStats(op, vars);

            case "gap_holonomy":
                return GapHolonomy(op, vars);

            default:
                throw new NotSupportedException($"unknown hpa op: {opName}");
        }
    }

    // ============================================================
    //  Op: scan_target
    //
    //  Paper mapping:
    //  - Θ: irrational rotation on S^1
    //  - golden branch α = φ^{-1}
    //  - scan time k = iteration index
    //
    //  DSL example:
    //    - op: scan_target
    //      iteration: "{{ state.iteration }}"
    //      alpha: "{{ hpa_alpha }}"
    //      seed_phase: "{{ hpa_seed_phase }}"
    //      store: scan
    // ============================================================
    private object ScanTarget(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var iteration = ResolveInt(GetParam(op, vars, "iteration") ?? GetParam(op, vars, "k"), defaultValue: 0);
        var alpha = ResolveDouble(GetParam(op, vars, "alpha"), defaultValue: GoldenAlpha);
        var seed = ResolveDouble(GetParam(op, vars, "seed_phase"), defaultValue: 0.0);

        // Normalize alpha/seed into (0,1) / [0,1)
        alpha = alpha - Math.Floor(alpha);
        if (alpha <= 0) alpha = GoldenAlpha;

        seed = seed - Math.Floor(seed);
        if (seed < 0) seed += 1.0;

        var phase01 = Frac(seed + iteration * alpha);
        var targetPhase = 2.0 * Math.PI * phase01;

        return new Dictionary<string, object>
        {
            ["scan_k"] = iteration,
            ["alpha"] = alpha,
            ["seed_phase"] = seed,
            ["target_phase01"] = phase01,
            ["target_phase"] = targetPhase
        };
    }

    private static double Frac(double x)
    {
        var f = x - Math.Floor(x);
        return f < 0 ? f + 1.0 : f;
    }

    // ============================================================
    //  Op: embed_node
    //
    //  DSL example:
    //    - op: embed_node
    //      node: "{{ candidate }}"
    //      attach_field: "hpa"
    // ============================================================
    private object EmbedNode(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var cfg = ResolveConfig(op, vars);

        // node is optional: can embed directly from depends_on/factor_sequence params
        var nodeObj = GetParam(op, vars, "node");

        var dependsOn = ResolveStringList(
            GetParam(op, vars, "depends_on") ?? GetField(nodeObj, "depends_on") ?? GetField(nodeObj, "dependsOn"));

        var factorSeq = ResolveStringList(
            GetParam(op, vars, "factor_sequence") ?? GetField(nodeObj, "factor_sequence") ?? GetField(nodeObj, "factorSequence"));

        var value = HpaEmbedding.EmbedToValue(dependsOn, factorSeq.Count > 0 ? factorSeq : null, cfg);

        var attachField = ResolveString(GetParam(op, vars, "attach_field"), defaultValue: "");
        if (string.IsNullOrWhiteSpace(attachField))
            attachField = "hpa";

        if (nodeObj != null && TrySetField(nodeObj, attachField, value))
        {
            // attached in-place
        }

        return value;
    }

    // ============================================================
    //  Op: embed_list
    //
    //  DSL example:
    //    - op: embed_list
    //      from: "state.theorems"
    //      id_field: "id"
    //      attach_field: "hpa"
    //      store: theorem_index
    // ============================================================
    private object EmbedList(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var cfg = ResolveConfig(op, vars);

        var from = ResolveString(GetParam(op, vars, "from") ?? GetParam(op, vars, "list"), defaultValue: "");
        if (string.IsNullOrWhiteSpace(from))
            throw new ArgumentException("embed_list requires 'from' (path) or 'list'");

        var listObj = ResolvePath(vars, from);
        var list = ToList(listObj);

        var idField = ResolveString(GetParam(op, vars, "id_field"), defaultValue: "id");
        var attachField = ResolveString(GetParam(op, vars, "attach_field"), defaultValue: "hpa");

        var index = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var it in list)
        {
            if (it == null) continue;
            var id = GetFieldString(it, idField) ?? "";
            if (string.IsNullOrWhiteSpace(id)) continue;

            var dependsOn = ResolveStringList(GetField(it, "depends_on") ?? GetField(it, "dependsOn"));
            var factorSeq = ResolveStringList(GetField(it, "factor_sequence") ?? GetField(it, "factorSequence"));
            var embed = HpaEmbedding.EmbedToValue(dependsOn, factorSeq.Count > 0 ? factorSeq : null, cfg);

            // attach into the theorem object (in-place)
            if (!string.IsNullOrWhiteSpace(attachField))
                TrySetField(it, attachField, embed);

            index[id.Trim()] = embed;
        }

        return index;
    }

    // ============================================================
    //  Op: evidence_synthesize (complex-plane coherence + gap)
    //
    //  DSL example:
    //    - op: evidence_synthesize
    //      candidate: "{{ candidate }}"
    //      verdicts: "worker_verdicts"
    //      relevant: "relevant_facts"
    //      theorem_index: "theorem_index"
    //      store: evidence
    // ============================================================
    private object EvidenceSynthesize(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var cfg = ResolveConfig(op, vars);

        var candidateObj = GetParam(op, vars, "candidate") ?? vars.GetValueOrDefault("candidate");
        var candidateEmbedding = TryGetEmbedded(candidateObj) ?? EmbedFromObject(candidateObj, cfg);
        var zA = GetComplex(candidateEmbedding);

        var verdictsPath = ResolveString(GetParam(op, vars, "verdicts") ?? GetParam(op, vars, "from"), defaultValue: "");
        if (string.IsNullOrWhiteSpace(verdictsPath))
            verdictsPath = "worker_verdicts";

        var verdicts = ToList(ResolvePath(vars, verdictsPath));

        // Optional projection lattice: candidate + relevant theorems
        var theoremIndexPath = ResolveString(GetParam(op, vars, "theorem_index"), defaultValue: "");
        var theoremIndex = !string.IsNullOrWhiteSpace(theoremIndexPath)
            ? ResolvePath(vars, theoremIndexPath) as IDictionary
            : null;

        var relevantPath = ResolveString(GetParam(op, vars, "relevant"), defaultValue: "");
        var relevant = string.IsNullOrWhiteSpace(relevantPath) ? [] : ToList(ResolvePath(vars, relevantPath));
        var relevantIds = relevant
            .Select(x => GetFieldString(x, "id"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // --------------------------------------------------------
        //  Coherence: weighted alignment of worker vectors
        // --------------------------------------------------------
        var acceptCount = 0;
        var strongRefutationCount = 0;
        var total = 0;

        var sumRe = 0.0;
        var sumIm = 0.0;
        var sumAbsW = 0.0;

        foreach (var v in verdicts)
        {
            if (v == null) continue;

            // include_failures: { success:false, ... }
            if (TryGetField(v, "success", out var successObj) && successObj is bool b && b == false)
                continue;

            total++;
            var accept = GetBool(v, "accept");
            var strong = GetBool(v, "strong_refutation");
            if (accept) acceptCount++;
            if (strong) strongRefutationCount++;

            var confidence = GetDouble(v, "confidence", 0.5);
            confidence = Math.Clamp(confidence, 0.0, 1.0);

            var sign = strong ? -1.0 : accept ? +1.0 : -0.5;
            var w = sign * confidence;
            sumAbsW += Math.Abs(w);

            // worker embedding: prefer embedded field, else derive from depends_on
            var wEmbed = TryGetEmbedded(v) ?? EmbedFromObject(v, cfg);
            var (re, im, norm) = GetComplex(wEmbed);
            if (norm <= 0) continue;

            // use direction only (stability): vote on phase alignment, not magnitude
            re /= norm;
            im /= norm;

            sumRe += w * re;
            sumIm += w * im;
        }

        var sumNorm = Math.Sqrt(sumRe * sumRe + sumIm * sumIm);
        var coherence = sumAbsW > 0 ? sumNorm / sumAbsW : 0.0;

        // Build a synthesized vector at candidate radius (projection readout)
        var vRe = zA.re * sumRe - zA.im * sumIm; // rotate by candidate? (keep candidate as reference)
        var vIm = zA.im * sumRe + zA.re * sumIm;
        var vNorm = Math.Sqrt(vRe * vRe + vIm * vIm);

        // --------------------------------------------------------
        //  Nearest-point projection onto lattice: {candidate} U {relevant theorems}
        // --------------------------------------------------------
        var bestId = "candidate";
        var bestRe = zA.re;
        var bestIm = zA.im;
        var bestDist2 = Dist2(vRe, vIm, bestRe, bestIm);

        if (theoremIndex != null)
        {
            foreach (var id in relevantIds)
            {
                if (!theoremIndex.Contains(id)) continue;
                var embObj = theoremIndex[id];
                if (embObj == null) continue;
                var (re, im, _) = GetComplex(embObj);
                var d2 = Dist2(vRe, vIm, re, im);
                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestId = id;
                    bestRe = re;
                    bestIm = im;
                }
            }
        }

        var gap = Math.Sqrt(bestDist2);
        var gapNorm = vNorm > 0 ? gap / vNorm : 0.0;

        return new Dictionary<string, object>
        {
            ["total_verdicts"] = total,
            ["accept_count"] = acceptCount,
            ["strong_refutation_count"] = strongRefutationCount,
            ["coherence"] = coherence,

            ["v_re"] = vRe,
            ["v_im"] = vIm,
            ["v_norm"] = vNorm,

            ["projection_id"] = bestId,
            ["projection_re"] = bestRe,
            ["projection_im"] = bestIm,

            ["gap"] = gap,
            ["gap_norm"] = gapNorm
        };
    }

    // ============================================================
    //  Op: associator_stats (octonion non-associativity signal)
    //
    //  DSL example:
    //    - op: associator_stats
    //      candidate: "{{ candidate }}"
    //      verdicts: "worker_verdicts"
    //      store: assoc
    // ============================================================
    private object AssociatorStats(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var cfg = ResolveConfig(op, vars);

        var sequences = new List<List<string>>();

        var candidateObj = GetParam(op, vars, "candidate") ?? vars.GetValueOrDefault("candidate");
        var candSeq = GetFactorSequence(candidateObj);
        if (candSeq.Count > 0) sequences.Add(candSeq);

        var verdictsPath = ResolveString(GetParam(op, vars, "verdicts") ?? GetParam(op, vars, "from"), defaultValue: "");
        var verdicts = string.IsNullOrWhiteSpace(verdictsPath) ? [] : ToList(ResolvePath(vars, verdictsPath));
        foreach (var v in verdicts)
        {
            if (v == null) continue;
            if (TryGetField(v, "success", out var successObj) && successObj is bool b && b == false)
                continue;
            var seq = GetFactorSequence(v);
            if (seq.Count > 0) sequences.Add(seq);
        }

        var triples = 0;
        var sum = 0.0;
        var max = 0.0;

        foreach (var seq in sequences)
        {
            if (seq.Count < 3) continue;
            for (var i = 0; i + 2 < seq.Count; i++)
            {
                var x = HpaEmbedding.UnitOctonionForId(seq[i], cfg);
                var y = HpaEmbedding.UnitOctonionForId(seq[i + 1], cfg);
                var z = HpaEmbedding.UnitOctonionForId(seq[i + 2], cfg);
                var a = Octonion.Associator(x, y, z);
                var n = a.Norm();
                triples++;
                sum += n;
                if (n > max) max = n;
            }
        }

        var mean = triples > 0 ? sum / triples : 0.0;
        return new Dictionary<string, object>
        {
            ["triple_count"] = triples,
            ["associator_mean"] = mean,
            ["associator_max"] = max
        };
    }

    // ============================================================
    //  Op: gap_holonomy (H^1 proxy)
    //
    //  Minimal implementation:
    //  - sum provided gap vectors (complex) -> holonomy
    //
    //  DSL example:
    //    - op: gap_holonomy
    //      gaps: "edge_gaps"
    // ============================================================
    private object GapHolonomy(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        var gapsPath = ResolveString(GetParam(op, vars, "gaps"), defaultValue: "");
        var gaps = string.IsNullOrWhiteSpace(gapsPath) ? [] : ToList(ResolvePath(vars, gapsPath));

        var re = 0.0;
        var im = 0.0;
        foreach (var g in gaps)
        {
            if (g == null) continue;
            var (gr, gi, _) = GetComplex(g);
            re += gr;
            im += gi;
        }

        var norm = Math.Sqrt(re * re + im * im);
        return new Dictionary<string, object>
        {
            ["holonomy_re"] = re,
            ["holonomy_im"] = im,
            ["holonomy_norm"] = norm,
            ["edge_count"] = gaps.Count
        };
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private HpaEmbeddingConfig ResolveConfig(Dictionary<string, object?> op, Dictionary<string, object> vars)
    {
        // Context vars are expected from service via initialVariables (hpa_*).
        var betaModelRaw = ResolveString(
            GetParam(op, vars, "beta_model") ?? vars.GetValueOrDefault("hpa_beta_model"),
            defaultValue: "random_prime_phase");

        var beta0 = ResolveDouble(GetParam(op, vars, "beta0") ?? vars.GetValueOrDefault("hpa_beta0"), 4.0);
        var beta1 = ResolveDouble(GetParam(op, vars, "beta1") ?? vars.GetValueOrDefault("hpa_beta1"), 2.0);

        var wBase = ResolveDouble(GetParam(op, vars, "radial_w_base") ?? vars.GetValueOrDefault("hpa_radial_w_base"), 0.12);
        var wScale = ResolveDouble(GetParam(op, vars, "radial_w_scale") ?? vars.GetValueOrDefault("hpa_radial_w_scale"), 0.38);

        var seed = ResolveInt(GetParam(op, vars, "seed") ?? vars.GetValueOrDefault("hpa_seed"), 0);

        return new HpaEmbeddingConfig
        {
            BetaModel = HpaEmbedding.ParseBetaModel(betaModelRaw),
            Beta0 = beta0,
            Beta1 = beta1,
            RadialWBase = wBase,
            RadialWScale = wScale,
            Seed = seed
        };
    }

    private object? GetParam(Dictionary<string, object?> op, Dictionary<string, object> vars, string key)
    {
        if (!op.TryGetValue(key, out var raw) || raw == null) return null;
        return ResolveValue(raw, vars);
    }

    private object? ResolveValue(object raw, Dictionary<string, object> vars)
    {
        // 0-token boundary: we allow template evaluation (deterministic), but we never call LLM here.
        if (raw is not string s) return raw;

        if (!s.Contains("{{", StringComparison.Ordinal))
            return s;

        // Pure path reference: return object directly (critical for hpa.embed_node node="{{ candidate }}").
        var m = PurePathTemplateRegex.Match(s);
        if (m.Success)
        {
            var path = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (vars.TryGetValue(path, out var direct)) return direct;
            return ResolvePath(vars, path);
        }

        try
        {
            // Evaluate() returns bool/double when possible; otherwise string.
            return _templateEngine.Evaluate(s, vars);
        }
        catch
        {
            // As a safe fallback, try plain render (string)
            try { return _templateEngine.Render(s, vars); }
            catch { return s; }
        }
    }

    private int ResolveInt(object? value, int defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
            _ => defaultValue
        };
    }

    private double ResolveDouble(object? value, double defaultValue)
    {
        if (value == null) return defaultValue;
        return value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
            _ => defaultValue
        };
    }

    private string ResolveString(object? value, string defaultValue)
        => value?.ToString() is { Length: > 0 } s ? s.Trim() : defaultValue;

    private static object? ResolvePath(Dictionary<string, object> vars, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        if (!vars.TryGetValue(parts[0], out var cur) || cur == null) return null;
        for (var i = 1; i < parts.Length; i++)
        {
            var key = parts[i];
            cur = cur switch
            {
                IDictionary<string, object> d => d.TryGetValue(key, out var v) ? v : null,
                IDictionary nd => nd.Contains(key) ? nd[key] : null,
                _ => null
            };
            if (cur == null) return null;
        }
        return cur;
    }

    private static List<object> ToList(object? items) => items switch
    {
        null => [],
        IEnumerable<object> enumerable => enumerable.ToList(),
        IList list => list.Cast<object>().ToList(),
        IEnumerable enumerable => enumerable.Cast<object>().ToList(),
        _ => [items]
    };

    private static bool TryGetField(object item, string field, out object? value)
    {
        value = null;
        if (item == null || string.IsNullOrWhiteSpace(field)) return false;

        if (item is IDictionary<string, object> d1 && d1.TryGetValue(field, out value))
            return true;
        if (item is IDictionary<string, object?> d2 && d2.TryGetValue(field, out var v2))
        {
            value = v2;
            return true;
        }
        if (item is IDictionary nd && nd.Contains(field))
        {
            value = nd[field];
            return true;
        }

        return false;
    }

    private static object? GetField(object? item, string field)
        => item != null && TryGetField(item, field, out var v) ? v : null;

    private static string? GetFieldString(object? item, string field)
        => GetField(item, field)?.ToString();

    private static bool TrySetField(object item, string field, object value)
    {
        if (item is IDictionary<string, object> d1)
        {
            d1[field] = value;
            return true;
        }
        if (item is IDictionary<string, object?> d2)
        {
            d2[field] = value;
            return true;
        }
        if (item is IDictionary nd)
        {
            nd[field] = value;
            return true;
        }
        return false;
    }

    private static bool GetBool(object item, string field)
    {
        if (!TryGetField(item, field, out var v) || v == null) return false;
        return v switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var p) => p,
            _ => false
        };
    }

    private static double GetDouble(object item, string field, double defaultValue)
    {
        if (!TryGetField(item, field, out var v) || v == null) return defaultValue;
        return v switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
            _ => defaultValue
        };
    }

    private static List<string> ResolveStringList(object? value)
    {
        var list = new List<string>();
        if (value == null) return list;

        if (value is string s)
        {
            var one = s.Trim();
            if (one.Length > 0) list.Add(one);
            return list;
        }

        foreach (var it in ToList(value))
        {
            var t = it?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(t)) list.Add(t!);
        }
        return list;
    }

    private static Dictionary<string, object>? TryGetEmbedded(object? obj)
    {
        if (obj == null) return null;
        if (!TryGetField(obj, "hpa", out var v) || v == null) return null;
        return v as Dictionary<string, object>;
    }

    private Dictionary<string, object> EmbedFromObject(object? obj, HpaEmbeddingConfig cfg)
    {
        var dependsOn = ResolveStringList(GetField(obj, "depends_on") ?? GetField(obj, "dependsOn"));
        var factorSeq = ResolveStringList(GetField(obj, "factor_sequence") ?? GetField(obj, "factorSequence"));
        return HpaEmbedding.EmbedToValue(dependsOn, factorSeq.Count > 0 ? factorSeq : null, cfg);
    }

    private static (double re, double im, double norm) GetComplex(object? embedOrObj)
    {
        // Accept both embedding dict and raw object that contains z_re/z_im.
        if (embedOrObj != null && TryGetField(embedOrObj, "z_re", out var reObj) && TryGetField(embedOrObj, "z_im", out var imObj))
        {
            var re = ToDouble(reObj);
            var im = ToDouble(imObj);
            var norm = Math.Sqrt(re * re + im * im);
            return (re, im, norm);
        }

        // Fallback: treat as zero
        return (0.0, 0.0, 0.0);
    }

    private static double ToDouble(object? v) => v switch
    {
        null => 0.0,
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) => p,
        _ => 0.0
    };

    private static double Dist2(double xRe, double xIm, double yRe, double yIm)
    {
        var dr = xRe - yRe;
        var di = xIm - yIm;
        return dr * dr + di * di;
    }

    private static List<string> GetFactorSequence(object? obj)
    {
        // Prefer explicit factor_sequence, else depends_on
        var seq = ResolveStringList(GetField(obj, "factor_sequence") ?? GetField(obj, "factorSequence"));
        if (seq.Count > 0) return seq;
        return ResolveStringList(GetField(obj, "depends_on") ?? GetField(obj, "dependsOn"));
    }
}

