using System.Text;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class Base64DecodeTool : AevatarToolBase
{
    public override string Name => "base64_decode";
    public override string Description => "Decode base64 to text (UTF-8).";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "base64", "text", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["base64"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "Base64 string."
                }
            },
            Required = new[] { "base64" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "base64", out var base64))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "base64_required" }));

        try
        {
            var bytes = Convert.FromBase64String(base64);
            var text = Encoding.UTF8.GetString(bytes);
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = true,
                text,
                bytes = bytes.Length
            }));
        }
        catch (FormatException)
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "invalid_base64" }));
        }
    }
}
