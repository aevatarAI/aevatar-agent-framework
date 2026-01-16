using System.Text;
using Microsoft.Extensions.Options;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Contracts.Collab;

namespace VibeResearching.Api.Materials;

// ============================================================
//  MaterialsService (DAG-based)
//
//  Goal:
//  - Load DAG knowledge nodes as "facts" materials.
//  - Build a bounded context string to inject into LLM.
// ============================================================
public sealed class MaterialsService
{
    private readonly DagStore _dag;
    private readonly IOptions<MaterialsOptions> _options;
    private readonly ILogger<MaterialsService> _logger;

    public MaterialsService(DagStore dag, IOptions<MaterialsOptions> options, ILogger<MaterialsService> logger)
    {
        _dag = dag ?? throw new ArgumentNullException(nameof(dag));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MaterialsSnapshot> LoadAsync(string sessionId, string dagId, string query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        sessionId = (sessionId ?? string.Empty).Trim();
        dagId = (dagId ?? string.Empty).Trim();
        query = (query ?? string.Empty).Trim();

        if (dagId.Length == 0)
            dagId = sessionId;

        SraDagSnapshot snap;
        try
        {
            snap = await _dag.LoadSnapshotAsync(dagId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[SRA] Failed to load DAG snapshot for materials (best-effort)." );
            snap = new SraDagSnapshot { SessionId = dagId };
        }

        var facts = BuildFactsFromDagSnapshot(snap, _options.Value);
        var context = BuildContextString(facts, query, _options.Value);

        return new MaterialsSnapshot
        {
            SessionId = sessionId,
            DagId = dagId,
            LoadedAt = DateTimeOffset.UtcNow,
            Facts = facts,
            RenderedContext = context
        };
    }

    // ============================================================
    //  Internal
    // ============================================================

    private static List<MaterialFile> BuildFactsFromDagSnapshot(SraDagSnapshot snap, MaterialsOptions options)
    {
        snap ??= new SraDagSnapshot();
        options ??= new MaterialsOptions();

        var maxNodes = Math.Clamp(options.MaxFiles, 0, 5000);
        var maxChars = Math.Clamp(options.MaxFileChars, 200, 500_000);

        var nodes = snap.Nodes
            .Where(n => n != null && n.Kind == SraDagNodeKind.Knowledge)
            .Where(n => !string.IsNullOrWhiteSpace(n.Id))
            .OrderByDescending(n => n.UpdatedAt?.ToDateTime().ToUniversalTime() ?? DateTime.MinValue)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .Take(maxNodes)
            .ToList();

        var list = new List<MaterialFile>(capacity: nodes.Count);
        foreach (var n in nodes)
        {
            var id = (n.Id ?? string.Empty).Trim();
            if (id.Length == 0) continue;

            var title = (n.Label ?? string.Empty).Trim();
            if (title.Length == 0) title = id;

            var content = BuildDagNodeContent(n, title, maxChars);

            list.Add(new MaterialFile
            {
                Kind = "fact",
                Id = $"dag:{id}",
                Title = title,
                RelativePath = $"dag/{id}",
                FullPath = $"dag:{id}",
                Content = content
            });
        }

        return list;
    }

    private static string BuildDagNodeContent(SraDagNode node, string fallbackTitle, int maxChars)
    {
        var proof = (node.Proof ?? string.Empty).Replace("\r", "").Trim();
        var content = proof.Length == 0 ? fallbackTitle : proof;

        if (node.Tags is { Count: > 0 })
        {
            var sb = new StringBuilder();
            sb.AppendLine(content);
            sb.AppendLine();
            sb.AppendLine("Tags:");
            foreach (var kv in node.Tags.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                sb.Append("- ").Append(kv.Key.Trim());
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    sb.Append(": ").Append(kv.Value.Trim());
                sb.AppendLine();
            }
            content = sb.ToString().Trim();
        }

        if (content.Length > maxChars)
            content = content[..maxChars];

        return content;
    }

    private static string BuildContextString(
        IReadOnlyList<MaterialFile> facts,
        string query,
        MaterialsOptions options)
    {
        var maxTotal = Math.Clamp(options.MaxContextChars, 2000, 200_000);
        var maxPerDoc = Math.Clamp(options.MaxPerDocChars, 200, 200_000);

        var sb = new StringBuilder(capacity: Math.Min(maxTotal, 8192));

        void AppendSection(string header)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.AppendLine(header);
        }

        AppendSection("DAG FACT INDEX (cite by id):");
        if (facts.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            var budgetLines = 260;
            foreach (var f in facts.Take(200))
            {
                if (sb.Length >= maxTotal) break;
                if (budgetLines-- <= 0) break;
                sb.Append("- [").Append(f.Id).Append("] ").Append(f.Title).AppendLine();
            }

            if (facts.Count > 200)
                sb.AppendLine("- ... (truncated)");
        }

        AppendSection("DAG FACTS (knowledge nodes):");
        if (facts.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            var ranked = RankMaterials(facts, query)
                .Take(32)
                .ToList();

            foreach (var r in ranked)
            {
                if (sb.Length >= maxTotal) break;
                AppendMaterial(sb, r.File, maxPerDoc, maxTotal, score: r.Score);
            }
        }

        var outText = sb.ToString();
        if (outText.Length <= maxTotal)
            return outText;
        return outText[..maxTotal];
    }

    private static void AppendMaterial(
        StringBuilder sb,
        MaterialFile f,
        int maxPerDoc,
        int maxTotal,
        int? score = null)
    {
        if (sb.Length >= maxTotal) return;

        sb.Append('[').Append(f.Id).Append(']');
        if (score.HasValue)
            sb.Append(" (score=").Append(score.Value).Append(')');
        if (!string.IsNullOrWhiteSpace(f.Title))
            sb.Append(' ').Append(f.Title.Trim());
        sb.AppendLine();

        var content = (f.Content ?? string.Empty).Replace("\r", "").Trim();
        if (content.Length > maxPerDoc)
            content = content[..maxPerDoc];

        if (content.Length > 0)
            sb.AppendLine(content);
        sb.AppendLine();
    }

    private static IEnumerable<(MaterialFile File, int Score)> RankMaterials(IReadOnlyList<MaterialFile> files, string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            foreach (var f in files)
                yield return (f, 0);
            yield break;
        }

        var separators = new[]
        {
            ' ', '\t', '\n', '\r',
            ',', '.', ';', ':', '!', '?',
            '(', ')', '[', ']', '{', '}',
            '"', '\''
        };

        var terms = q
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();

        int Score(MaterialFile f)
        {
            var text = (f.Content ?? string.Empty);
            if (text.Length == 0) return 0;

            var score = 0;
            foreach (var t in terms)
            {
                score += CountOccurrences(text, t);
            }

            foreach (var t in terms)
            {
                if (!string.IsNullOrWhiteSpace(f.Title) &&
                    f.Title.Contains(t, StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                }
            }

            return score;
        }

        var ranked = files
            .Select(f => (File: f, Score: Score(f)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.File.Id, StringComparer.Ordinal);

        foreach (var x in ranked)
            yield return x;
    }

    private static int CountOccurrences(string text, string term)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return 0;

        var count = 0;
        var idx = 0;
        while (idx < text.Length)
        {
            var hit = text.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase);
            if (hit < 0) break;
            count++;
            idx = hit + term.Length;
        }

        return count;
    }
}

public sealed record MaterialsSnapshot
{
    public required string SessionId { get; init; }
    public required string DagId { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }

    public required List<MaterialFile> Facts { get; init; }

    /// <summary>
    /// Bounded string for LLM injection.
    /// </summary>
    public required string RenderedContext { get; init; }
}

public sealed record MaterialFile
{
    public required string Kind { get; init; }            // fact
    public required string Id { get; init; }              // stable id: dag:{nodeId}
    public required string Title { get; init; }           // label or fallback id
    public required string RelativePath { get; init; }    // dag/{nodeId}
    public required string FullPath { get; init; }        // dag:{nodeId}
    public required string Content { get; init; }         // bounded content
}

