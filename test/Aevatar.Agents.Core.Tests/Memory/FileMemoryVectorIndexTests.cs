using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.Core.Memory;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.Core.Tests.Memory;

public class FileMemoryVectorIndexTests
{
    [Fact]
    public async Task Upsert_ThenSearch_ShouldReturnTopMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "aevatar-memory-vector-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var index = new FileMemoryVectorIndex(root);

            await index.UpsertAsync(new MemoryVectorRecord
            {
                EntryId = "e1",
                MemoryId = "privateagent::agent-1",
                Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" },
                Role = "user",
                Content = "cats are lovely",
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                Embedding = { 1f, 0f }
            });

            await index.UpsertAsync(new MemoryVectorRecord
            {
                EntryId = "e2",
                MemoryId = "privateagent::agent-1",
                Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" },
                Role = "user",
                Content = "dogs are loyal",
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                Embedding = { 0f, 1f }
            });

            var matches = await index.SearchAsync(
                queryEmbedding: new[] { 1f, 0f },
                limit: 5,
                memoryId: "privateagent::agent-1");

            matches.Count.ShouldBeGreaterThan(0);
            matches[0].Record.EntryId.ShouldBe("e1");
            matches[0].Similarity.ShouldBeGreaterThan(0.9);
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


