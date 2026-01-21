using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.Tools;

public sealed class TimeNowTool : AevatarToolBase
{
    public override string Name => "time_now";
    public override string Description => "Get current time (UTC + local).";
    public override ToolCategory Category => ToolCategory.Utility;
    public override IList<string> Tags => new List<string> { "time", "clock", "utility" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["format"] = new ToolParameter
                {
                    Type = "string",
                    Required = false,
                    Description = "DateTime format string (default: O)."
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
        var format = parameters.TryGetValue("format", out var raw) ? raw?.ToString() : null;
        if (string.IsNullOrWhiteSpace(format))
            format = "O";

        var nowUtc = DateTimeOffset.UtcNow;
        var nowLocal = DateTimeOffset.Now;

        return Task.FromResult<IMessage>(FileToolHelpers.ToStruct(new
        {
            ok = true,
            utc = nowUtc.ToString(format),
            local = nowLocal.ToString(format),
            offset_minutes = (int)nowLocal.Offset.TotalMinutes
        }));
    }
}
