using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class JsonFormatTool : AevatarToolBase
{
    public override string Name => "json_format";
    public override string Description => "Format JSON (pretty or minified).";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "json", "format", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["json"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "JSON string."
                },
                ["minify"] = new ToolParameter
                {
                    Type = "boolean",
                    Required = false,
                    Description = "Minify output (default: false)."
                }
            },
            Required = new[] { "json" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "json", out var raw))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "json_required" }));

        var minify = parameters.TryGetValue("minify", out var m) && FileToolHelpers.TryGetBool(m);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = !minify
            });
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = true,
                json = formatted
            }));
        }
        catch (JsonException ex)
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = false,
                error = "invalid_json",
                message = ex.Message
            }));
        }
    }
}
