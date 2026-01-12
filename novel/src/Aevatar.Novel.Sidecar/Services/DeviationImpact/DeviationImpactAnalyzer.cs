using System.Text;
using System.Text.RegularExpressions;
using Aevatar.Novel.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services.DeviationImpact;

// ============================================================
//  DeviationImpactAnalyzer (v1)
//
//  - Turns a revision delta into:
//    - Structured DeviationSummary (5-20 items when possible)
//    - Human readable markdown reports
//    - Deterministic impact scan (keyword search in future chapters + outline)
//
//  NOTE:
//  - v1 is deterministic (no LLM). Later we can add AI enrichment in a separate step.
// ============================================================

public sealed class DeviationImpactAnalyzer
{
    private static readonly Regex TokenAscii = new(@"[A-Za-z0-9_]{3,}", RegexOptions.Compiled);
    private static readonly Regex TokenHan = new(@"[\u4e00-\u9fff]{2,6}", RegexOptions.Compiled);

    public DeviationSummary BuildDeviationSummary(
        string projectId,
        string storyId,
        string chapterId,
        long baseRevision,
        long editedRevision,
        string baseText,
        string editedText)
    {
        var baseLines = SplitLines(baseText);
        var editedLines = SplitLines(editedText);

        var ops = MyersDiff.DiffLines(baseLines, editedLines);
        var hunks = BuildHunks(ops, contextLines: 2, maxHunks: 20);

        var summary = new DeviationSummary
        {
            SummaryId = Guid.NewGuid().ToString("N"),
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            ProjectId = projectId,
            StoryId = storyId,
            ChapterId = chapterId,
            BaseRevision = baseRevision,
            EditedRevision = editedRevision
        };

        var idx = 0;
        foreach (var h in hunks)
        {
            idx++;
            var (category, severity) = GuessCategoryAndSeverity(h);

            var item = new DeviationItem
            {
                ItemId = $"{editedRevision}-{idx:00}",
                Category = category,
                Severity = severity,
                Title = h.Title,
                Description = h.Description,
                BaseExcerpt = new TextPayload
                {
                    Format = TextFormat.PlainText,
                    InlineText = h.BaseExcerpt,
                    Preview = Preview(h.BaseExcerpt)
                },
                EditedExcerpt = new TextPayload
                {
                    Format = TextFormat.PlainText,
                    InlineText = h.EditedExcerpt,
                    Preview = Preview(h.EditedExcerpt)
                }
            };

            item.Tags["hunk_index"] = idx.ToString();
            item.Tags["base_line_start"] = h.BaseLineStart.ToString();
            item.Tags["edited_line_start"] = h.EditedLineStart.ToString();
            summary.Items.Add(item);
        }

        // If changes exist but hunks are empty (shouldn't), add one coarse item.
        if (summary.Items.Count == 0 && !string.Equals(baseText, editedText, StringComparison.Ordinal))
        {
            summary.Items.Add(new DeviationItem
            {
                ItemId = $"{editedRevision}-01",
                Category = DeviationCategory.Plot,
                Severity = DeviationSeverity.Major,
                Title = "Chapter changed",
                Description = "Content changed (coarse diff).",
                BaseExcerpt = new TextPayload { Format = TextFormat.PlainText, InlineText = Preview(baseText), Preview = Preview(baseText) },
                EditedExcerpt = new TextPayload { Format = TextFormat.PlainText, InlineText = Preview(editedText), Preview = Preview(editedText) }
            });
        }

        return summary;
    }

    public string BuildDeviationReportMarkdown(DeviationSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Deviation Report");
        sb.AppendLine();
        sb.AppendLine($"- **summary_id**: `{summary.SummaryId}`");
        sb.AppendLine($"- **project_id**: `{summary.ProjectId}`");
        sb.AppendLine($"- **story_id**: `{summary.StoryId}`");
        sb.AppendLine($"- **chapter_id**: `{summary.ChapterId}`");
        sb.AppendLine($"- **base_revision**: `{summary.BaseRevision}`");
        sb.AppendLine($"- **edited_revision**: `{summary.EditedRevision}`");
        sb.AppendLine($"- **created_at**: `{summary.CreatedAt.ToDateTime():O}`");
        sb.AppendLine();

        sb.AppendLine("## Structured Items");
        sb.AppendLine();
        foreach (var item in summary.Items)
        {
            sb.AppendLine($"- **[{item.Severity}] [{item.Category}] {item.ItemId}**: {item.Title}");
            if (!string.IsNullOrWhiteSpace(item.Description))
                sb.AppendLine($"  - {item.Description}");

            sb.AppendLine("  - **base_excerpt**:");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine((item.BaseExcerpt?.InlineText ?? "").Trim());
            sb.AppendLine("```");

            sb.AppendLine("  - **edited_excerpt**:");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine((item.EditedExcerpt?.InlineText ?? "").Trim());
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("## Machine Readable (Protobuf JSON)");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(Google.Protobuf.JsonFormatter.Default.Format(summary));
        sb.AppendLine("```");

        return sb.ToString();
    }

    public string BuildDeviationPromptMarkdown(DeviationSummary summary, IReadOnlyList<string> suggestedScanTargets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Deviation Prompt (One-click)");
        sb.AppendLine();
        sb.AppendLine("You are a rigorous story assistant. Treat the author edits as the new canon.");
        sb.AppendLine("Your job: check whether future chapters / outline need updates due to the deviations below, and propose 2-3 concrete repair routes.");
        sb.AppendLine();
        sb.AppendLine("## Deviations (Structured)");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(Google.Protobuf.JsonFormatter.Default.Format(summary));
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Scan Targets (files)");
        sb.AppendLine();
        foreach (var t in suggestedScanTargets.Distinct(StringComparer.OrdinalIgnoreCase).Take(50))
        {
            sb.AppendLine($"- `{t}`");
        }
        sb.AppendLine();
        sb.AppendLine("## Output Requirements");
        sb.AppendLine();
        sb.AppendLine("- List impacted locations (file + excerpt).");
        sb.AppendLine("- For each impact, propose fixes.");
        sb.AppendLine("- Provide 2-3 alternative routes:");
        sb.AppendLine("  - conservative (minimal edits)");
        sb.AppendLine("  - aggressive (upgrade to climax)");
        sb.AppendLine("  - branch (keep both versions)");
        return sb.ToString();
    }

    public string BuildBackupOptionsMarkdown(DeviationSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Backup Options (v1 template)");
        sb.AppendLine();
        sb.AppendLine("This document is deterministic in v1 (no LLM). It provides a stable decision surface for the author.");
        sb.AppendLine();
        sb.AppendLine("## Option A — Propagate Forward (recommended default)");
        sb.AppendLine("- Treat deviations as canon and update future chapters/outline accordingly.");
        sb.AppendLine("- Keep a checklist of touched files and rerun Narrative Tests after each fix.");
        sb.AppendLine();
        sb.AppendLine("## Option B — Roll Back the Deviation");
        sb.AppendLine("- Revert the edited excerpt(s) to match the previous revision.");
        sb.AppendLine("- Useful when deviation was accidental (tone drift, typo, etc.).");
        sb.AppendLine();
        sb.AppendLine("## Option C — Branch & Merge");
        sb.AppendLine("- Create a rewrite branch and continue both versions in parallel.");
        sb.AppendLine("- Later selectively merge the better beats (author-driven).");
        sb.AppendLine();
        sb.AppendLine("## Deviation Summary (for reference)");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(Google.Protobuf.JsonFormatter.Default.Format(summary));
        sb.AppendLine("```");
        return sb.ToString();
    }

    public string BuildChangeImpactReportMarkdown(
        DeviationSummary summary,
        string storyRoot,
        string chapterFullPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Change Impact Report (deterministic v1)");
        sb.AppendLine();
        sb.AppendLine("This report scans future chapters/outlines for keyword hits extracted from the deviation hunks.");
        sb.AppendLine();

        var keywords = ExtractKeywords(summary).ToList();
        if (keywords.Count == 0)
        {
            sb.AppendLine("- No keywords extracted; impact scan skipped.");
            return sb.ToString();
        }

        sb.AppendLine("## Keywords");
        foreach (var k in keywords)
        {
            sb.AppendLine($"- `{k}`");
        }
        sb.AppendLine();

        var hits = ScanFutureChapters(storyRoot, chapterFullPath, keywords);

        sb.AppendLine("## Hits (future chapters / outline)");
        sb.AppendLine();
        if (hits.Count == 0)
        {
            sb.AppendLine("✅ No keyword hits found in future chapters/outline (this does NOT guarantee no logical impact).");
            return sb.ToString();
        }

        foreach (var h in hits.Take(200))
        {
            sb.AppendLine($"- **{h.Keyword}** in `{h.RelativePath}`");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(h.Excerpt.Trim());
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    public IReadOnlyList<string> SuggestScanTargets(string storyRoot, string chapterFullPath)
    {
        var targets = new List<string>();

        // Future chapters.
        var chaptersDir = Path.Combine(storyRoot, "chapters");
        if (Directory.Exists(chaptersDir))
        {
            foreach (var f in Directory.EnumerateFiles(chaptersDir, "*.txt", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(chapterFullPath), StringComparison.OrdinalIgnoreCase))
                    continue;
                targets.Add(f.Replace('\\', '/'));
            }
        }

        // Outline (best-effort)
        var outline = Path.Combine(storyRoot, "artifacts", "story_outline.md");
        if (File.Exists(outline))
            targets.Add(outline.Replace('\\', '/'));

        return targets;
    }

    // -------------------- internals --------------------

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n").Split('\n');

    private static string Preview(string text)
    {
        var s = (text ?? string.Empty).Replace("\r\n", "\n");
        return s.Length > 400 ? s[..400] : s;
    }

    private sealed record Hunk(
        int BaseLineStart,
        int EditedLineStart,
        string Title,
        string Description,
        string BaseExcerpt,
        string EditedExcerpt,
        string ChangedText);

    private static List<Hunk> BuildHunks(IReadOnlyList<MyersDiff.Op> ops, int contextLines, int maxHunks)
    {
        var hunks = new List<Hunk>();

        var baseLine = 0;
        var editedLine = 0;

        var buffer = new List<MyersDiff.Op>();
        var bufferBaseStart = 0;
        var bufferEditedStart = 0;
        var inChange = false;

        void Flush()
        {
            if (!inChange || buffer.Count == 0)
                return;

            // Build excerpts.
            var baseLines = new List<string>();
            var editedLines = new List<string>();
            var changedLines = new List<string>();

            foreach (var op in buffer)
            {
                switch (op.Kind)
                {
                    case MyersDiff.OpKind.Equal:
                        baseLines.Add(op.Text);
                        editedLines.Add(op.Text);
                        break;
                    case MyersDiff.OpKind.Delete:
                        baseLines.Add(op.Text);
                        changedLines.Add(op.Text);
                        break;
                    case MyersDiff.OpKind.Insert:
                        editedLines.Add(op.Text);
                        changedLines.Add(op.Text);
                        break;
                }
            }

            var baseExcerpt = string.Join("\n", baseLines).TrimEnd();
            var editedExcerpt = string.Join("\n", editedLines).TrimEnd();
            var changedText = string.Join("\n", changedLines).TrimEnd();

            hunks.Add(new Hunk(
                BaseLineStart: bufferBaseStart,
                EditedLineStart: bufferEditedStart,
                Title: "Changed text block",
                Description: $"Hunk at baseLine≈{bufferBaseStart}, editedLine≈{bufferEditedStart}",
                BaseExcerpt: baseExcerpt,
                EditedExcerpt: editedExcerpt,
                ChangedText: changedText));

            buffer.Clear();
            inChange = false;
        }

        // Sliding context handling:
        var recentEquals = new Queue<(int baseLine, int editedLine, string text)>();

        foreach (var op in ops)
        {
            if (op.Kind == MyersDiff.OpKind.Equal)
            {
                if (inChange)
                {
                    buffer.Add(op);
                    if (recentEquals.Count > contextLines)
                        recentEquals.Dequeue();

                    // If we have enough trailing equals, flush and start looking for new hunks.
                    if (buffer.TakeLast(contextLines).All(x => x.Kind == MyersDiff.OpKind.Equal))
                    {
                        Flush();
                        recentEquals.Clear();
                    }
                }
                else
                {
                    recentEquals.Enqueue((baseLine, editedLine, op.Text));
                    while (recentEquals.Count > contextLines)
                        recentEquals.Dequeue();
                }

                baseLine++;
                editedLine++;
            }
            else
            {
                if (!inChange)
                {
                    inChange = true;
                    bufferBaseStart = baseLine;
                    bufferEditedStart = editedLine;

                    // Prepend context.
                    foreach (var (b0, e0, t) in recentEquals)
                    {
                        buffer.Add(new MyersDiff.Op(MyersDiff.OpKind.Equal, t));
                        bufferBaseStart = Math.Min(bufferBaseStart, b0);
                        bufferEditedStart = Math.Min(bufferEditedStart, e0);
                    }
                }

                buffer.Add(op);

                if (op.Kind == MyersDiff.OpKind.Delete) baseLine++;
                if (op.Kind == MyersDiff.OpKind.Insert) editedLine++;
            }

            if (hunks.Count >= maxHunks)
                break;
        }

        Flush();
        return hunks;
    }

    private static (DeviationCategory category, DeviationSeverity severity) GuessCategoryAndSeverity(Hunk h)
    {
        var t = h.ChangedText ?? string.Empty;
        var len = t.Length;

        var severity = len switch
        {
            < 120 => DeviationSeverity.Minor,
            < 600 => DeviationSeverity.Major,
            _ => DeviationSeverity.Critical
        };

        // Very rough heuristics. Good enough for deterministic v1; LLM will improve later.
        if (Regex.IsMatch(t, @"\b(昨天|今天|明天|翌日|三天后|一年后|\d{4}年)\b"))
            return (DeviationCategory.Timeline, severity);

        if (Regex.IsMatch(t, @"(语气|口吻|更像|更偏|更强烈|更克制)"))
            return (DeviationCategory.Tone, severity);

        if (Regex.IsMatch(t, @"(设定|规则|境界|体系|法则|术式|咒语|魔法)"))
            return (DeviationCategory.CanonSetting, severity);

        if (Regex.IsMatch(t, @"[“”\""]"))
            return (DeviationCategory.Style, severity);

        return (DeviationCategory.Plot, severity);
    }

    private static IEnumerable<string> ExtractKeywords(DeviationSummary summary)
    {
        var pool = new StringBuilder();
        foreach (var i in summary.Items)
        {
            pool.AppendLine(i.BaseExcerpt?.InlineText ?? "");
            pool.AppendLine(i.EditedExcerpt?.InlineText ?? "");
        }

        var text = pool.ToString();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in TokenAscii.Matches(text))
        {
            var s = m.Value;
            if (s.Length >= 3)
                set.Add(s);
        }

        foreach (Match m in TokenHan.Matches(text))
        {
            var s = m.Value;
            if (s.Length >= 2)
                set.Add(s);
        }

        // Drop super common words.
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "然后","但是","因为","所以","一个","我们","他们","自己","什么","这样","这里","那里","时候","可以"
        };

        return set.Where(s => !stop.Contains(s)).Take(15);
    }

    private sealed record ImpactHit(string Keyword, string RelativePath, string Excerpt);

    private static List<ImpactHit> ScanFutureChapters(string storyRoot, string chapterFullPath, List<string> keywords)
    {
        var hits = new List<ImpactHit>();

        var chaptersDir = Path.Combine(storyRoot, "chapters");
        if (!Directory.Exists(chaptersDir))
            return hits;

        var currentName = Path.GetFileName(chapterFullPath);
        var currentOrder = TryParseOrder(currentName);

        var files = Directory.EnumerateFiles(chaptersDir, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(f => new FileInfo(f))
            .OrderBy(f => TryParseOrder(f.Name) ?? int.MaxValue)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var fi in files)
        {
            if (string.Equals(fi.FullName, chapterFullPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var order = TryParseOrder(fi.Name);
            if (currentOrder is not null && order is not null && order <= currentOrder)
                continue;

            var text = SafeReadText(fi.FullName);
            if (string.IsNullOrEmpty(text))
                continue;

            foreach (var k in keywords)
            {
                var idx = text.IndexOf(k, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                var excerpt = BuildExcerpt(text, idx);
                var rel = Path.GetRelativePath(storyRoot, fi.FullName).Replace('\\', '/');
                hits.Add(new ImpactHit(k, rel, excerpt));
            }
        }

        // Outline best-effort.
        var outline = Path.Combine(storyRoot, "artifacts", "story_outline.md");
        if (File.Exists(outline))
        {
            var text = SafeReadText(outline);
            foreach (var k in keywords)
            {
                var idx = text.IndexOf(k, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var rel = Path.GetRelativePath(storyRoot, outline).Replace('\\', '/');
                hits.Add(new ImpactHit(k, rel, BuildExcerpt(text, idx)));
            }
        }

        // Cap for readability.
        return hits.Take(120).ToList();
    }

    private static string SafeReadText(string fullPath)
    {
        try
        {
            return File.ReadAllText(fullPath, Encoding.UTF8);
        }
        catch
        {
            return "";
        }
    }

    private static string BuildExcerpt(string text, int index)
    {
        var normalized = text.Replace("\r\n", "\n");
        var start = Math.Max(0, index - 80);
        var end = Math.Min(normalized.Length, index + 200);
        return normalized[start..end];
    }

    private static int? TryParseOrder(string filename)
    {
        var dash = filename.IndexOf('-', StringComparison.Ordinal);
        var prefix = dash > 0 ? filename[..dash] : "";
        if (int.TryParse(prefix, out var n))
            return n;
        return null;
    }
}


