using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class TextDiffTool : AevatarToolBase
{
    public override string Name => "text_diff";
    public override string Description => "Compute a simple line-based diff.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "diff", "text", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["before"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Original text."
                },
                ["after"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Updated text."
                },
                ["max_lines"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "Max diff lines (default: 400).",
                    DefaultValue = 400,
                    Minimum = 50,
                    Maximum = 5000
                }
            },
            Required = new[] { "before", "after" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "before", out var before))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "before_required" }));
        if (!FileToolHelpers.TryGetString(parameters, "after", out var after))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "after_required" }));

        var maxLines = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("max_lines"), 400, 50, 5000);
        var diff = BuildDiff(before, after, maxLines, out var truncated);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            diff,
            truncated
        }));
    }

    private static string BuildDiff(string before, string after, int maxLines, out bool truncated)
    {
        var a = SplitLines(before);
        var b = SplitLines(after);

        var lcs = BuildLcsTable(a, b);
        var lines = new List<string>();

        var i = 0;
        var j = 0;
        while (i < a.Count && j < b.Count)
        {
            if (a[i] == b[j])
            {
                lines.Add(" " + a[i]);
                i++;
                j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                lines.Add("-" + a[i]);
                i++;
            }
            else
            {
                lines.Add("+" + b[j]);
                j++;
            }

            if (lines.Count >= maxLines)
                break;
        }

        while (lines.Count < maxLines && i < a.Count)
        {
            lines.Add("-" + a[i]);
            i++;
        }

        while (lines.Count < maxLines && j < b.Count)
        {
            lines.Add("+" + b[j]);
            j++;
        }

        truncated = lines.Count >= maxLines && (i < a.Count || j < b.Count);
        return string.Join("\n", lines);
    }

    private static List<string> SplitLines(string text)
    {
        return text.Replace("\r", "").Split('\n').ToList();
    }

    private static int[,] BuildLcsTable(List<string> a, List<string> b)
    {
        var m = a.Count;
        var n = b.Count;
        var table = new int[m + 1, n + 1];
        for (var i = m - 1; i >= 0; i--)
        {
            for (var j = n - 1; j >= 0; j--)
            {
                if (a[i] == b[j])
                    table[i, j] = table[i + 1, j + 1] + 1;
                else
                    table[i, j] = Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }
        return table;
    }
}
