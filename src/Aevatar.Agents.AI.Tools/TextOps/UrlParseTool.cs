using System.Net;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

// ============================================================
//  UrlParseTool
//
//  说明：
//  - 解析 URL 的常见字段
// ============================================================
public sealed class UrlParseTool : AevatarToolBase
{
    public override string Name => "url_parse";
    public override string Description => "Parse a URL into components.";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "url", "parse", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["url"] = new ToolParameter
                {
                    Type = "string",
                    Required = true,
                    Description = "URL string."
                }
            },
            Required = new[] { "url" }
        };
    }

    public override Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (!FileToolHelpers.TryGetString(parameters, "url", out var url))
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "url_required" }));

        if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new { ok = false, error = "invalid_url" }));
        }

        if (!uri.IsAbsoluteUri)
        {
            return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
            {
                ok = true,
                is_absolute = false,
                path = uri.OriginalString
            }));
        }

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            is_absolute = true,
            scheme = uri.Scheme,
            host = uri.Host,
            port = uri.Port,
            path = uri.AbsolutePath,
            query = uri.Query,
            fragment = uri.Fragment,
            user_info = uri.UserInfo,
            is_default_port = uri.IsDefaultPort
        }));
    }
}
