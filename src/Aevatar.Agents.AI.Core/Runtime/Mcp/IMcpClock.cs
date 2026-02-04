namespace Aevatar.Agents.AI.Core;

internal interface IMcpClock
{
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemMcpClock : IMcpClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

