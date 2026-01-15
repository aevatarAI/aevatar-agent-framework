using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Novel.Contracts;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Novel.Sidecar.Services.NarrativeTests;

// ============================================================
//  NarrativeTestRunner (v1)
//
//  DESIGN:
//  - Tests are author-defined constraints stored as a .md file (SSOT).
//  - v1 supports a deterministic JSON code block embedded in markdown:
//      ```json
//      { "cases": [ ... ] }
//      ```
//  - When no JSON block exists, we still generate a report explaining how to add one.
//
//  NOTE:
//  - This runner is intentionally simple; advanced semantic checks can be added later via LLM.
// ============================================================

public sealed class NarrativeTestRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<NarrativeTestRunResult> RunAsync(
        string projectRoot,
        string storyRoot,
        string? triggerChapterPath,
        CancellationToken ct)
    {
        var startedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        var runId = Guid.NewGuid().ToString("N");

        var testsPath = Path.Combine(storyRoot, "artifacts", "tests", "narrative_tests.md");
        var chaptersDir = Path.Combine(storyRoot, "chapters");

        var failures = new List<NarrativeTestFailure>();
        var notes = new List<string>();

        if (!Directory.Exists(chaptersDir))
        {
            notes.Add($"Chapters directory not found: `{chaptersDir}`");
            return BuildResult(runId, startedAt, failures, notes);
        }

        var chapterFiles = await LoadChaptersAsync(chaptersDir, ct);
        if (chapterFiles.Count == 0)
        {
            notes.Add("No chapter .txt files found under `chapters/`.");
            return BuildResult(runId, startedAt, failures, notes);
        }

        if (!File.Exists(testsPath))
        {
            notes.Add($"Narrative tests file not found: `{testsPath}`");
            notes.Add("Create it and add a JSON code block (see report template).");
            return BuildResult(runId, startedAt, failures, notes);
        }

        var markdown = await File.ReadAllTextAsync(testsPath, Encoding.UTF8, ct);
        if (!TryExtractFirstJsonBlock(markdown, out var json))
        {
            notes.Add("No ```json code block found in narrative_tests.md. v1 runner only executes deterministic JSON tests.");
            notes.Add("Add a JSON code block with a 'cases' array (see template in report).");
            return BuildResult(runId, startedAt, failures, notes);
        }

        NarrativeTestSuiteDefinition? suite;
        try
        {
            suite = JsonSerializer.Deserialize<NarrativeTestSuiteDefinition>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            notes.Add($"Failed to parse JSON test block: {ex.Message}");
            return BuildResult(runId, startedAt, failures, notes);
        }

        var cases = suite?.Cases ?? new List<NarrativeTestCaseDefinition>();
        if (cases.Count == 0)
        {
            notes.Add("Test JSON block parsed, but it contains no cases.");
            return BuildResult(runId, startedAt, failures, notes);
        }

        foreach (var c in cases)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
            {
                failures.Add(new NarrativeTestFailure
                {
                    CaseId = "",
                    Severity = NarrativeTestSeverity.Error,
                    Message = "Invalid test case: missing 'id'."
                });
                continue;
            }

            var severity = ParseSeverity(c.Severity);
            var type = (c.Type ?? string.Empty).Trim().ToLowerInvariant();
            var pattern = c.Pattern ?? string.Empty;
            var regex = c.Regex ?? false;
            var message = c.Message ?? $"Test failed: {c.Id}";

            if (string.IsNullOrWhiteSpace(type))
            {
                failures.Add(new NarrativeTestFailure
                {
                    CaseId = c.Id,
                    Severity = severity,
                    Message = "Invalid test case: missing 'type'."
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(pattern))
            {
                failures.Add(new NarrativeTestFailure
                {
                    CaseId = c.Id,
                    Severity = severity,
                    Message = "Invalid test case: missing 'pattern'."
                });
                continue;
            }

            var scope = SelectChapters(chapterFiles, c, type);
            EvaluateCase(failures, c.Id, severity, type, pattern, regex, message, scope);
        }

        return BuildResult(runId, startedAt, failures, notes);
    }

    private static NarrativeTestRunResult BuildResult(
        string runId,
        Timestamp startedAt,
        List<NarrativeTestFailure> failures,
        List<string> notes)
    {
        var endedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        var status = failures.Count == 0
            ? NarrativeTestRunStatus.Passed
            : NarrativeTestRunStatus.Failed;

        var summary = new NarrativeTestRunSummary
        {
            RunId = runId,
            Status = status,
            StartedAt = startedAt,
            EndedAt = endedAt
        };
        summary.Failures.AddRange(failures);

        return new NarrativeTestRunResult(summary, notes);
    }

    private static NarrativeTestSeverity ParseSeverity(string? s)
    {
        var v = (s ?? string.Empty).Trim().ToUpperInvariant();
        return v switch
        {
            "INFO" => NarrativeTestSeverity.Info,
            "WARNING" => NarrativeTestSeverity.Warning,
            "ERROR" => NarrativeTestSeverity.Error,
            _ => NarrativeTestSeverity.Error
        };
    }

    private static List<ChapterFile> SelectChapters(
        List<ChapterFile> all,
        NarrativeTestCaseDefinition def,
        string type)
    {
        // Supports:
        // - must_not_contain_before_chapter: untilChapter (int)
        // - optional chapterRange: "1-12", "13+", "5"
        if (type == "must_not_contain_before_chapter")
        {
            var until = def.UntilChapter ?? 0;
            if (until <= 0) return all;
            return all.Where(c => c.Order is null || c.Order <= until).ToList();
        }

        if (!string.IsNullOrWhiteSpace(def.ChapterRange))
        {
            var range = def.ChapterRange.Trim();
            return ApplyRange(all, range);
        }

        return all;
    }

    private static List<ChapterFile> ApplyRange(List<ChapterFile> all, string range)
    {
        // Examples:
        // - "1-12"
        // - "13+"
        // - "5"
        if (range.EndsWith("+", StringComparison.Ordinal))
        {
            if (int.TryParse(range[..^1], out var start))
            {
                return all.Where(c => c.Order is null || c.Order >= start).ToList();
            }
        }

        if (range.Contains('-', StringComparison.Ordinal))
        {
            var parts = range.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var start) &&
                int.TryParse(parts[1], out var end))
            {
                return all.Where(c => c.Order is null || (c.Order >= start && c.Order <= end)).ToList();
            }
        }

        if (int.TryParse(range, out var single))
        {
            return all.Where(c => c.Order == single).ToList();
        }

        return all;
    }

    private static void EvaluateCase(
        List<NarrativeTestFailure> failures,
        string caseId,
        NarrativeTestSeverity severity,
        string type,
        string pattern,
        bool regex,
        string message,
        List<ChapterFile> chapters)
    {
        var matches = FindMatches(chapters, pattern, regex);

        var failed = type switch
        {
            "must_contain" => matches.Count == 0,
            "must_not_contain" => matches.Count > 0,
            "must_not_contain_before_chapter" => matches.Count > 0,
            _ => true // unknown type fails fast
        };

        if (!failed)
            return;

        // Pick first match (if any) for location.
        MatchHit? hit = matches.FirstOrDefault();

        var failure = new NarrativeTestFailure
        {
            CaseId = caseId,
            Severity = severity,
            Message = type switch
            {
                "must_contain" => message,
                "must_not_contain" => message,
                "must_not_contain_before_chapter" => message,
                _ => $"Unknown test type: {type}"
            }
        };

        if (hit is not null)
        {
            failure.RelatedArtifact = new ArtifactRef
            {
                ArtifactId = hit.ChapterId,
                Kind = ArtifactKind.Chapter,
                Title = hit.ChapterTitle,
                Uri = $"file://{hit.Path}"
            };

            failure.Excerpt = new TextPayload
            {
                Format = TextFormat.PlainText,
                InlineText = hit.Excerpt,
                Preview = hit.Excerpt.Length > 180 ? hit.Excerpt[..180] : hit.Excerpt
            };
        }

        failures.Add(failure);
    }

    private static List<MatchHit> FindMatches(List<ChapterFile> chapters, string pattern, bool regex)
    {
        var hits = new List<MatchHit>();

        Regex? re = null;
        if (regex)
        {
            try
            {
                re = new Regex(pattern, RegexOptions.Compiled);
            }
            catch
            {
                // Invalid regex -> treat as no matches (will fail must_contain, pass must_not_contain).
                return hits;
            }
        }

        foreach (var c in chapters)
        {
            var text = c.Content;
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
                idx = text.IndexOf(pattern, StringComparison.Ordinal);
                if (idx < 0) continue;
            }

            hits.Add(new MatchHit(
                c.Path,
                c.Id,
                c.Title,
                BuildExcerpt(text, idx)));
        }

        return hits;
    }

    private static string BuildExcerpt(string text, int index)
    {
        // Keep it short and readable.
        var start = Math.Max(0, index - 60);
        var end = Math.Min(text.Length, index + 120);
        var snippet = text[start..end].Replace("\r\n", "\n");
        return snippet.Length > 0 ? snippet : text[..Math.Min(180, text.Length)];
    }

    private static async Task<List<ChapterFile>> LoadChaptersAsync(string chaptersDir, CancellationToken ct)
    {
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
                Path: f.FullName,
                Id: Path.GetFileNameWithoutExtension(f.Name),
                Title: Path.GetFileNameWithoutExtension(f.Name),
                Order: TryParseOrder(f.Name),
                Content: content));
        }

        return list;
    }

    private static int? TryParseOrder(string filename)
    {
        // "001-xxx.txt" -> 1
        var name = filename;
        var dash = name.IndexOf('-', StringComparison.Ordinal);
        var prefix = dash > 0 ? name[..dash] : string.Empty;
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

    private sealed record ChapterFile(
        string Path,
        string Id,
        string Title,
        int? Order,
        string Content);

    private sealed record MatchHit(
        string Path,
        string ChapterId,
        string ChapterTitle,
        string Excerpt);

    // ----------------- JSON definition (v1) -----------------

    private sealed class NarrativeTestSuiteDefinition
    {
        public string? SuiteId { get; init; }
        public List<NarrativeTestCaseDefinition>? Cases { get; init; }
    }

    private sealed class NarrativeTestCaseDefinition
    {
        public string Id { get; init; } = string.Empty;
        public string? Title { get; init; }
        public string? Severity { get; init; } // INFO/WARNING/ERROR
        public string? Type { get; init; } // must_contain / must_not_contain / must_not_contain_before_chapter
        public string? Pattern { get; init; }
        public bool? Regex { get; init; }
        public string? Message { get; init; }

        public int? UntilChapter { get; init; }
        public string? ChapterRange { get; init; }
    }
}

public sealed record NarrativeTestRunResult(
    NarrativeTestRunSummary Summary,
    IReadOnlyList<string> Notes);


