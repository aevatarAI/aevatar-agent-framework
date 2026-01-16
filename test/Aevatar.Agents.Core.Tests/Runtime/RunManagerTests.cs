using Aevatar.Agents.Core.Runtime;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Runtime;

public class RunManagerTests
{
    [Fact]
    public void StartOrReplace_ShouldCancelPreviousRun_AndKeepLatestActive()
    {
        var mgr = new RunManager();

        var r1 = mgr.StartOrReplace("s", "r1", reason: "first");
        r1.IsCancellationRequested.ShouldBeFalse();

        var r2 = mgr.StartOrReplace("s", "r2", reason: "second");

        r1.IsCancellationRequested.ShouldBeTrue();
        r1.SupersededByRunId.ShouldBe("r2");
        r1.Reason.ShouldBe("second");

        mgr.TryGetActive("s", out var active).ShouldBeTrue();
        active.ShouldNotBeNull();
        active!.RunId.ShouldBe("r2");
        active.IsCancellationRequested.ShouldBeFalse();

        // Cleanup
        mgr.TryClear("s", "r2").ShouldBeTrue();
        r2.Dispose();
    }
}


