using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.AI.Tool.Messages;
using FluentAssertions;

namespace Aevatar.Agents.AI.Core.Tests.ToolEvolution;

public class ToolMetricsStoreTests
{
    [Fact]
    public void Record_ShouldCreateSnapshot_WhenThresholdReached()
    {
        var store = new ToolMetricsStore();
        var feedback = new ToolExecutionFeedback
        {
            ToolName = "calc",
            ToolVersion = "1.0.0",
            Success = true,
            DurationMs = 10
        };

        var snapshot = store.Record(feedback, snapshotEveryNCalls: 1);

        snapshot.Should().NotBeNull();
        snapshot!.Entries.Should().Contain(e =>
            e.ToolName == "calc" &&
            e.ToolVersion == "1.0.0" &&
            e.TotalCalls == 1);
    }
}
