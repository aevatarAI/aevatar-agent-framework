using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.Core.Memory;
using Aevatar.Agents.Core.MemoryGraphs;
using Aevatar.Agents.Core.Tracing;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Tracing;

public class ProjectingExecutionTraceStoreTests
{
    [Fact]
    public async Task SaveAsync_ShouldProjectGraphAndMemoryEntries_BestEffort()
    {
        var inner = new FakeExecutionTraceStore();
        var memoryStore = new InMemoryMemoryStore();
        var graphStore = new CapturingGraphStore();

        var store = new ProjectingExecutionTraceStore(inner, memoryStore, graphStore, NullLogger.Instance);

        var trace = new ExecutionTrace
        {
            ExecutionId = "exec-1",
            Kind = ExecutionTraceKind.Maker,
            Status = ExecutionTraceStatus.Succeeded,
            Name = "demo",
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

        await store.SaveAsync(trace);

        inner.SaveCalls.ShouldBe(1);
        graphStore.SavedGraph.ShouldNotBeNull();
        graphStore.SavedGraph!.GraphId.ShouldBe("exec-1");

        var entries = await memoryStore.ListEntriesAsync("execution::exec-1", limit: 200);
        entries.Count.ShouldBeGreaterThan(0);
    }

    private sealed class FakeExecutionTraceStore : IExecutionTraceStore
    {
        public int SaveCalls { get; private set; }

        public Task SaveAsync(ExecutionTrace trace, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SaveCalls++;
            return Task.CompletedTask;
        }

        public Task<ExecutionTrace?> LoadAsync(string executionId, CancellationToken ct = default)
            => Task.FromResult<ExecutionTrace?>(null);

        public Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task DeleteAsync(string executionId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ExecutionTraceBundleInfo>> ListAsync(int limit = 200, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExecutionTraceBundleInfo>>([]);
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


