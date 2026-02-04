namespace Aevatar.Agents.AI.Core.Tests.TestKit;

internal sealed class FakeClock(DateTimeOffset? now = null) : IMcpClock
{
    public DateTimeOffset Now { get; set; } = now ?? DateTimeOffset.UtcNow;
    public DateTimeOffset UtcNow => Now;
}

