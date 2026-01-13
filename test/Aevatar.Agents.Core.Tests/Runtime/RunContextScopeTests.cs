using Aevatar.Agents.Core.Runtime;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Runtime;

public class RunContextScopeTests
{
    [Fact]
    public void Begin_ShouldSetAndRestore_AsyncLocalValue()
    {
        RunContextScope.Value.ShouldBeNull();

        var a = new RunContext("scope", "run-a");
        var b = new RunContext("scope", "run-b");

        using (RunContextScope.Begin(a))
        {
            RunContextScope.Value.ShouldBeSameAs(a);

            using (RunContextScope.Begin(b))
            {
                RunContextScope.Value.ShouldBeSameAs(b);
            }

            RunContextScope.Value.ShouldBeSameAs(a);
        }

        RunContextScope.Value.ShouldBeNull();

        a.Dispose();
        b.Dispose();
    }
}


