using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Evolution;
using Aevatar.Agents.AI.Tool.Messages;
using Aevatar.Agents.AI.Tool.Tools;
using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.Core.Tests.ToolEvolution;

public class ToolEvolutionRegistryTests
{
    [Fact]
    public async Task RegisterEvolvedToolAsync_ShouldReplaceActive_WhenPolicyReplace()
    {
        var manager = new AevatarToolManager(NullLogger<AevatarToolManager>.Instance);
        var store = new ToolMetricsStore();
        var options = new ToolEvolutionOptions { Enabled = true };
        var registry = new ToolEvolutionRegistry(manager, store, options, NullLogger.Instance);
        manager.EvolutionRegistry = registry;

        var baseTool = BuildTool("calc", "1.0.0");
        await manager.RegisterToolAsync(baseTool);

        var evolved = BuildTool("calc", "2.0.0");
        await registry.RegisterEvolvedToolAsync(
            evolved,
            new ToolEvolutionPolicy { Strategy = ToolEvolutionStrategy.Replace },
            CancellationToken.None);

        var tools = await manager.GetAvailableToolsAsync();
        tools.Should().ContainSingle(t => t.Name == "calc" && t.Version == "2.0.0");
    }

    [Fact]
    public async Task ResolveToolForExecution_ShouldReturnCanary_WhenPercentageFull()
    {
        var manager = new AevatarToolManager(NullLogger<AevatarToolManager>.Instance);
        var store = new ToolMetricsStore();
        var options = new ToolEvolutionOptions { Enabled = true };
        var registry = new ToolEvolutionRegistry(manager, store, options, NullLogger.Instance);
        manager.EvolutionRegistry = registry;

        var baseTool = BuildTool("echo", "1.0.0");
        await manager.RegisterToolAsync(baseTool);

        var canary = BuildTool("echo", "1.1.0");
        await registry.RegisterEvolvedToolAsync(
            canary,
            new ToolEvolutionPolicy
            {
                Strategy = ToolEvolutionStrategy.Canary,
                CanaryPercentage = 1.0
            },
            CancellationToken.None);

        var resolved = registry.ResolveToolForExecution("echo");
        resolved.Should().NotBeNull();
        resolved!.Version.Should().Be("1.1.0");
    }

    private static ToolDefinition BuildTool(string name, string version)
    {
        return new ToolDefinition
        {
            Name = name,
            Description = "test",
            Version = version,
            Parameters = new ToolParameters(),
            ExecuteAsync = (_, _, _) => Task.FromResult<IMessage>(new StringValue { Value = "ok" })
        };
    }
}
