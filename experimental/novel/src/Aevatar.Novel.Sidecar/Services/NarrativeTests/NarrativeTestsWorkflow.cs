using System.Text;
using Aevatar.Novel.Contracts;

namespace Aevatar.Novel.Sidecar.Services.NarrativeTests;

// ============================================================
//  NarrativeTestsWorkflow (shared helpers)
//
//  Keep workflow-related helpers in one place so both:
//  - hosted services (bridges)
//  - Aevatar agents (actual executors)
//  can share the same rules.
// ============================================================

internal static class NarrativeTestsWorkflow
{
    public static bool ShouldTriggerTests(SstFileChangedEvent fc)
    {
        var path = fc.FullPath ?? string.Empty;

        var ext = Path.GetExtension(path);
        if (!ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".md", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/artifacts/tests/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.EndsWith("/artifacts/tests/narrative_tests.md", StringComparison.OrdinalIgnoreCase))
            return false;

        if (normalized.Contains("/chapters/", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return true;

        if (normalized.EndsWith("/artifacts/tests/narrative_tests.md", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static string? TryGetStoryRoot(string fullPath)
    {
        var normalized = fullPath.Replace('\\', '/');

        var chaptersIdx = normalized.LastIndexOf("/chapters/", StringComparison.OrdinalIgnoreCase);
        if (chaptersIdx >= 0)
        {
            var chaptersDir = fullPath[..(chaptersIdx + "/chapters".Length)].TrimEnd('/', '\\');
            return Directory.GetParent(chaptersDir)?.FullName;
        }

        var artifactsIdx = normalized.LastIndexOf("/artifacts/", StringComparison.OrdinalIgnoreCase);
        if (artifactsIdx >= 0)
        {
            return fullPath[..artifactsIdx].TrimEnd('/', '\\');
        }

        return null;
    }

    public static string BuildReportMarkdown(
        NarrativeTestRunResult result,
        string storyId,
        string chapterId,
        string storyRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Narrative Test Report");
        sb.AppendLine();
        sb.AppendLine($"- **run_id**: `{result.Summary.RunId}`");
        sb.AppendLine($"- **status**: `{result.Summary.Status}`");
        sb.AppendLine($"- **story_id**: `{storyId}`");
        if (!string.IsNullOrWhiteSpace(chapterId))
            sb.AppendLine($"- **trigger_chapter_id**: `{chapterId}`");
        sb.AppendLine($"- **started_at**: `{result.Summary.StartedAt.ToDateTime():O}`");
        sb.AppendLine($"- **ended_at**: `{result.Summary.EndedAt.ToDateTime():O}`");
        sb.AppendLine();

        if (result.Notes.Count > 0)
        {
            sb.AppendLine("## Notes");
            foreach (var n in result.Notes)
            {
                sb.AppendLine($"- {n}");
            }
            sb.AppendLine();
        }

        if (result.Summary.Failures.Count == 0)
        {
            sb.AppendLine("## Result");
            sb.AppendLine();
            sb.AppendLine("✅ **All tests passed.**");
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine("## Failures");
        sb.AppendLine();
        foreach (var f in result.Summary.Failures)
        {
            sb.AppendLine($"- **[{f.Severity}] {f.CaseId}**: {f.Message}");
            if (f.RelatedArtifact is not null && !string.IsNullOrWhiteSpace(f.RelatedArtifact.Uri))
            {
                sb.AppendLine($"  - **location**: `{f.RelatedArtifact.Uri}`");
            }
            if (f.Excerpt is not null && !string.IsNullOrWhiteSpace(f.Excerpt.InlineText))
            {
                sb.AppendLine("  - **excerpt**:");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(f.Excerpt.InlineText.Trim());
                sb.AppendLine("```");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Test Definition");
        sb.AppendLine();
        sb.AppendLine("This runner (v1) expects a JSON code block in:");
        sb.AppendLine();
        sb.AppendLine($"- `{Path.Combine(storyRoot, "artifacts", "tests", "narrative_tests.md").Replace('\\', '/')}`");
        sb.AppendLine();
        sb.AppendLine("Template:");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine("{");
        sb.AppendLine("  \"suiteId\": \"default\",");
        sb.AppendLine("  \"cases\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"id\": \"no-x-before-12\",");
        sb.AppendLine("      \"severity\": \"ERROR\",");
        sb.AppendLine("      \"type\": \"must_not_contain_before_chapter\",");
        sb.AppendLine("      \"untilChapter\": 12,");
        sb.AppendLine("      \"pattern\": \"X\",");
        sb.AppendLine("      \"message\": \"Before chapter 12, do not mention X.\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        sb.AppendLine("```");

        return sb.ToString();
    }
}


