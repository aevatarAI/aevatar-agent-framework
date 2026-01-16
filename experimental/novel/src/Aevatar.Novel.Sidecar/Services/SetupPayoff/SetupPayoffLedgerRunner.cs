using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Novel.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services.SetupPayoff;

// ============================================================
//  SetupPayoffLedgerRunner (v1 deterministic)
//
//  SSOT:
//  - Ledger definition: artifacts/ledger/setup_payoff_ledger.md (author editable)
//
//  Derived:
//  - artifacts/ledger/reports/<run_id>_setup_payoff_report.md
//
//  Ledger format:
//  - a markdown file containing a ```json block with:
//      { "entries": [ ... ] }
// ============================================================

public sealed class SetupPayoffLedgerRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<SetupPayoffRunResult> RunAsync(
        string projectRoot,
        string storyRoot,
        string triggerRelativePath,
        CancellationToken ct)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        var ledgerPath = Path.Combine(storyRoot, "artifacts", "ledger", "setup_payoff_ledger.md");
        var chaptersDir = Path.Combine(storyRoot, "chapters");

        var notes = new List<string>();

        var entries = new List<EntryDef>();
        if (!File.Exists(ledgerPath))
        {
            notes.Add($"Ledger file not found: `{ledgerPath}`");
            notes.Add("Create it and add a JSON code block (see report template).");
        }
        else
        {
            var md = await File.ReadAllTextAsync(ledgerPath, Encoding.UTF8, ct);
            if (!TryExtractFirstJsonBlock(md, out var json))
            {
                notes.Add("No ```json code block found in setup_payoff_ledger.md. v1 runner only executes deterministic JSON ledger entries.");
            }
            else
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<LedgerDef>(json, JsonOptions);
                    entries = parsed?.Entries ?? new List<EntryDef>();
                }
                catch (Exception ex)
                {
                    notes.Add($"Failed to parse JSON ledger block: {ex.Message}");
                }
            }
        }

        var chapters = Directory.Exists(chaptersDir)
            ? await LoadChaptersAsync(chaptersDir, ct)
            : new List<ChapterFile>();

        if (chapters.Count == 0)
        {
            notes.Add($"No chapter .txt files found under `{chaptersDir.Replace('\\', '/')}`.");
        }

        var results = Evaluate(entries, chapters);

        var endedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        var summary = new SetupPayoffRunSummary(
            runId,
            startedAt,
            endedAt,
            results.Count(r => r.Status == EntryStatus.Open),
            results.Count(r => r.Status == EntryStatus.Paid),
            results.Count(r => r.Status == EntryStatus.Broken),
            results.Count(r => r.IsDueSoon));

        return new SetupPayoffRunResult(
            runId,
            ledgerPath,
            triggerRelativePath,
            summary,
            notes,
            results);
    }

    public string BuildReportMarkdown(SetupPayoffRunResult run, string storyId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Setup / Payoff Ledger Report (deterministic v1)");
        sb.AppendLine();
        sb.AppendLine($"- **run_id**: `{run.RunId}`");
        sb.AppendLine($"- **story_id**: `{storyId}`");
        sb.AppendLine($"- **trigger**: `{run.TriggerRelativePath}`");
        sb.AppendLine($"- **ledger**: `{run.LedgerPath.Replace('\\', '/')}`");
        sb.AppendLine($"- **started_at**: `{run.Summary.StartedAt.ToDateTime():O}`");
        sb.AppendLine($"- **ended_at**: `{run.Summary.EndedAt.ToDateTime():O}`");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **open**: `{run.Summary.OpenCount}`");
        sb.AppendLine($"- **paid**: `{run.Summary.PaidCount}`");
        sb.AppendLine($"- **broken**: `{run.Summary.BrokenCount}`");
        sb.AppendLine($"- **due_soon**: `{run.Summary.DueSoonCount}`");
        sb.AppendLine();

        if (run.Notes.Count > 0)
        {
            sb.AppendLine("## Notes");
            foreach (var n in run.Notes)
                sb.AppendLine($"- {n}");
            sb.AppendLine();
        }

        sb.AppendLine("## Action Queue (author-driven)");
        sb.AppendLine();
        foreach (var r in run.Results.Where(x => x.Status != EntryStatus.Paid).OrderByDescending(x => x.IsDueSoon).ThenBy(x => x.Id))
        {
            var badge = r.IsDueSoon ? "DUE_SOON" : r.Status.ToString().ToUpperInvariant();
            sb.AppendLine($"- **[{badge}] {r.Id}**: {r.Title}");
            foreach (var s in r.Suggestions)
                sb.AppendLine($"  - {s}");
        }
        sb.AppendLine();

        sb.AppendLine("## Entries");
        sb.AppendLine();
        foreach (var r in run.Results.OrderBy(x => x.Id))
        {
            sb.AppendLine($"### {r.Id} — {r.Title}");
            sb.AppendLine();
            sb.AppendLine($"- **status**: `{r.Status}`");
            if (r.IsDueSoon) sb.AppendLine($"- **due_soon**: `true`");
            if (!string.IsNullOrWhiteSpace(r.Type)) sb.AppendLine($"- **type**: `{r.Type}`");
            if (!string.IsNullOrWhiteSpace(r.Severity)) sb.AppendLine($"- **severity**: `{r.Severity}`");
            if (r.DeadlineChapter > 0) sb.AppendLine($"- **deadline_chapter**: `{r.DeadlineChapter}`");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(r.SetupHit?.RelativePath))
            {
                sb.AppendLine($"- **setup_hit**: `{r.SetupHit.RelativePath}`");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(r.SetupHit.Excerpt.Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(r.PayoffHit?.RelativePath))
            {
                sb.AppendLine($"- **payoff_hit**: `{r.PayoffHit.RelativePath}`");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(r.PayoffHit.Excerpt.Trim());
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (r.Suggestions.Count > 0)
            {
                sb.AppendLine("- **suggestions**:");
                foreach (var s in r.Suggestions)
                    sb.AppendLine($"  - {s}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Ledger Template");
        sb.AppendLine();
        sb.AppendLine("Create/edit this file:");
        sb.AppendLine();
        sb.AppendLine("- `artifacts/ledger/setup_payoff_ledger.md`");
        sb.AppendLine();
        sb.AppendLine("Example:");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"entries\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"id\": \"promise-a\",");
        sb.AppendLine("      \"type\": \"promise\",");
        sb.AppendLine("      \"title\": \"A promises to reveal X\",");
        sb.AppendLine("      \"setupPattern\": \"I will tell you X\",");
        sb.AppendLine("      \"payoffPattern\": \"X is revealed\",");
        sb.AppendLine("      \"deadlineChapter\": 12,");
        sb.AppendLine("      \"regex\": false,");
        sb.AppendLine("      \"severity\": \"MAJOR\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return sb.ToString();
    }

    // ----------------- evaluation -----------------

    private static List<EntryResult> Evaluate(List<EntryDef> entries, List<ChapterFile> chapters)
    {
        var list = new List<EntryResult>();
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Id))
                continue;

            var setup = FindFirstHit(chapters, e.SetupPattern, e.Regex);
            var payoff = FindFirstHit(chapters, e.PayoffPattern, e.Regex, afterOrder: setup?.Order);

            var status = EntryStatus.Open;
            if (setup is null)
                status = EntryStatus.Broken;
            else if (payoff is not null)
                status = EntryStatus.Paid;

            var dueSoon = false;
            var deadline = e.DeadlineChapter ?? 0;
            if (status == EntryStatus.Open && deadline > 0)
            {
                var maxOrder = chapters.Select(c => c.Order ?? 0).DefaultIfEmpty(0).Max();
                if (maxOrder >= deadline - 1)
                    dueSoon = true;
            }

            var setupCount = CountHits(chapters, e.SetupPattern, e.Regex);
            var suggestions = new List<string>();

            if (status == EntryStatus.Broken)
            {
                suggestions.Add("Broken: setup pattern not found in current chapters. Either reintroduce the setup or update ledger patterns.");
            }
            else if (status == EntryStatus.Open && dueSoon)
            {
                suggestions.Add("Time to pay off: deadline is approaching. Decide how/when to resolve or intentionally defer (and update the ledger).");
            }

            if (status == EntryStatus.Open && setupCount >= 3)
            {
                suggestions.Add("Upgrade candidate: setup is referenced repeatedly; consider upgrading payoff to a climax or escalating stakes.");
            }

            list.Add(new EntryResult(
                e.Id,
                e.Type ?? "",
                e.Title ?? e.Id,
                e.Severity ?? "",
                deadline,
                status,
                dueSoon,
                setup,
                payoff,
                suggestions));
        }

        return list;
    }

    private static int CountHits(List<ChapterFile> chapters, string? pattern, bool? regex)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return 0;

        var isRegex = regex ?? false;
        Regex? re = null;
        if (isRegex)
        {
            try { re = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { return 0; }
        }

        var count = 0;
        foreach (var c in chapters)
        {
            var text = c.Content ?? "";
            if (string.IsNullOrEmpty(text)) continue;
            if (re is not null)
            {
                count += re.Matches(text).Count;
            }
            else
            {
                var idx = 0;
                while (true)
                {
                    idx = text.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    count++;
                    idx += Math.Max(1, pattern.Length);
                }
            }
        }
        return count;
    }

    private static MatchHit? FindFirstHit(
        List<ChapterFile> chapters,
        string? pattern,
        bool? regex,
        int? afterOrder = null)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return null;

        var isRegex = regex ?? false;
        Regex? re = null;
        if (isRegex)
        {
            try { re = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { return null; }
        }

        foreach (var c in chapters.OrderBy(x => x.Order ?? int.MaxValue))
        {
            // v1: allow payoff in the same chapter as setup (inclusive).
            // (Author-controlled patterns; later we can do "same chapter but after setup position".)
            if (afterOrder is not null && c.Order is not null && c.Order < afterOrder)
                continue;

            var text = c.Content ?? "";
            if (string.IsNullOrEmpty(text))
                continue;

            int idx;
            if (re is not null)
            {
                var m = re.Match(text);
                if (!m.Success) continue;
                idx = m.Index;
            }
            else
            {
                idx = text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
            }

            return new MatchHit(
                c.RelativePath,
                c.Order ?? 0,
                BuildExcerpt(text, idx));
        }

        return null;
    }

    private static string BuildExcerpt(string text, int index)
    {
        var normalized = (text ?? "").Replace("\r\n", "\n");
        var start = Math.Max(0, index - 80);
        var end = Math.Min(normalized.Length, index + 220);
        return normalized[start..end];
    }

    private static async Task<List<ChapterFile>> LoadChaptersAsync(string chaptersDir, CancellationToken ct)
    {
        var root = Directory.GetParent(chaptersDir)!.FullName;
        var files = Directory.EnumerateFiles(chaptersDir, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(f => TryParseOrder(f.Name) ?? int.MaxValue)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var list = new List<ChapterFile>(files.Count);
        foreach (var f in files)
        {
            var content = await File.ReadAllTextAsync(f.FullName, Encoding.UTF8, ct);
            list.Add(new ChapterFile(
                Path.GetRelativePath(root, f.FullName).Replace('\\', '/'),
                TryParseOrder(f.Name),
                content));
        }

        return list;
    }

    private static int? TryParseOrder(string filename)
    {
        var dash = filename.IndexOf('-', StringComparison.Ordinal);
        var prefix = dash > 0 ? filename[..dash] : string.Empty;
        if (int.TryParse(prefix, out var n))
            return n;
        return null;
    }

    private static bool TryExtractFirstJsonBlock(string markdown, out string json)
    {
        json = string.Empty;
        var start = markdown.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;

        start = markdown.IndexOf('\n', start);
        if (start < 0) return false;
        start += 1;

        var end = markdown.IndexOf("```", start, StringComparison.Ordinal);
        if (end < 0) return false;

        json = markdown[start..end].Trim();
        return !string.IsNullOrWhiteSpace(json);
    }

    // ----------------- internal models -----------------

    private sealed class LedgerDef
    {
        public List<EntryDef>? Entries { get; init; }
    }

    private sealed class EntryDef
    {
        public string Id { get; init; } = "";
        public string? Type { get; init; }
        public string? Title { get; init; }
        public string? Severity { get; init; }
        public string? SetupPattern { get; init; }
        public string? PayoffPattern { get; init; }
        public int? DeadlineChapter { get; init; }
        public bool? Regex { get; init; }
    }

    private sealed record ChapterFile(
        string RelativePath,
        int? Order,
        string Content);
}

public sealed record SetupPayoffRunSummary(
    string RunId,
    Timestamp StartedAt,
    Timestamp EndedAt,
    int OpenCount,
    int PaidCount,
    int BrokenCount,
    int DueSoonCount);

public enum EntryStatus
{
    Open,
    Paid,
    Broken
}

public sealed record MatchHit(
    string RelativePath,
    int Order,
    string Excerpt);

public sealed record EntryResult(
    string Id,
    string Type,
    string Title,
    string Severity,
    int DeadlineChapter,
    EntryStatus Status,
    bool IsDueSoon,
    MatchHit? SetupHit,
    MatchHit? PayoffHit,
    IReadOnlyList<string> Suggestions);

public sealed record SetupPayoffRunResult(
    string RunId,
    string LedgerPath,
    string TriggerRelativePath,
    SetupPayoffRunSummary Summary,
    IReadOnlyList<string> Notes,
    IReadOnlyList<EntryResult> Results);


