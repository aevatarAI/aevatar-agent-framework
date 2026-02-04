using System.Text;
using Microsoft.Extensions.Options;
using Aevatar.Agents.Cognitive.Researching.Dag;
using VibeResearching.Contracts.Collab;

namespace Aevatar.Agents.Cognitive.Researching.Materials;

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

        var header = score.HasValue
            ? $"[{f.Id}] {f.Title} (score={score.Value})"
            : $"[{f.Id}] {f.Title}";

        sb.AppendLine(header);

        var content = (f.Content ?? string.Empty).Replace("\r", "").Trim();
        if (content.Length > maxPerDoc)
            content = content[..maxPerDoc];
        sb.AppendLine(content);
        sb.AppendLine();
    }

    private static List<(MaterialFile File, int Score)> RankMaterials(IReadOnlyList<MaterialFile> facts, string query)
    {
        if (facts.Count == 0) return [];
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0)
            return facts.Select(f => (f, 0)).ToList();

        var tokens = Tokenize(query);
        var list = new List<(MaterialFile, int)>(facts.Count);
        foreach (var f in facts)
        {
            var content = (f.Content ?? string.Empty).ToLowerInvariant();
            var score = 0;
            foreach (var t in tokens)
            {
                if (content.Contains(t)) score++;
            }
            list.Add((f, score));
        }

        return list
            .OrderByDescending(x => x.Item2)
            .ThenBy(x => x.Item1.Id, StringComparer.Ordinal)
            .Select(x => (x.Item1, x.Item2))
            .ToList();
    }

    private static List<string> Tokenize(string s)
    {
        s = (s ?? string.Empty).ToLowerInvariant();
        var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();
        return tokens;
    }
}
