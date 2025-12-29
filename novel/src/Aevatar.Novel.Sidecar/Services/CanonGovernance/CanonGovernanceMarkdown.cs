using System.Text;
using Aevatar.Novel.Sidecar.Services.DeviationImpact;

namespace Aevatar.Novel.Sidecar.Services.CanonGovernance;

internal static class CanonGovernanceMarkdown
{
    public static string BuildCanonChangeRecordMarkdown(
        string recordId,
        string storyId,
        string changedRelativePath,
        long baseRevision,
        long editedRevision,
        string baseText,
        string editedText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Canon Change Record");
        sb.AppendLine();
        sb.AppendLine("> 目的：任何设定变更必须显式记录“原因 / 影响范围 / 是否追溯改前文”，避免设定泥石流。");
        sb.AppendLine();

        sb.AppendLine("## Metadata");
        sb.AppendLine();
        sb.AppendLine($"- **record_id**: `{recordId}`");
        sb.AppendLine($"- **story_id**: `{storyId}`");
        sb.AppendLine($"- **changed_file**: `{changedRelativePath}`");
        sb.AppendLine($"- **base_revision**: `{baseRevision}`");
        sb.AppendLine($"- **edited_revision**: `{editedRevision}`");
        sb.AppendLine();

        sb.AppendLine("## Required Fields (author must fill)");
        sb.AppendLine();
        sb.AppendLine("- **reason**: _TODO_");
        sb.AppendLine("- **impact_scope**: _TODO_ (e.g. story-only / volume / project-wide)");
        sb.AppendLine("- **trace_back_required**: _TODO_ (yes/no)");
        sb.AppendLine("- **trace_back_targets**: _TODO_ (list files/chapters to backfill)");
        sb.AppendLine();

        sb.AppendLine("## Diff (deterministic v1)");
        sb.AppendLine();
        sb.AppendLine("```diff");
        sb.AppendLine(BuildUnifiedDiff(baseText, editedText));
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Notes / Decisions");
        sb.AppendLine();
        sb.AppendLine("- _TODO_");
        sb.AppendLine();

        return sb.ToString();
    }

    private static string BuildUnifiedDiff(string baseText, string editedText)
    {
        var a = (baseText ?? "").Replace("\r\n", "\n").Split('\n');
        var b = (editedText ?? "").Replace("\r\n", "\n").Split('\n');
        var ops = MyersDiff.DiffLines(a, b);

        var sb = new StringBuilder();
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case MyersDiff.OpKind.Equal:
                    sb.AppendLine(" " + op.Text);
                    break;
                case MyersDiff.OpKind.Delete:
                    sb.AppendLine("-" + op.Text);
                    break;
                case MyersDiff.OpKind.Insert:
                    sb.AppendLine("+" + op.Text);
                    break;
            }
        }
        return sb.ToString().TrimEnd();
    }
}


