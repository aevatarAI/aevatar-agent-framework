using System.Text;
using Microsoft.Extensions.Options;

namespace ScientificResearchAssistant.Api.Materials;

// ============================================================
//  MaterialsService (MVP)
//
//  Goal:
//  - Load axioms and reference materials from local files.
//  - Build a bounded "materials context" string to inject into LLM.
//
//  Design (borrowed taste from Notebook):
//  - Deterministic: same input files → same ids/order.
//  - Bounded: max files, max chars per file, max total injected chars.
//  - Coverage-first for axioms, relevance-next for references.
// ============================================================

public sealed class MaterialsService
{
    private static readonly string[] AllowedExtensions = [".md", ".txt"];

    private readonly IHostEnvironment _env;
    private readonly IOptions<MaterialsOptions> _options;
    private readonly ILogger<MaterialsService> _logger;

    public MaterialsService(IHostEnvironment env, IOptions<MaterialsOptions> options, ILogger<MaterialsService> logger)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MaterialsSnapshot> LoadAsync(string sessionId, string query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        sessionId = (sessionId ?? string.Empty).Trim();
        query = (query ?? string.Empty).Trim();

        var root = ResolveMaterialsRoot();
        var axiomsDir = Path.Combine(root, _options.Value.AxiomsDir ?? "axioms");
        var refsDir = Path.Combine(root, _options.Value.ReferencesDir ?? "references");

        var axioms = await LoadFolderAsync(kind: "axiom", folder: axiomsDir, ct);
        var refs = await LoadFolderAsync(kind: "reference", folder: refsDir, ct);

        var context = BuildContextString(axioms, refs, query, _options.Value);

        return new MaterialsSnapshot
        {
            SessionId = sessionId,
            LoadedAt = DateTimeOffset.UtcNow,
            RootDir = root,
            AxiomsDir = axiomsDir,
            ReferencesDir = refsDir,
            Axioms = axioms,
            References = refs,
            RenderedContext = context
        };
    }

    private string ResolveMaterialsRoot()
    {
        // contentRoot: scientific-research-assistant/src/ScientificResearchAssistant.Api
        var contentRoot = _env.ContentRootPath;
        var systemRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));

        var raw = (_options.Value.RootDir ?? "materials").Trim();
        var root = Path.IsPathRooted(raw)
            ? raw
            : Path.GetFullPath(Path.Combine(systemRoot, raw));

        return root;
    }

    private async Task<List<MaterialFile>> LoadFolderAsync(string kind, string folder, CancellationToken ct)
    {
        kind = (kind ?? string.Empty).Trim();
        folder = (folder ?? string.Empty).Trim();

        var list = new List<MaterialFile>(capacity: 32);
        if (kind.Length == 0 || folder.Length == 0)
            return list;

        if (!Directory.Exists(folder))
            return list;

        string Normalize(string s) => (s ?? string.Empty).Replace('\\', '/').Trim('/');

        // Deterministic: stable ordering by relative path.
        var all = Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(p => IsAllowed(p))
            .Select(p =>
            {
                var rel = Normalize(Path.GetRelativePath(folder, p));
                return new { path = p, rel };
            })
            .OrderBy(x => x.rel, StringComparer.Ordinal)
            .Take(Math.Max(0, _options.Value.MaxFiles))
            .ToList();

        foreach (var f in all)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var content = await ReadFileBoundedAsync(f.path, _options.Value.MaxFileChars, ct);
                var title = InferTitle(f.rel, content);
                var id = $"{kind}:{f.rel}";

                list.Add(new MaterialFile
                {
                    Kind = kind,
                    Id = id,
                    Title = title,
                    RelativePath = f.rel,
                    FullPath = f.path,
                    Content = content
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[SRA] Failed to read material file: {Path}", f.path);
            }
        }

        return list;
    }

    private static bool IsAllowed(string path)
    {
        var ext = Path.GetExtension(path ?? string.Empty);
        if (string.IsNullOrWhiteSpace(ext))
            return false;

        // Skip hidden/system-ish files
        var name = Path.GetFileName(path);
        if (name.StartsWith(".", StringComparison.Ordinal))
            return false;

        return AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadFileBoundedAsync(string path, int maxChars, CancellationToken ct)
    {
        maxChars = Math.Clamp(maxChars, 1, 500_000);

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var sb = new StringBuilder(capacity: Math.Min(maxChars, 4096));
        var buf = new char[2048];
        while (!sr.EndOfStream && sb.Length < maxChars)
        {
            ct.ThrowIfCancellationRequested();

            var remaining = maxChars - sb.Length;
            var n = await sr.ReadAsync(buf.AsMemory(0, Math.Min(buf.Length, remaining)), ct);
            if (n <= 0) break;
            sb.Append(buf, 0, n);
        }

        var s = sb.ToString().Replace("\r", "").Trim();
        return s;
    }

    private static string InferTitle(string rel, string content)
    {
        // Prefer first markdown heading; fallback to filename.
        var lines = (content ?? string.Empty)
            .Replace("\r", "")
            .Split('\n')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Take(32)
            .ToList();

        foreach (var line in lines)
        {
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                var title = line.TrimStart('#').Trim();
                if (title.Length > 0)
                    return title;
            }
        }

        return Path.GetFileNameWithoutExtension(rel) ?? rel;
    }

    private static string BuildContextString(
        IReadOnlyList<MaterialFile> axioms,
        IReadOnlyList<MaterialFile> references,
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

        // ------------------------------------------------------------
        // Coverage-first: axioms are "hard constraints" - always include.
        // ------------------------------------------------------------
        AppendSection("AXIOMS:");
        if (axioms.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var a in axioms)
            {
                if (sb.Length >= maxTotal) break;
                AppendMaterial(sb, a, maxPerDoc, maxTotal);
            }
        }

        // ------------------------------------------------------------
        // Relevance-next: references are large; pick top by lexical score.
        // ------------------------------------------------------------
        AppendSection("REFERENCES (relevance-ranked):");
        if (references.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            var ranked = RankReferences(references, query)
                .Take(24)
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

        // Keep a blank line separator to make model parsing easier.
        if (content.Length > 0)
            sb.AppendLine(content);
        sb.AppendLine();
    }

    private static IEnumerable<(MaterialFile File, int Score)> RankReferences(IReadOnlyList<MaterialFile> refs, string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            // Deterministic fallback: keep file order.
            foreach (var f in refs)
                yield return (f, 0);
            yield break;
        }

        var terms = q
            .Split(' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

            // Mild bias to title matches.
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

        // Score all, then stable sort: score desc, id asc.
        var ranked = refs
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
    public required DateTimeOffset LoadedAt { get; init; }

    public required string RootDir { get; init; }
    public required string AxiomsDir { get; init; }
    public required string ReferencesDir { get; init; }

    public required List<MaterialFile> Axioms { get; init; }
    public required List<MaterialFile> References { get; init; }

    /// <summary>
    /// Bounded string for LLM injection.
    /// </summary>
    public required string RenderedContext { get; init; }
}

public sealed record MaterialFile
{
    public required string Kind { get; init; }            // axiom | reference
    public required string Id { get; init; }              // stable id: {kind}:{relativePath}
    public required string Title { get; init; }           // inferred title
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public required string Content { get; init; }         // bounded read
}


