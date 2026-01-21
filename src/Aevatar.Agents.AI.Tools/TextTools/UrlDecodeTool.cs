using System.Net;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class UrlDecodeTool : AevatarToolBase
{
    public override string Name => "url_decode";
    public override string Description => "URL-decode text.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "url", "decode", "utility" };

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
                    Description = "Text to decode."
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

        var decoded = WebUtility.UrlDecode(text);
        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            text = decoded
        }));
    }
}
