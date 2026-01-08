using System.Text.Json;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Abstractions.Memory;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Aevatar.Agents.Core.Memory;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

public class MemorySearchSemanticTests
{
    [Fact]
    public async Task SearchMemory_ShouldFallbackToSemantic_ForStateHistory_WhenNoLexicalMatch()
    {
        var tool = new AevatarMemorySearchTool(NullLogger<AevatarMemorySearchTool>.Instance);

        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = "cats are lovely",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = "dogs are loyal",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "test-agent",
            GetStateCallback = () => state,
            GenerateEmbeddingsAsync = DeterministicEmbeddingsAsync
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "feline",
                ["maxResults"] = 1,
                ["memoryType"] = "all"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBe(1);

        var first = results[0];
        first.GetProperty("Content").GetString().ShouldNotBeNull().ShouldContain("cats");
        first.GetProperty("Metadata").GetProperty("ranking").GetString().ShouldBe("semantic");
    }

    [Fact]
    public async Task SearchMemory_ShouldKeepLexicalBehavior_WhenEmbeddingsNotProvided()
    {
        var tool = new AevatarMemorySearchTool(NullLogger<AevatarMemorySearchTool>.Instance);

        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = "cats are lovely",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "test-agent",
            GetStateCallback = () => state,
            GenerateEmbeddingsAsync = null
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "cats",
                ["maxResults"] = 10,
                ["memoryType"] = "all"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBeGreaterThan(0);

        // State lexical search keeps the original source marker.
        results[0].GetProperty("Metadata").GetProperty("source").GetString().ShouldBe("state.history");
    }

    [Fact]
    public async Task SearchMemory_ShouldFallbackToSemantic_ForCqrsDoc_WhenNoLexicalMatch()
    {
        var cqrs = new FakeStateQueryService();
        var tool = new AevatarMemorySearchTool(NullLogger<AevatarMemorySearchTool>.Instance, cqrs);

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "test-agent",
            GetStateCallback = () => new AevatarAIAgentState(),
            GenerateEmbeddingsAsync = DeterministicEmbeddingsAsync
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "feline",
                ["maxResults"] = 1,
                ["memoryType"] = "all"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBe(1);

        var first = results[0];
        first.GetProperty("Content").GetString().ShouldNotBeNull().ShouldContain("cats");
        first.GetProperty("Metadata").GetProperty("ranking").GetString().ShouldBe("semantic");
        first.GetProperty("Metadata").GetProperty("source").GetString().ShouldStartWith("cqrs.semantic");
    }

    [Fact]
    public async Task SearchMemory_ShouldUseVectorIndex_WhenAvailable()
    {
        var vectorIndex = new FakeMemoryVectorIndex();
        var tool = new AevatarMemorySearchTool(
            NullLogger<AevatarMemorySearchTool>.Instance,
            stateQueryService: null,
            memoryStore: null,
            memoryVectorIndex: vectorIndex);

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "test-agent",
            GetStateCallback = () => new AevatarAIAgentState(),
            GenerateEmbeddingsAsync = DeterministicEmbeddingsAsync
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "feline",
                ["maxResults"] = 1,
                ["memoryType"] = "working"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBe(1);

        var first = results[0];
        first.GetProperty("Metadata").GetProperty("source").GetString().ShouldBe("memory.vector_index");
        first.GetProperty("Metadata").GetProperty("ranking").GetString().ShouldBe("vector");
        vectorIndex.LastMemoryId.ShouldBe("privateagent::agent-1");
    }

    [Fact]
    public async Task SearchMemory_ShouldPassExplicitMemoryId_ToVectorIndex()
    {
        var vectorIndex = new FakeMemoryVectorIndex();
        var tool = new AevatarMemorySearchTool(
            NullLogger<AevatarMemorySearchTool>.Instance,
            stateQueryService: null,
            memoryStore: null,
            memoryVectorIndex: vectorIndex);

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "test-agent",
            GetStateCallback = () => new AevatarAIAgentState(),
            GenerateEmbeddingsAsync = DeterministicEmbeddingsAsync
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "feline",
                ["maxResults"] = 1,
                ["memoryType"] = "working",
                ["memoryId"] = "  session::s1  "
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBe(1);
        vectorIndex.LastMemoryId.ShouldBe("session::s1");
    }

    [Fact]
    public async Task SearchMemory_ShouldFallbackToMemoryStoreLexical_WhenVectorIndexReturnsEmpty()
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
        var tool = new AevatarMemorySearchTool(
            NullLogger<AevatarMemorySearchTool>.Instance,
            stateQueryService: null,
            memoryStore: store,
            memoryVectorIndex: vectorIndex);

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "test-agent",
            GetStateCallback = () => new AevatarAIAgentState(),
            GenerateEmbeddingsAsync = DeterministicEmbeddingsAsync
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "cats",
                ["maxResults"] = 1,
                ["memoryType"] = "working"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBe(1);

        var first = results[0];
        first.GetProperty("Metadata").GetProperty("source").GetString().ShouldBe("memory.store");
        first.GetProperty("Metadata").GetProperty("ranking").GetString().ShouldBe("lexical");
    }

    [Fact]
    public async Task SearchMemory_ShouldFallbackToMemoryStoreLexical_WhenNoEmbeddings()
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

        var tool = new AevatarMemorySearchTool(
            NullLogger<AevatarMemorySearchTool>.Instance,
            stateQueryService: null,
            memoryStore: store,
            memoryVectorIndex: null);

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "test-agent",
            GetStateCallback = () => new AevatarAIAgentState(),
            GenerateEmbeddingsAsync = null
        };

        var result = await tool.ExecuteAsync(
            new Dictionary<string, object>
            {
                ["query"] = "cats",
                ["maxResults"] = 1,
                ["memoryType"] = "working"
            },
            ctx,
            NullLogger.Instance,
            CancellationToken.None);

        var json = JsonFormatter.Default.Format(result);
        using var doc = JsonDocument.Parse(json);

        var results = doc.RootElement.GetProperty("results");
        results.GetArrayLength().ShouldBe(1);

        var first = results[0];
        first.GetProperty("Metadata").GetProperty("source").GetString().ShouldBe("memory.store");
        first.GetProperty("Metadata").GetProperty("ranking").GetString().ShouldBe("lexical");
        first.GetProperty("Content").GetString().ShouldNotBeNull().ShouldContain("cats");
    }

    private static Task<IReadOnlyList<Embedding<float>>> DeterministicEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var list = new List<Embedding<float>>(inputs.Count);
        foreach (var s in inputs)
        {
            list.Add(Embed(s));
        }

        return Task.FromResult<IReadOnlyList<Embedding<float>>>(list);
    }

    private static Embedding<float> Embed(string? text)
    {
        var t = (text ?? string.Empty).ToLowerInvariant();

        // Minimal deterministic embedding:
        // - "cat/cats/feline" -> [1,0]
        // - "dog/dogs/canine" -> [0,1]
        // - otherwise -> [0,0]
        float[] vec;
        if (t.Contains("cat") || t.Contains("cats") || t.Contains("feline"))
        {
            vec = [1f, 0f];
        }
        else if (t.Contains("dog") || t.Contains("dogs") || t.Contains("canine"))
        {
            vec = [0f, 1f];
        }
        else
        {
            vec = [0f, 0f];
        }

        return new Embedding<float>(vec);
    }

    private sealed class FakeStateQueryService : IStateQueryService
    {
        public Task<StateQueryResult?> GetByIdAsync(string agentType, string agentId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            return Task.FromResult<StateQueryResult?>(new StateQueryResult
            {
                AgentId = agentId,
                AgentType = agentType,
                Data = new Dictionary<string, object?>
                {
                    ["historyText"] = "cats are lovely"
                },
                Version = 1,
                IndexedAt = DateTime.UtcNow
            });
        }

        public Task<PagedStateQueryResult> QueryAsync(StateQuery query, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new PagedStateQueryResult
            {
                TotalCount = 0,
                Items = new List<StateQueryResult>(),
                PageIndex = query.PageIndex,
                PageSize = query.PageSize
            });
        }

        public Task<long> CountAsync(string agentType, string? queryString = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(0L);
        }
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


