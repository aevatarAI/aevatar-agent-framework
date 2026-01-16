using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Core.AgenticRag;
using Aevatar.Agents.AI.Core.Messages;
using Aevatar.Agents.Core.Memory;
using Google.Protobuf.WellKnownTypes;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

public class MemoryStoreAgenticRagRetrieverTests
{
    [Fact]
    public async Task RetrieveAsync_ShouldUseVectorIndex_WhenQueryEmbeddingProvided()
    {
        var vectorIndex = new FakeMemoryVectorIndex();
        var retriever = new MemoryStoreAgenticRagRetriever(memoryStore: null, memoryVectorIndex: vectorIndex);

        var scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" };

        var results = await retriever.RetrieveAsync(new RagRetrieveRequest
        {
            RequestId = "r1",
            Query = "feline",
            MaxResults = 1,
            MaxSnippetChars = 10,
            Scope = scope,
            QueryEmbedding = new float[] { 0.1f, 0.2f, 0.3f }
        }, CancellationToken.None);

        results.Count.ShouldBe(1);
        vectorIndex.LastMemoryId.ShouldBe("privateagent::agent-1");

        var first = results[0];
        first.Tags["source"].ShouldBe("memory.vector_index");
        first.Tags["ranking"].ShouldBe("vector");
        first.Score.ShouldBe(0.99);

        first.Snippet.Length.ShouldBeLessThanOrEqualTo(13); // 10 + "..."
        first.Citation.RefCase.ShouldBe(RagCitation.RefOneofCase.MemoryEntry);
        first.Citation.MemoryEntry.MemoryId.ShouldBe("privateagent::agent-1");
        first.Citation.MemoryEntry.EntryId.ShouldBe("e1");
        first.Citation.MemoryEntry.Scope.Type.ShouldBe(MemoryScopeType.PrivateAgent);
    }

    [Fact]
    public async Task RetrieveAsync_ShouldFallbackToMemoryStore_WhenVectorIndexReturnsEmpty()
    {
        var store = new InMemoryMemoryStore();
        await store.AppendAsync(new MemoryEntry
        {
            EntryId = "e1",
            MemoryId = "privateagent::agent-1",
            Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" },
            Role = "user",
            Content = "cats are lovely",
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        var vectorIndex = new FakeEmptyMemoryVectorIndex();
        var retriever = new MemoryStoreAgenticRagRetriever(store, vectorIndex);

        var results = await retriever.RetrieveAsync(new RagRetrieveRequest
        {
            RequestId = "r2",
            Query = "cats",
            MaxResults = 1,
            MaxSnippetChars = 400,
            Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" },
            QueryEmbedding = new float[] { 0.1f, 0.2f, 0.3f }
        }, CancellationToken.None);

        results.Count.ShouldBe(1);
        vectorIndex.LastMemoryId.ShouldBe("privateagent::agent-1");

        var first = results[0];
        first.Tags["source"].ShouldBe("memory.store");
        first.Tags["ranking"].ShouldBe("lexical");
        first.Score.ShouldBe(0);
        first.Citation.MemoryEntry.EntryId.ShouldBe("e1");
    }

    [Fact]
    public async Task RetrieveAsync_ShouldBoundSnippets()
    {
        var store = new InMemoryMemoryStore();
        await store.AppendAsync(new MemoryEntry
        {
            EntryId = "e1",
            MemoryId = "privateagent::agent-1",
            Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" },
            Role = "user",
            Content = new string('x', 200) + " cats " + new string('y', 200),
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        var retriever = new MemoryStoreAgenticRagRetriever(store, memoryVectorIndex: null);

        const int maxChars = 32;
        var results = await retriever.RetrieveAsync(new RagRetrieveRequest
        {
            RequestId = "r3",
            Query = "cats",
            MaxResults = 1,
            MaxSnippetChars = maxChars,
            Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" }
        }, CancellationToken.None);

        results.Count.ShouldBe(1);
        var snippet = results[0].Snippet ?? string.Empty;

        // BuildSnippet may add leading and/or trailing "..." depending on query position.
        snippet.Length.ShouldBeLessThanOrEqualTo(maxChars + 6);
    }

    private sealed class FakeMemoryVectorIndex : IMemoryVectorIndex
    {
        public string? LastMemoryId { get; private set; }

        public Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
            IReadOnlyList<float> queryEmbedding,
            int limit = 20,
            string? memoryId = null,
            MemoryScopeType? scopeTypeFilter = null,
            string? scopeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastMemoryId = memoryId;

            var record = new MemoryVectorRecord
            {
                EntryId = "e1",
                MemoryId = memoryId ?? string.Empty,
                Scope = new MemoryScope { Type = MemoryScopeType.PrivateAgent, ScopeId = "agent-1" },
                Role = "user",
                Content = "cats are lovely"
            };

            var match = new MemoryVectorMatch { Record = record, Similarity = 0.99 };
            return Task.FromResult<IReadOnlyList<MemoryVectorMatch>>([match]);
        }
    }

    private sealed class FakeEmptyMemoryVectorIndex : IMemoryVectorIndex
    {
        public string? LastMemoryId { get; private set; }

        public Task UpsertAsync(MemoryVectorRecord record, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemoryVectorMatch>> SearchAsync(
            IReadOnlyList<float> queryEmbedding,
            int limit = 20,
            string? memoryId = null,
            MemoryScopeType? scopeTypeFilter = null,
            string? scopeId = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastMemoryId = memoryId;
            return Task.FromResult<IReadOnlyList<MemoryVectorMatch>>([]);
        }
    }
}


