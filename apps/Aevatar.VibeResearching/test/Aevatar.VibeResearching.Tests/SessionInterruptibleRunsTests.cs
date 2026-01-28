using Aevatar.Agents.Core.Runtime;
using Aevatar.VibeResearching.Sessions.Services;
using Shouldly;

namespace VibeResearching.Tests;

public class SessionInterruptibleRunsTests
{
    [Fact]
    public void BeginNewRun_ShouldCancelPreviousRun_AndMarkSuperseded()
    {
        var session = new ResearchSession("s-test");

        var run1 = session.BeginNewRun("s-test:1", reason: "first", out var interrupted1);
        interrupted1.ShouldBeNull();
        run1.IsCancellationRequested.ShouldBeFalse();

        var run2 = session.BeginNewRun("s-test:2", reason: "second", out var interrupted2);
        interrupted2.ShouldBe("s-test:1");

        run1.IsCancellationRequested.ShouldBeTrue();
        run1.SupersededByRunId.ShouldBe("s-test:2");
        run1.Reason.ShouldBe("second");

        run2.IsCancellationRequested.ShouldBeFalse();
        run2.RunId.ShouldBe("s-test:2");

        // Cleanup
        run1.Dispose();
        run2.Dispose();
    }
}


