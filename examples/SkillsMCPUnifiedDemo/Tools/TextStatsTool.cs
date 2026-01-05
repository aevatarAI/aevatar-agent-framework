using System.Text.Json;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace SkillsMCPUnifiedDemo.Tools;

/// <summary>
/// 文本统计工具
/// - 输入：任意文本
/// - 输出：字符/单词/行数等基础统计
/// </summary>
public sealed class TextStatsTool : AevatarToolBase
{
    public override string Name => "text_stats";
    public override string Description => "Compute basic statistics for a text (chars/words/lines).";
    public override ToolCategory Category => ToolCategory.Custom;
    public override string Version => "1.0.0";
    public override IList<string> Tags { get; } = new List<string> { "ai-tool", "text", "analysis" };

    public override ToolParameters CreateParameters() => new()
    {
        Required = new List<string> { "text" },
        Items = new Dictionary<string, ToolParameter>
        {
            ["text"] = new ToolParameter
            {
                Type = "string",
                Required = true,
                Description = "Text to analyze"
            },
            ["count_whitespace"] = new ToolParameter
            {
                Type = "boolean",
                Description = "If false, exclude whitespace from character count (default true)"
            }
        }
    };

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var text = GetString(parameters, "text") ?? string.Empty;
        var countWhitespace = GetBool(parameters, "count_whitespace") ?? true;

        // ============================================================
        //  统计逻辑（尽量无分支、可读、可预测）
        // ============================================================
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Length == 0 ? 0 : 1 + CountChar(normalized, '\n');

        var chars = countWhitespace ? normalized.Length : CountNonWhitespace(normalized);
        var words = CountWords(normalized);

        var result = new
        {
            success = true,
            chars,
            words,
            lines,
            preview = normalized.Length <= 120 ? normalized : normalized[..120] + "..."
        };

        return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(result));
    }

    private static string? GetString(Dictionary<string, object> parameters, string key)
    {
        return parameters.TryGetValue(key, out var v) ? v?.ToString() : null;
    }

    private static bool? GetBool(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var v) || v == null) return null;
        if (v is bool b) return b;
        return bool.TryParse(v.ToString(), out var parsed) ? parsed : null;
    }

    private static int CountChar(string s, char c)
    {
        var n = 0;
        foreach (var ch in s)
        {
            if (ch == c) n++;
        }
        return n;
    }

    private static int CountNonWhitespace(string s)
    {
        var n = 0;
        foreach (var ch in s)
        {
            if (!char.IsWhiteSpace(ch)) n++;
        }
        return n;
    }

    private static int CountWords(string s)
    {
        // 简单定义：连续的字母/数字/下划线算一个 word（不追求 NLP 完美）
        var inWord = false;
        var n = 0;

        foreach (var ch in s)
        {
            var isWordChar = char.IsLetterOrDigit(ch) || ch == '_';
            if (isWordChar)
            {
                if (!inWord)
                {
                    n++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return n;
    }
}


