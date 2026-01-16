using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace MCPToolDemo;

/// <summary>
/// A simple local tool for demonstration.
/// </summary>
public class TimeTool : AevatarToolBase
{
    public override string Name => "get_current_time";
    public override string Description => "Get the current system time";

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters();
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters, 
        ToolContext context, 
        ILogger? logger, 
        CancellationToken cancellationToken = default)
    {
        var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        logger?.LogInformation("TimeTool executed: {Time}", time);
        return new StringValue { Value = time };
    }
}