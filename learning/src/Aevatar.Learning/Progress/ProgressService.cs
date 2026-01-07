using System.Text.Json;
using Aevatar.Learning.Contracts;
using Aevatar.Learning.Notebooks;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Learning.Progress;

// ============================================================
//  ProgressService (MVP)
//
//  目标：
//  - notebook-scoped 汇总：sources 数、reports 数、cards（due/new）、quiz 历史计数、最近活动时间
//  - 快速/有界：避免深度扫描或读取大文件（仅读取小 meta/state 文件）
//
//  输出：
//  - 使用 Protobuf contract：LearningProgressSummary
//  - 额外字段（如 reports 数）放入 tags map（string-only，bounded）
// ============================================================
public sealed class ProgressService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int MaxScanDirsPerCategory = 5_000;
    private const int MaxQuizAttemptLines = 20_000;

    public async Task<LearningProgressSummary> GetSummaryAsync(
        NotebookWorkspace workspace,
        DateTimeOffset? nowUtc = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        workspace.EnsureDirectories();

        var now = nowUtc ?? DateTimeOffset.UtcNow;

        var partial = false;
        DateTimeOffset? lastActivity = null;

        // sources/
        var totalSources = CountDirectories(workspace.SourcesDir, MaxScanDirsPerCategory, ct, out var sourcesPartial);
        partial |= sourcesPartial;
        lastActivity = Max(lastActivity, TryGetDirLastWriteUtc(workspace.SourcesDir));

        // reports/
        var totalReports = CountDirectories(workspace.ReportsDir, MaxScanDirsPerCategory, ct, out var reportsPartial);
        partial |= reportsPartial;
        lastActivity = Max(lastActivity, TryGetDirLastWriteUtc(workspace.ReportsDir));

        // cards/ (need due/new => read card.json)
        var (dueCards, newCards, cardsPartial, cardsLast) = await ScanCardsAsync(workspace, now, ct);
        partial |= cardsPartial;
        lastActivity = Max(lastActivity, cardsLast);

        // quizzes/ (count attempts)
        var (attempts, quizzesPartial, quizzesLast) = await ScanQuizzesAsync(workspace, ct);
        partial |= quizzesPartial;
        lastActivity = Max(lastActivity, quizzesLast);

        // skills/ (activity only)
        lastActivity = Max(lastActivity, TryGetDirLastWriteUtc(workspace.SkillsDir));

        var summary = new LearningProgressSummary
        {
            DueCards = dueCards,
            NewCards = newCards,
            TotalSources = totalSources,
            QuizzesTaken = attempts
        };

        if (lastActivity.HasValue)
            summary.LastActivity = Timestamp.FromDateTimeOffset(lastActivity.Value);

        // Tags (bounded, string-only) for extra metrics/debugging
        summary.Tags["partial"] = partial ? "true" : "false";
        summary.Tags["as_of"] = now.ToString("O");
        summary.Tags["total_reports"] = totalReports.ToString();

        return summary;
    }

    // ============================================================
    //  cards/
    // ============================================================

    private static async Task<(int Due, int New, bool Partial, DateTimeOffset? Last)> ScanCardsAsync(
        NotebookWorkspace ws,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        if (!Directory.Exists(ws.CardsDir))
            return (0, 0, false, null);

        var due = 0;
        var @new = 0;
        var partial = false;
        DateTimeOffset? last = null;

        var scanned = 0;
        foreach (var dir in Directory.EnumerateDirectories(ws.CardsDir))
        {
            ct.ThrowIfCancellationRequested();
            scanned++;
            if (scanned > MaxScanDirsPerCategory)
            {
                partial = true;
                break;
            }

            var cardPath = Path.Combine(dir, "card.json");
            if (!File.Exists(cardPath))
                continue;

            try
            {
                var json = await File.ReadAllTextAsync(cardPath, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var reps = root.TryGetProperty("repetitions", out var r) && r.TryGetInt32(out var rv) ? rv : 0;
                if (reps <= 0)
                {
                    @new++;
                }
                else
                {
                    if (TryGetDateTimeOffset(root, "dueAt", out var dueAt) && dueAt <= nowUtc)
                        due++;
                }

                if (TryGetDateTimeOffset(root, "updatedAt", out var updatedAt))
                    last = Max(last, updatedAt);
                else
                    last = Max(last, TryGetFileLastWriteUtc(cardPath));
            }
            catch
            {
                // best-effort
            }
        }

        return (due, @new, partial, last);
    }

    // ============================================================
    //  quizzes/
    // ============================================================

    private static async Task<(int Attempts, bool Partial, DateTimeOffset? Last)> ScanQuizzesAsync(
        NotebookWorkspace ws,
        CancellationToken ct)
    {
        if (!Directory.Exists(ws.QuizzesDir))
            return (0, false, null);

        var partial = false;
        DateTimeOffset? last = null;
        var attempts = 0;

        var scannedDirs = 0;
        foreach (var dir in Directory.EnumerateDirectories(ws.QuizzesDir))
        {
            ct.ThrowIfCancellationRequested();
            scannedDirs++;
            if (scannedDirs > MaxScanDirsPerCategory)
            {
                partial = true;
                break;
            }

            var quizJson = Path.Combine(dir, "quiz.json");
            last = Max(last, TryGetFileLastWriteUtc(quizJson));

            var attemptsPath = Path.Combine(dir, "attempts.jsonl");
            if (!File.Exists(attemptsPath))
                continue;

            last = Max(last, TryGetFileLastWriteUtc(attemptsPath));

            try
            {
                await using var fs = new FileStream(attemptsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync(ct);
                    if (line == null) break;
                    if (line.Trim().Length == 0) continue;
                    attempts++;
                    if (attempts >= MaxQuizAttemptLines)
                    {
                        partial = true;
                        return (attempts, partial, last);
                    }
                }
            }
            catch
            {
                // best-effort
            }
        }

        return (attempts, partial, last);
    }

    // ============================================================
    //  helpers
    // ============================================================

    private static int CountDirectories(string dir, int max, CancellationToken ct, out bool partial)
    {
        partial = false;
        if (!Directory.Exists(dir))
            return 0;

        var count = 0;
        foreach (var _ in Directory.EnumerateDirectories(dir))
        {
            ct.ThrowIfCancellationRequested();
            count++;
            if (count >= max)
            {
                partial = true;
                break;
            }
        }

        return count;
    }

    private static bool TryGetDateTimeOffset(JsonElement obj, string name, out DateTimeOffset value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        if (!obj.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind != JsonValueKind.String) return false;

        var s = p.GetString();
        return DateTimeOffset.TryParse(s, out value);
    }

    private static DateTimeOffset? TryGetDirLastWriteUtc(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            return new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetFileLastWriteUtc(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            return new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        return a.Value >= b.Value ? a : b;
    }
}


