using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class UuidTool : AevatarToolBase
{
    public override string Name => "uuid";
    public override string Description => "Generate UUIDs.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "uuid", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["count"] = new ToolParameter
                {
                    Type = "integer",
                    Required = false,
                    Description = "How many UUIDs to generate (default: 1).",
                    DefaultValue = 1,
                    Minimum = 1,
                    Maximum = 50
                },
                ["format"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "Guid format (D/N/B/P).",
                    DefaultValue = "D"
                }
            }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        var count = FileToolHelpers.ClampInt(parameters.GetValueOrDefault("count"), 1, 1, 50);
        var format = parameters.TryGetValue("format", out var raw) ? raw?.ToString() : "D";
        if (string.IsNullOrWhiteSpace(format))
            format = "D";

        var list = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(Guid.NewGuid().ToString(format));
        }

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            uuid = list.Count == 1 ? list[0] : null,
            uuids = list
        }));
    }
}
