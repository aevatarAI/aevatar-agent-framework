using System.Globalization;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace SkillsMCPUnifiedDemo.Tools;

/// <summary>
/// Slugify 工具
/// - 输入：任意文本
/// - 输出：URL 友好的 slug（小写、连字符、去重）
/// </summary>
public sealed class SlugifyTool : AevatarToolBase
{
    public override string Name => "slugify";
    public override string Description => "Convert text to a URL-friendly slug.";
    public override ToolCategory Category => ToolCategory.Custom;
    public override string Version => "1.0.0";
    public override IList<string> Tags { get; } = new List<string> { "ai-tool", "text", "slug" };

    public override ToolParameters CreateParameters() => new()
    {
        Required = new List<string> { "text" },
        Items = new Dictionary<string, ToolParameter>
        {
            ["text"] = new ToolParameter
            {
                Type = "string",
                Required = true,
                Description = "Text to slugify"
            },
            ["max_length"] = new ToolParameter
            {
                Type = "integer",
                Description = "Max slug length (default 80, max 256)"
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

        var text = parameters.TryGetValue("text", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        var maxLen = GetInt(parameters, "max_length") ?? 80;
        maxLen = Math.Clamp(maxLen, 1, 256);

        var slug = Slugify(text, maxLen);

        return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(new
        {
            success = true,
            slug
        }));
    }

    private static int? GetInt(Dictionary<string, object> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var v) || v == null) return null;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (v is double d) return (int)d;
        return int.TryParse(v.ToString(), out var parsed) ? parsed : null;
    }

    private static string Slugify(string input, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // ============================================================
        //  简单、稳定、可预测的 slugify：
        //  1) Normalize(FormD) 去音标
        //  2) 保留字母/数字；其他分隔符统一变 '-'
        //  3) 连续 '-' 压缩；首尾 '-' 去除；长度截断
        // ============================================================
        var s = input.Trim().ToLowerInvariant();
        var normalized = s.Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder(normalized.Length);
        var lastWasDash = false;

        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasDash = false;
            }
            else
            {
                if (!lastWasDash)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }

            if (sb.Length >= maxLen)
                break;
        }

        var slug = sb.ToString().Trim('-');
        return slug;
    }
}


