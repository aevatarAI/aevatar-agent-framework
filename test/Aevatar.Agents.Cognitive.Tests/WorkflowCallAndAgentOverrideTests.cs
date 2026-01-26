using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Primitives;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

[Collection("EnvVarNonParallel")]
public class WorkflowCallAndAgentOverrideTests
{
    [Fact]
    public async Task WorkflowCall_ShouldResolveTemplateName()
    {
        var coordinator = new TestCoordinator();
        coordinator.RegisterWorkflow(BuildChildWorkflow());
        coordinator.RegisterWorkflow(BuildParentWorkflow());

        await coordinator.StartWorkflowAsync("parent", new Dictionary<string, object>
        {
            ["child_name"] = "child",
            ["child_result"] = "hello"
        });

        var result = coordinator.GetResult();
        result.Success.ShouldBeTrue(result.Error ?? "workflow failed");

        var output = result.Output.ShouldBeOfType<Dictionary<string, object?>>();
        output["result"]?.ToString().ShouldBe("hello");
    }

    [Fact]
    public async Task LlmCall_ShouldUseAgentYaml_SystemPromptOverride()
    {
        const string workspaceRootEnv = "AEVATAR_COGNITIVE_WORKSPACE_ROOT";
        var root = MakeTempDir();
        var oldRoot = Environment.GetEnvironmentVariable(workspaceRootEnv);

        try
        {
            Environment.SetEnvironmentVariable(workspaceRootEnv, root);
            var agentsDir = Path.Combine(root, "aevatar", "agents");
            Directory.CreateDirectory(agentsDir);
            var agentPath = Path.Combine(agentsDir, "planner.yaml");
            File.WriteAllText(agentPath, """
id: "planner"
name: "Planner"
version: "1.0"
system_prompt: |
  You are the planner agent.
""");

            var coordinator = new TestCoordinator();
            coordinator.RegisterWorkflow(BuildAgentCallWorkflow());

            await coordinator.StartWorkflowAsync("agent_call");

            coordinator.LastRequest.ShouldNotBeNull();
            coordinator.LastRequest!.Context.TryGetValue("system_prompt", out var sp).ShouldBeTrue();
            sp.ShouldBe("You are the planner agent.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(workspaceRootEnv, oldRoot);
            TryDeleteDir(root);
        }
    }

    private static WorkflowDefinition BuildChildWorkflow()
    {
        return new WorkflowDefinition
        {
            Name = "child",
            Inputs =
            [
                new InputParameter
                {
                    Name = "result",
                    Type = "string",
                    Required = false,
                    DefaultValue = "child_ok"
                }
            ],
            Steps =
            [
                new StepDefinition
                {
                    Id = "set",
                    Type = "assign",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["from"] = "result"
                    },
                    Store = "answer"
                }
            ],
            Output = new Dictionary<string, string>
            {
                ["result"] = "{{answer}}"
            }
        };
    }

    private static WorkflowDefinition BuildParentWorkflow()
    {
        return new WorkflowDefinition
        {
            Name = "parent",
            Inputs =
            [
                new InputParameter
                {
                    Name = "child_name",
                    Type = "string",
                    Required = true
                },
                new InputParameter
                {
                    Name = "child_result",
                    Type = "string",
                    Required = true
                }
            ],
            Steps =
            [
                new StepDefinition
                {
                    Id = "call_child",
                    Type = "workflow_call",
                    Workflow = "{{ child_name }}",
                    Params = new Dictionary<string, object?>
                    {
                        ["result"] = "{{ child_result }}"
                    },
                    Store = "child_output"
                },
                new StepDefinition
                {
                    Id = "unwrap",
                    Type = "assign",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["from"] = "child_output.result"
                    },
                    Store = "final"
                }
            ],
            Output = new Dictionary<string, string>
            {
                ["result"] = "{{final}}"
            }
        };
    }

    private static WorkflowDefinition BuildAgentCallWorkflow()
    {
        return new WorkflowDefinition
        {
            Name = "agent_call",
            Steps =
            [
                new StepDefinition
                {
                    Id = "call",
                    Type = "llm_call",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["prompt"] = "hello",
                        ["agent"] = "planner",
                        ["output"] = "text"
                    },
                    Store = "response"
                }
            ],
            Output = new Dictionary<string, string>
            {
                ["result"] = "{{response}}"
            }
        };
    }

    private sealed class TestCoordinator : CognitiveCoordinatorGAgent
    {
        public ChatRequest? LastRequest { get; private set; }

        public TestCoordinator()
        {
            EventPublisher = NullEventPublisher.Instance;
        }

        public override Task<bool> SupportsStreamingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public override Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChatResponse { Content = "ok" });
        }
    }

    private static string MakeTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aevatar_cognitive_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }
}
