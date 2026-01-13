using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

public sealed class MEAILLMProviderJsonSchemaTests
{
    [Fact]
    public async Task FunctionSchema_ArrayParameter_Should_Include_Items()
    {
        // Arrange: build a tool definition with an array parameter.
        var toolLogger = LoggerFactory
            .Create(b => b.SetMinimumLevel(LogLevel.Critical))
            .CreateLogger<AevatarToolManager>();

        var toolManager = new AevatarToolManager(toolLogger);
        var tool = new ToolDefinition
        {
            Name = "skills_run_python",
            Description = "test tool",
            Parameters = new ToolParameters
            {
                Items = new Dictionary<string, ToolParameter>
                {
                    ["args"] = new ToolParameter
                    {
                        Type = "array",
                        Description = "CLI args",
                        Required = false,
                        Items = new ToolParameter { Type = "string", Description = "arg string" }
                    }
                }
            },
            ExecuteAsync = (_, _, _) => Task.FromResult<Google.Protobuf.IMessage>(new StringValue { Value = "ok" })
        };
        await toolManager.RegisterToolAsync(tool, CancellationToken.None);

        var functions = (await toolManager.GenerateFunctionDefinitionsAsync(CancellationToken.None)).ToList();
        Assert.Single(functions);

        ChatOptions? capturedOptions = null;
        var mockChatClient = new Mock<IChatClient>();
        mockChatClient
            .Setup(x => x.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions, CancellationToken>((_, opt, _) => capturedOptions = opt)
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
            Functions = functions
        };

        // Act
        await provider.GenerateAsync(request, CancellationToken.None);

        // Assert: ensure tool schema includes items for array parameters.
        Assert.NotNull(capturedOptions);
        Assert.NotNull(capturedOptions!.Tools);
        Assert.NotEmpty(capturedOptions.Tools!);

        var funcTool = capturedOptions.Tools!.OfType<AIFunction>().First();
        var schema = funcTool.JsonSchema;

        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("args", out var argsSchema));
        Assert.True(argsSchema.TryGetProperty("type", out var argsType));
        Assert.Equal("array", argsType.GetString());

        Assert.True(argsSchema.TryGetProperty("items", out var itemsSchema));
        Assert.True(itemsSchema.TryGetProperty("type", out var itemType));
        Assert.Equal("string", itemType.GetString());
    }
}


