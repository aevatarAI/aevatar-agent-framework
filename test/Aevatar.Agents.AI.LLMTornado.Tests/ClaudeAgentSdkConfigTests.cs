using System.Text.Json;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.LLMTornado.ClaudeAgentSdk;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.AI.LLMTornado.Tests;

public class ClaudeAgentSdkConfigTests
{
    [Fact]
    public void From_ShouldThrow_WhenRunnerCommandMissing()
    {
        var cfg = new LLMProviderConfig
        {
            Name = "claude",
            ProviderType = "claude_agent_sdk",
            ProviderSpecificSettings = new Dictionary<string, object>
            {
                ["runnerArgs"] = new[] { "/abs/runner.mjs" }
            }
        };

        Should.Throw<InvalidOperationException>(() => ClaudeAgentSdkProviderConfig.From(cfg))
            .Message.ShouldContain("runnerCommand");
    }

    [Fact]
    public void From_ShouldParse_JsonElementArraysAndObjects()
    {
        using var argsDoc = JsonDocument.Parse("[\"/abs/runner.mjs\",\"--flag\"]");
        using var pluginsDoc = JsonDocument.Parse("[\"/abs/plugin-a\",\"/abs/plugin-b\"]");
        using var envDoc = JsonDocument.Parse("{\"FOO\":\"bar\"}");
        using var timeoutDoc = JsonDocument.Parse("12345");

        var cfg = new LLMProviderConfig
        {
            Name = "claude",
            ProviderType = "claude_agent_sdk",
            TimeoutMilliseconds = 600_000,
            ProviderSpecificSettings = new Dictionary<string, object>
            {
                ["runnerCommand"] = "node",
                ["runnerArgs"] = argsDoc.RootElement.Clone(),
                ["plugins"] = pluginsDoc.RootElement.Clone(),
                ["env"] = envDoc.RootElement.Clone(),
                ["timeoutMs"] = timeoutDoc.RootElement.Clone(),
                ["maxOutputChars"] = 9999
            }
        };

        var parsed = ClaudeAgentSdkProviderConfig.From(cfg);
        parsed.RunnerCommand.ShouldBe("node");
        parsed.RunnerArgs.Count.ShouldBe(2);
        parsed.Plugins.Count.ShouldBe(2);
        parsed.ExtraEnvironment["FOO"].ShouldBe("bar");
        parsed.TimeoutMs.ShouldBe(12345);
        parsed.MaxOutputChars.ShouldBe(9999);
    }
}


