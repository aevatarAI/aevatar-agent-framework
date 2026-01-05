using System.Text;
using Microsoft.Extensions.Options;

namespace ScientificResearchAssistant.Api.Materials;

// ============================================================
//  MaterialsService (MVP)
//
//  Goal:
//  - Load materials (NotebookLM-style "sources") from local files.
//  - Build a bounded "materials context" string to inject into LLM.
//
//  Design (borrowed taste from Notebook):
//  - Deterministic: same input files → same ids/order.
//  - Bounded: max files, max chars per file, max total injected chars.
//  - Relevance-next: rank by query, inject only top sources within budget.
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

        var factsRoot = ResolveSystemPath(_options.Value.FactsDir ?? "facts");
        var sourcesRoot = ResolveSystemPath(_options.Value.SourcesDir ?? "sources");

        var facts = await LoadFolderAsync(kind: "fact", folder: factsRoot, ct);
        var sources = await LoadFolderAsync(kind: "source", folder: sourcesRoot, ct);

        var context = BuildContextString(facts, sources, query, _options.Value);

        return new MaterialsSnapshot
        {
            SessionId = sessionId,
            LoadedAt = DateTimeOffset.UtcNow,
            FactsDir = factsRoot,
            SourcesDir = sourcesRoot,
            Facts = facts,
            Sources = sources,
            RenderedContext = context
        };
    }

    // ============================================================
    //  Write-back (optional): persist verified notes as new sources
    // ============================================================

    public async Task<MaterialFile> SaveFactAsync(
        string title,
        string content,
        string? relativePath,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!_options.Value.AllowWrite)
            throw new InvalidOperationException("Facts write is disabled (Materials:AllowWrite=false).");

        title = (title ?? string.Empty).Trim();
        content = (content ?? string.Empty).Replace("\r", "").Trim();
        if (content.Length == 0)
            throw new ArgumentException("content is required", nameof(content));

        var maxWrite = Math.Clamp(_options.Value.MaxWriteChars, 1, 500_000);
        if (content.Length > maxWrite)
            content = content[..maxWrite];

        var factsRoot = ResolveSystemPath(_options.Value.FactsDir ?? "facts");
        Directory.CreateDirectory(factsRoot);

        var rel = NormalizeRelativePath(relativePath);
        if (rel.Length == 0)
        {
            // Default: {yyyyMMdd_HHmmss}_{slug}.md
            var slug = Slugify(title.Length == 0 ? "fact" : title, maxChars: 48);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            rel = $"{stamp}_{slug}.md";
        }

        if (!HasAllowedExtension(rel))
            rel += ".md";

        var full = Path.GetFullPath(Path.Combine(factsRoot, rel));
        EnsureWithinRoot(factsRoot, full);

        var md = BuildMarkdownFact(title, content);
        await File.WriteAllTextAsync(full, md, Encoding.UTF8, ct);

        var finalRel = NormalizeRelativePath(Path.GetRelativePath(factsRoot, full));
        var finalTitle = title.Length == 0 ? InferTitle(finalRel, md) : title;

        return new MaterialFile
        {
            Kind = "fact",
            Id = $"fact:{finalRel}",
            Title = finalTitle,
            RelativePath = finalRel,
            FullPath = full,
            Content = md
        };
    }
    private static string BuildMarkdownFact(string title, string content)
    {
        var t = (title ?? string.Empty).Trim();
        var body = (content ?? string.Empty).Replace("\r", "").Trim();

        // Minimal structure for future parsing.
        if (t.Length == 0)
            return body;

        return $"""
               # {t}

               {body}
               """;
    }

    private static bool HasAllowedExtension(string relPath)
    {
        var ext = Path.GetExtension(relPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(ext))
            return false;
        return AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string? value)
    {
        var s = (value ?? string.Empty).Replace('\\', '/').Trim();
        if (s.Length == 0) return string.Empty;

        // Prevent absolute paths.
        if (s.StartsWith("/", StringComparison.Ordinal))
            s = s.TrimStart('/');

        // Prevent traversal segments.
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var safe = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            if (p == "." || p == "..")
                continue;
            safe.Add(p);
        }

        return string.Join('/', safe);
    }

    private static void EnsureWithinRoot(string rootDir, string fullPath)
    {
        var root = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(fullPath);

        var prefix = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid path: write must stay within materials root.");
    }

    private static string Slugify(string input, int maxChars)
    {
        maxChars = Math.Clamp(maxChars, 8, 96);

        var s = (input ?? string.Empty).Trim();
        if (s.Length == 0) return "note";

        var sb = new StringBuilder(capacity: Math.Min(maxChars, 64));
        var prevDash = false;
        foreach (var ch in s)
        {
            if (sb.Length >= maxChars) break;

            var isAlphaNum = char.IsLetterOrDigit(ch);
            if (isAlphaNum)
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevDash = false;
                continue;
            }

            if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var outSlug = sb
            .ToString()
            .Trim('-')
            .Trim();

        return outSlug.Length == 0 ? "note" : outSlug;
    }

    private string ResolveSystemPath(string dirName)
    {
        // contentRoot: scientific-research-assistant/src/ScientificResearchAssistant.Api
        var contentRoot = _env.ContentRootPath;
        var systemRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        var raw = (dirName ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new InvalidOperationException("Knowledge directory name is empty.");

        return Path.IsPathRooted(raw)
            ? raw
            : Path.GetFullPath(Path.Combine(systemRoot, raw));
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
        var name = Path.GetFileName(path) ?? string.Empty;
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
        while (sb.Length < maxChars)
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
        IReadOnlyList<MaterialFile> facts,
        IReadOnlyList<MaterialFile> sources,
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

        // Facts first: higher-confidence, usually smaller.
        AppendSection("FACTS (verified, higher confidence):");
        if (facts.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var f in facts)
            {
                if (sb.Length >= maxTotal) break;
                AppendMaterial(sb, f, maxPerDoc, maxTotal);
            }
        }

        AppendSection("SOURCES (evidence, relevance-ranked):");
        if (sources.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            var ranked = RankMaterials(sources, query)
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

    private static IEnumerable<(MaterialFile File, int Score)> RankMaterials(IReadOnlyList<MaterialFile> files, string query)
    {
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            // Deterministic fallback: keep file order.
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
    public required DateTimeOffset LoadedAt { get; init; }

    public required string FactsDir { get; init; }
    public required string SourcesDir { get; init; }

    public required List<MaterialFile> Facts { get; init; }
    public required List<MaterialFile> Sources { get; init; }

    /// <summary>
    /// Bounded string for LLM injection.
    /// </summary>
    public required string RenderedContext { get; init; }
}

public sealed record MaterialFile
{
    public required string Kind { get; init; }            // fact | source
    public required string Id { get; init; }              // stable id: {kind}:{relativePath}
    public required string Title { get; init; }           // inferred title
    public required string RelativePath { get; init; }
    public required string FullPath { get; init; }
    public required string Content { get; init; }         // bounded read
}


