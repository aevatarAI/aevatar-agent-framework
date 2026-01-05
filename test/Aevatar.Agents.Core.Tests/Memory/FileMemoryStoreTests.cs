using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Core.Memory;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Memory;

public class FileMemoryStoreTests
{
    [Fact]
    public async Task Append_List_Search_ShouldWork()
    {
        var root = Path.Combine(Path.GetTempPath(), "aevatar-memory-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new FileMemoryStore(root);

            var entry = new MemoryEntry
            {
                EntryId = "e1",
                MemoryId = "privateagent::agent-1",
                Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" },
                Role = "user",
                Content = "cats are lovely",
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            await store.AppendAsync(entry);

            var resources = await store.ListResourcesAsync(limit: 10);
            resources.Count.ShouldBe(1);
            resources[0].MemoryId.ShouldBe("privateagent::agent-1");
            resources[0].EntryCount.ShouldBe(1);

            var entries = await store.ListEntriesAsync("privateagent::agent-1", limit: 10);
            entries.Count.ShouldBe(1);
            entries[0].Content.ShouldContain("cats");

            var results = await store.SearchAsync("cats", limit: 10, memoryId: "privateagent::agent-1");
            results.Count.ShouldBe(1);
            results[0].EntryId.ShouldBe("e1");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}


