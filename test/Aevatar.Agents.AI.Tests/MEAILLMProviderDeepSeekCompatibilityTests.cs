using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

public class MEAILLMProviderDeepSeekCompatibilityTests
{
    [Fact]
    public async Task DeepSeekReasoner_Should_Include_ReasoningContent_On_Assistant_Messages_Even_When_Empty()
    {
        // Arrange
        List<ChatMessage>? capturedMessages = null;

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((msgs, _, _) => capturedMessages = msgs.ToList())
            .ReturnsAsync(new Microsoft.Extensions.AI.ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "deepseek-reasoner",
            Temperature = 0.7
        };

        var logger = new Mock<ILogger<MEAILLMProvider>>();
        var provider = new MEAILLMProvider(mockChatClient.Object, providerConfig, logger.Object);

        var request = new AevatarLLMRequest
        {
            SystemPrompt = "sys",
            UserPrompt = "user",
            Messages =
            {
                new AevatarChatMessage
                {
                    Role = AevatarChatRole.Assistant,
                    Content = "previous-assistant-message"
                    // No reasoning_content metadata captured (empty)
                }
            }
        };

        // Act
        await provider.GenerateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedMessages);

        var assistantMsg = capturedMessages!
            .First(m => m.Role == ChatRole.Assistant && m.Text == "previous-assistant-message");

        Assert.NotNull(assistantMsg.AdditionalProperties);
        Assert.True(assistantMsg.AdditionalProperties!.ContainsKey("reasoning_content"));
        Assert.Equal(string.Empty, assistantMsg.AdditionalProperties["reasoning_content"] as string);
    }

    [Fact]
    public async Task ToolRole_Should_Map_To_ChatRoleTool()
    {
        // Arrange
        List<ChatMessage>? capturedMessages = null;

        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((msgs, _, _) => capturedMessages = msgs.ToList())
            .ReturnsAsync(new Microsoft.Extensions.AI.ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var providerConfig = new LLMProviderConfig
        {
            ProviderType = "openai",
            ApiKey = "test-key",
            Model = "gpt-4",
            Temperature = 0.7
        };

        var logger = new Mock<ILogger<MEAILLMProvider>>();
        var provider = new MEAILLMProvider(mockChatClient.Object, providerConfig, logger.Object);

        var request = new AevatarLLMRequest
        {
            SystemPrompt = "sys",
            UserPrompt = "user",
            Messages =
            {
                new AevatarChatMessage
                {
                    Role = AevatarChatRole.Tool,
                    Content = "tool-output"
                }
            }
        };

        // Act
        await provider.GenerateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedMessages);
        Assert.Contains(capturedMessages!, m => m.Role == ChatRole.Tool && m.Text == "tool-output");
    }
}


