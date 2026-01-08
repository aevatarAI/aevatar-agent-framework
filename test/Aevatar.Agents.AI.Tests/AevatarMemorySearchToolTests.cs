using System.Text.Json;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.BuiltIn;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

public class AevatarMemorySearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldSearchCqrsState_WhenStateQueryServiceProvided()
    {
        // Arrange
        var queryService = new Mock<IStateQueryService>(MockBehavior.Strict);
        queryService
            .Setup(s => s.GetByIdAsync(
                "My.Agent.FullName",
                "agent-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StateQueryResult
            {
                AgentId = "agent-1",
                AgentType = "My.Agent.FullName",
                Version = 1,
                Data = new Dictionary<string, object?>
                {
                    ["historyText"] = "I met Linus yesterday. He said: simplify."
                }
            });

        var tool = new AevatarMemorySearchTool(
            NullLogger<AevatarMemorySearchTool>.Instance,
            queryService.Object);

        var parameters = new Dictionary<string, object>
        {
            ["query"] = "Linus",
            ["maxResults"] = 10,
            ["memoryType"] = "working"
        };

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "My.Agent.FullName"
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, ctx, NullLogger.Instance, CancellationToken.None);

        // Assert
        var json = JsonFormatter.Default.Format((Struct)result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("count").GetInt32().ShouldBe(1);

        var results = root.GetProperty("results");
        results.ValueKind.ShouldBe(JsonValueKind.Array);

        var first = results.EnumerateArray().First();
        first.GetProperty("Type").GetString().ShouldBe("cqrs_state");
        var content = first.GetProperty("Content").GetString();
        content.ShouldNotBeNull();
        content.ShouldContain("Linus");

        queryService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallbackToStateSnapshot_WhenCqrsReturnsNull()
    {
        // Arrange
        var queryService = new Mock<IStateQueryService>(MockBehavior.Strict);
        queryService
            .Setup(s => s.GetByIdAsync(
                "My.Agent.FullName",
                "agent-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((StateQueryResult?)null);

        var tool = new AevatarMemorySearchTool(
            NullLogger<AevatarMemorySearchTool>.Instance,
            queryService.Object);

        var state = new AevatarAIAgentState();
        state.Context["history_summary"] = "Summary: Linus says 'no bullshit'.";

        var parameters = new Dictionary<string, object>
        {
            ["query"] = "Linus",
            ["maxResults"] = 10,
            ["memoryType"] = "conversation"
        };

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "My.Agent.FullName",
            GetStateCallback = () => state
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, ctx, NullLogger.Instance, CancellationToken.None);

        // Assert
        var json = JsonFormatter.Default.Format((Struct)result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("count").GetInt32().ShouldBeGreaterThan(0);

        var results = root.GetProperty("results").EnumerateArray().ToList();
        results.Any(r => r.GetProperty("Type").GetString() == "conversation_summary")
            .ShouldBeTrue();

        queryService.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotThrow_WhenCqrsThrows_AndShouldFallbackToStateSnapshot()
    {
        // Arrange
        var queryService = new Mock<IStateQueryService>(MockBehavior.Strict);
        queryService
            .Setup(s => s.GetByIdAsync(
                "My.Agent.FullName",
                "agent-1",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var tool = new AevatarMemorySearchTool(
            NullLogger<AevatarMemorySearchTool>.Instance,
            queryService.Object);

        var state = new AevatarAIAgentState();
        state.History.Add(new AevatarChatMessage
        {
            Role = AevatarChatRole.User,
            Content = "Hello Linus, please review my patch.",
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc))
        });

        var parameters = new Dictionary<string, object>
        {
            ["query"] = "Linus",
            ["maxResults"] = 10,
            ["memoryType"] = "conversation"
        };

        var ctx = new ToolContext
        {
            AgentId = "agent-1",
            AgentType = "My.Agent.FullName",
            GetStateCallback = () => state
        };

        // Act
        var result = await tool.ExecuteAsync(parameters, ctx, NullLogger.Instance, CancellationToken.None);

        // Assert
        var json = JsonFormatter.Default.Format((Struct)result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("count").GetInt32().ShouldBeGreaterThan(0);
        var results = root.GetProperty("results").EnumerateArray().ToList();
        results.Any(r => r.GetProperty("Type").GetString() == "conversation")
            .ShouldBeTrue();

        queryService.VerifyAll();
    }
}


