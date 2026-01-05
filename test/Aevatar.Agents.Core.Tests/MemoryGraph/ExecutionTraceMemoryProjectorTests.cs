using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Core.Memory;
using Aevatar.Agents.Core.MemoryGraphs;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.MemoryGraphs;

public class ExecutionTraceMemoryProjectorTests
{
    [Fact]
    public async Task ProjectAsync_ShouldWriteGraphAndMemoryEntries_ForExecutionTrace()
    {
        var trace = new ExecutionTrace
        {
            ExecutionId = "exec-1",
            Kind = ExecutionTraceKind.Maker,
            Status = ExecutionTraceStatus.Succeeded,
            Name = "demo",
            Description = "demo trace",
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            Root = new ExecutionTraceNode
            {
                NodeId = "n0",
                Name = "root",
                Type = "workflow",
                Status = ExecutionTraceStatus.Succeeded,
                Output = "root output",
                StartedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            }
        };

        trace.Root.Children.Add(new ExecutionTraceNode
        {
            NodeId = "n1",
            Name = "child",
            Type = "step",
            Status = ExecutionTraceStatus.Succeeded,
            Output = "child output",
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        trace.Root.Decisions.Add(new ExecutionTraceDecisionSession
        {
            DecisionId = "d1",
            Type = "select",
            Rounds = 1,
            WinnerCandidateId = "c1",
            Candidates =
            {
                new ExecutionTraceCandidate { CandidateId = "c1", Content = "option A", Score = 0.9 },
                new ExecutionTraceCandidate { CandidateId = "c2", Content = "option B", Score = 0.1 }
            }
        });

        var memoryStore = new InMemoryMemoryStore();
        var graphStore = new CapturingGraphStore();
        var projector = new ExecutionTraceMemoryProjector(NullLogger.Instance);

        await projector.ProjectAsync(trace, memoryStore, graphStore, CancellationToken.None);

        graphStore.SavedGraph.ShouldNotBeNull();
        graphStore.SavedGraph!.GraphId.ShouldBe("exec-1");
        graphStore.SavedGraph.Nodes.Count.ShouldBeGreaterThan(0);
        graphStore.SavedGraph.Edges.Count.ShouldBeGreaterThan(0);

        // Ensure execution-scoped memory entries were appended.
        var memoryId = "execution::exec-1";
        var entries = await memoryStore.ListEntriesAsync(memoryId, limit: 200);
        entries.Count.ShouldBeGreaterThan(0);
        entries.Any(e => e.Role == "trace_node").ShouldBeTrue();
        entries.Any(e => (e.Content ?? string.Empty).Contains("root output", StringComparison.OrdinalIgnoreCase)).ShouldBeTrue();
    }

    private sealed class CapturingGraphStore : IMemoryGraphStore
    {
        public MemoryGraph? SavedGraph { get; private set; }

        public Task SaveAsync(MemoryGraph graph, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SavedGraph = graph.Clone();
            return Task.CompletedTask;
        }

        public Task<MemoryGraph?> LoadAsync(string graphId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<MemoryGraph?>(SavedGraph?.Clone());
        }

        public Task<bool> ExistsAsync(string graphId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(SavedGraph != null && SavedGraph.GraphId == graphId);
        }

        public Task DeleteAsync(string graphId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (SavedGraph?.GraphId == graphId)
                SavedGraph = null;
            return Task.CompletedTask;
        }
    }
}


