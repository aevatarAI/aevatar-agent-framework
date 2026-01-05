using Aevatar.Agents.AI.MEAI.Internal;
using System.Text.Json.Nodes;
using Xunit;

namespace Aevatar.Agents.AI.Tests;

public class DeepSeekThinkingModeFixHandlerTests
{
    [Fact]
    public void TryPatchRequestJson_Should_Add_ReasoningContent_For_Assistant_Messages()
    {
        // Arrange
        var json = """
                   {
                     "model": "deepseek-reasoner",
                     "messages": [
                       { "role": "system", "content": "sys" },
                       { "role": "user", "content": "u1" },
                       { "role": "assistant", "content": "a1" },
                       { "role": "user", "content": "u2" }
                     ]
                   }
                   """;

        // Act
        var patched = DeepSeekThinkingModeFixHandler.TryPatchRequestJson(json);

        // Assert
        Assert.NotNull(patched);
        var root = JsonNode.Parse(patched!)!.AsObject();
        var messages = root["messages"]!.AsArray();

        var assistant = messages[2]!.AsObject();
        Assert.True(assistant.ContainsKey("reasoning_content"));
        Assert.Equal(string.Empty, assistant["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void TryPatchRequestJson_Should_Return_Null_When_No_Assistant_Messages_Need_Patching()
    {
        // Arrange
        var json = """
                   {
                     "model": "deepseek-reasoner",
                     "messages": [
                       { "role": "user", "content": "u1" },
                       { "role": "assistant", "content": "a1", "reasoning_content": "" }
                     ]
                   }
                   """;

        // Act
        var patched = DeepSeekThinkingModeFixHandler.TryPatchRequestJson(json);

        // Assert
        Assert.Null(patched);
    }
}


