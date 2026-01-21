using System.Text.RegularExpressions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  TextReplaceTool
//
//  说明：
//  - 对文本进行替换（可选忽略大小写）
// ============================================================
public sealed class TextReplaceTool : AevatarToolBase
{
    public override string Name => "text_replace";
    public override string Description => "Replace text with optional case-insensitive matching.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "text", "replace", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["text"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Original text."
                },
                ["find"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Text to find."
                },
                ["replace"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Replacement text."
                },
                ["ignore_case"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Ignore case (default: false)."
                },
                ["replace_all"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Replace all occurrences (default: true)."
                }
            },
            Required = new[] { "text", "find", "replace" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "text", out var text))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "text_required" }));
        if (!FileToolHelpers.TryGetString(parameters, "find", out var find))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "find_required" }));
        if (!FileToolHelpers.TryGetString(parameters, "replace", out var replace))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "replace_required" }));

        var ignoreCase = parameters.TryGetValue("ignore_case", out var ic) && FileToolHelpers.TryGetBool(ic);
        var replaceAll = !parameters.TryGetValue("replace_all", out var ra) || FileToolHelpers.TryGetBool(ra);

        var regex = new Regex(Regex.Escape(find), ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        var count = 0;
        string result;

        if (replaceAll)
        {
            result = regex.Replace(text, _ =>
            {
                count++;
                return replace;
            });
        }
        else
        {
            result = regex.Replace(text, _ =>
            {
                count++;
                return replace;
            }, 1);
        }

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            replaced = count,
            text = result
        }));
    }
}
