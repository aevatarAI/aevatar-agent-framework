using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class JsonValidateTool : AevatarToolBase
{
    public override string Name => "json_validate";
    public override string Description => "Validate JSON syntax.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "json", "validate", "utility" };

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

        try
        {
            JsonDocument.Parse(raw);
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = true, valid = true }));
        }
        catch (JsonException ex)
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = true,
                valid = false,
                error = ex.Message
            }));
        }
    }
}
