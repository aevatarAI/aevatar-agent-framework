using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace SkillsMCPUnifiedDemo.Tools;

/// <summary>
/// JSON 美化工具
/// - 输入：JSON 字符串
/// - 输出：缩进后的 pretty JSON（字符串）
/// </summary>
public sealed class JsonPrettifyTool : AevatarToolBase
{
    public override string Name => "json_prettify";
    public override string Description => "Pretty-print a JSON string (returns formatted JSON).";
    public override ToolCategory Category => ToolCategory.Custom;
    public override string Version => "1.0.0";
    public override IList<string> Tags { get; } = new List<string> { "ai-tool", "json", "format" };

    public override ToolParameters CreateParameters() => new()
    {
        Required = new List<string> { "json" },
        Items = new Dictionary<string, ToolParameter>
        {
            ["json"] = new ToolParameter
            {
                Type = "string",
                Required = true,
                Description = "Raw JSON string"
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

        var raw = parameters.TryGetValue("json", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(new
            {
                success = false,
                error = "Parameter 'json' is required."
            }));
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(new
            {
                success = true,
                pretty
            }));
        }
        catch (JsonException ex)
        {
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(new
            {
                success = false,
                error = $"Invalid JSON: {ex.Message}"
            }));
        }
    }
}


