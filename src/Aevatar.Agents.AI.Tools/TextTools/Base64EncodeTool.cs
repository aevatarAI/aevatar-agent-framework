using System.Text;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class Base64EncodeTool : AevatarToolBase
{
    public override string Name => "base64_encode";
    public override string Description => "Encode text to base64.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "base64", "text", "utility" };

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
                    Description = "Text to encode."
                }
            },
            Required = new[] { "text" }
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

        var bytes = Encoding.UTF8.GetBytes(text);
        var encoded = Convert.ToBase64String(bytes);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            base64 = encoded
        }));
    }
}
