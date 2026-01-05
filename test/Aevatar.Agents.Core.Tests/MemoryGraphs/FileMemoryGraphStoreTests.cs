using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Core.MemoryGraphs;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.MemoryGraphs;

public class FileMemoryGraphStoreTests
{
    [Fact]
    public async Task Save_Load_Exists_Delete_ShouldWork()
    {
        var traceRoot = Path.Combine(Path.GetTempPath(), "aevatar-trace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(traceRoot);

        try
        {
            var store = new FileMemoryGraphStore(traceRoot);

            var graph = new MemoryGraph
            {
                GraphId = "exec-1",
                Scope = new MemoryScope { Type = MemoryScopeType.Execution, ScopeId = "exec-1" },
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            graph.Nodes.Add(new MemoryGraphNode
            {
                NodeId = "exec:exec-1",
                Type = "execution",
                Name = "exec-1",
                Content = "demo"
            });

            graph.Edges.Add(new MemoryGraphEdge
            {
                EdgeId = "edge:1",
                FromNodeId = "exec:exec-1",
                ToNodeId = "exec:exec-1",
                Type = "self",
                Label = "self"
            });

            await store.SaveAsync(graph);

            (await store.ExistsAsync("exec-1")).ShouldBeTrue();

            var loaded = await store.LoadAsync("exec-1");
            loaded.ShouldNotBeNull();
            loaded!.GraphId.ShouldBe("exec-1");
            loaded.Nodes.Count.ShouldBe(1);

            await store.DeleteAsync("exec-1");
            (await store.ExistsAsync("exec-1")).ShouldBeFalse();
        }
        finally
        {
            try
            {
                Directory.Delete(traceRoot, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}


