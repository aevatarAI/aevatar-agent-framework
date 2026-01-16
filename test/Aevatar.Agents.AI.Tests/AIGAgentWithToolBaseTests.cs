using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Tool.Abstractions;
using Google.Protobuf;
using Moq;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

public class AIGAgentWithToolBaseTests
{
    private readonly Mock<IAevatarLLMProvider> _mockLLMProvider;
    private readonly Mock<IAevatarToolManager> _mockToolManager;
    private readonly Mock<IEventPublisher> _mockEventPublisher;
    private readonly TestAgent _agent;

    public AIGAgentWithToolBaseTests()
    {
        _mockLLMProvider = new Mock<IAevatarLLMProvider>();
        _mockToolManager = new Mock<IAevatarToolManager>();
        _mockEventPublisher = new Mock<IEventPublisher>();

        // Tool caches require these async methods to return a Task (not null).
        _mockToolManager
            .Setup(m => m.GenerateFunctionDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AevatarFunctionDefinition>());

        _agent = new TestAgent(_mockLLMProvider.Object, _mockToolManager.Object);
        _agent.SetEventPublisher(_mockEventPublisher.Object);
        
        // Setup default event publisher behavior
        _mockEventPublisher
            .Setup(p => p.PublishEventAsync(It.IsAny<IMessage>(), It.IsAny<EventDirection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-event-id");
    }

    [Fact]
    public async Task ChatAsync_ShouldAddMessagesToHistory()
    {
        // Arrange
        var request = new ChatRequest { Message = "Hello", RequestId = "req1" };
        
        _mockLLMProvider
            .Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AevatarLLMResponse { Content = "Hello, user!" });

        _mockToolManager
            .Setup(m => m.GetAvailableToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ToolDefinition>());

        _agent.EnableChatHistoryInState = true;
        await _agent.InitializeAsync("test-provider");

        // Act
        var response = await _agent.ChatAsync(request);

        // Assert
        response.Content.ShouldBe("Hello, user!");
        
        // Verify History
        _agent.PublicState.History.Count.ShouldBe(2);
        _agent.PublicState.History[0].Role.ShouldBe(AevatarChatRole.User);
        _agent.PublicState.History[0].Content.ShouldBe("Hello");
        _agent.PublicState.History[1].Role.ShouldBe(AevatarChatRole.Assistant);
        _agent.PublicState.History[1].Content.ShouldBe("Hello, user!");
    }

    [Fact]
    public async Task ChatAsync_WithToolCall_ShouldExecuteToolAndAddToHistory()
    {
        // Arrange
        var request = new ChatRequest { Message = "What time is it?", RequestId = "req2" };
        
        // First response: Tool Call
        var toolCallResponse = new AevatarLLMResponse 
        { 
            Content = null,
            AevatarFunctionCall = new AevatarFunctionCall 
            { 
                Name = "GetTime", 
                Arguments = "{}" 
            } 
        };

        // Second response: Final Answer
        var finalResponse = new AevatarLLMResponse { Content = "It is 12:00" };

        _mockLLMProvider
            .SetupSequence(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolCallResponse)
            .ReturnsAsync(finalResponse);

        _mockToolManager
            .Setup(m => m.ExecuteToolAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<ToolExecutionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult { IsSuccess = true, Content = "12:00" });

        _mockToolManager
            .Setup(m => m.GetAvailableToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ToolDefinition> { new() { Name = "GetTime" } });

        _agent.EnableChatHistoryInState = true;
        await _agent.InitializeAsync("test-provider");

        // Act
        var response = await _agent.ChatAsync(request);

        // Assert
        response.Content.ShouldBe("It is 12:00");
        response.ToolCalled.ShouldBeTrue();
        response.ToolCall.ToolName.ShouldBe("GetTime");
        response.ToolCall.Result.ShouldBe("12:00");

        // Verify History
        // 1. User: What time is it?
        // 2. Assistant: Tool Call (GetTime)
        // 3. Tool: Result (12:00)
        // 4. Assistant: It is 12:00
        _agent.PublicState.History.Count.ShouldBe(4);
        
        _agent.PublicState.History[0].Role.ShouldBe(AevatarChatRole.User);
        _agent.PublicState.History[0].Content.ShouldBe("What time is it?");
        
        _agent.PublicState.History[1].Role.ShouldBe(AevatarChatRole.Assistant);
        _agent.PublicState.History[1].ToolCalls.ShouldNotBeEmpty();
        _agent.PublicState.History[1].ToolCalls[0].ToolName.ShouldBe("GetTime");
        
        _agent.PublicState.History[2].Role.ShouldBe(AevatarChatRole.Tool);
        _agent.PublicState.History[2].Content.ShouldBe("12:00");
        _agent.PublicState.History[2].ToolResult.ShouldNotBeNull();
        _agent.PublicState.History[2].ToolResult.ToolName.ShouldBe("GetTime");
        
        _agent.PublicState.History[3].Role.ShouldBe(AevatarChatRole.Assistant);
        _agent.PublicState.History[3].Content.ShouldBe("It is 12:00");
    }

    [Fact]
    public async Task ChatAsync_WithSkillsAllowlist_ShouldDenyToolsOutsideAllowlist()
    {
        // Arrange
        var request = new ChatRequest { Message = "Use skill", RequestId = "req-skills" };

        // Tool definitions (simulate skill tool + two normal tools)
        _mockToolManager
            .Setup(m => m.GenerateFunctionDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AevatarFunctionDefinition>
            {
                new() { Name = "skills_load", Description = "load skill" },
                new() { Name = "AllowedTool", Description = "allowed tool" },
                new() { Name = "DeniedTool", Description = "denied tool" }
            });

        _mockToolManager
            .Setup(m => m.GetAvailableToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ToolDefinition>
            {
                new() { Name = "skills_load" },
                new() { Name = "AllowedTool" },
                new() { Name = "DeniedTool" }
            });

        // skills_load returns allowlist -> only AllowedTool is permitted afterwards
        _mockToolManager
            .Setup(m => m.ExecuteToolAsync(
                "skills_load",
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<ToolExecutionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolExecutionResult
            {
                IsSuccess = true,
                Content = """{"success":true,"name":"demo-skill","allowedTools":["AllowedTool"]}"""
            });

        var functionNamesPerCall = new List<IReadOnlyList<string>>();
        var callIndex = 0;

        _mockLLMProvider
            .Setup(p => p.GenerateAsync(It.IsAny<AevatarLLMRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AevatarLLMRequest req, CancellationToken _) =>
            {
                functionNamesPerCall.Add(req.Functions?.Select(f => f.Name).ToList() ?? new List<string>());
                callIndex++;

                return callIndex switch
                {
                    // First response: model calls skills_load
                    1 => new AevatarLLMResponse
                    {
                        AevatarFunctionCall = new AevatarFunctionCall { Name = "skills_load", Arguments = """{"name":"demo-skill"}""" }
                    },
                    // Second response: hallucinated tool call outside allowlist (should be denied by framework)
                    2 => new AevatarLLMResponse
                    {
                        AevatarFunctionCall = new AevatarFunctionCall { Name = "DeniedTool", Arguments = "{}" }
                    },
                    // Third response: final answer
                    _ => new AevatarLLMResponse { Content = "done" }
                };
            });

        _agent.EnableChatHistoryInState = false; // irrelevant for this test
        await _agent.InitializeAsync("test-provider");

        // Act
        var response = await _agent.ChatAsync(request);

        // Assert
        response.Content.ShouldBe("done");

        // skills_load executed once; DeniedTool must NOT execute
        _mockToolManager.Verify(m => m.ExecuteToolAsync(
            "skills_load",
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<ToolExecutionContext>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockToolManager.Verify(m => m.ExecuteToolAsync(
            "DeniedTool",
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<ToolExecutionContext>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // After skills_load, the next LLM call should only see AllowedTool (hard allowlist)
        functionNamesPerCall.Count.ShouldBeGreaterThanOrEqualTo(2);
        functionNamesPerCall[0].ShouldContain("DeniedTool"); // before allowlist
        functionNamesPerCall[1].ShouldBe(new[] { "AllowedTool" });
    }

    // Test Agent Implementation
    public class TestAgent : AIGAgentBase
    {
        public AevatarAIAgentState PublicState => State;

        public void SetEventPublisher(IEventPublisher publisher)
        {
            EventPublisher = publisher;
        }

        public TestAgent(IAevatarLLMProvider llmProvider, IAevatarToolManager toolManager) 
            : base()
        {
            // Use injected mocks
            _llmProvider = llmProvider;
            _isInitialized = true;
            ToolManager = toolManager;
        }

        public override Task<string> GetDescriptionAsync() => Task.FromResult("test-agent");

        protected override Task RegisterToolsAsync(CancellationToken cancellationToken = default)
        {
            // Tests control tools via mock ToolManager.
            return Task.CompletedTask;
        }

        public override Task InitializeAsync(string providerName, Action<AevatarAIAgentConfig>? configAI = null, CancellationToken cancellationToken = default)
        {
            // Skip base init logic that requires factories
            return Task.CompletedTask;
        }
    }
}
