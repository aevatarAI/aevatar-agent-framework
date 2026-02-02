using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using VibeResearching.Api.Vibe;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Workspace;
using VibeResearching.Vibe.Pivot;
using VibeResearching.Vibe.Pivot.Models;

namespace VibeResearching.Api.Tests;

internal static class WorkflowTestHelpers
{
    internal static WorkflowCoordinatorAgent CreateAgent(Dictionary<string, object?> vars)
    {
        var agent = new WorkflowCoordinatorAgent();
        var dict = GetWorkflowVars(agent);
        foreach (var (key, value) in vars)
        {
            if (key.Length > 0)
                dict[key] = value ?? string.Empty;
        }
        return agent;
    }

    internal static void SetWorkflowVar(WorkflowCoordinatorAgent agent, string key, object? value)
    {
        var dict = GetWorkflowVars(agent);
        dict[key] = value ?? string.Empty;
    }

    internal static Dictionary<string, object> ShouldBeDictionary(object? value)
        => value.ShouldBeAssignableTo<Dictionary<string, object>>()!;

    internal static VibePivot CreateNoopPivot()
    {
        return new VibePivot
        {
            DirectionDetector = new NoopDirectionChangeDetector(),
            Orchestrator = new NoopPivotOrchestrator(),
            Queue = new NoopPivotQueue(),
            AgentCoordinator = new NoopAgentPivotCoordinator(),
            FeedbackEmitter = new PivotFeedbackEmitter(NullLogger<PivotFeedbackEmitter>.Instance),
            Options = new PivotOptions
            {
                ConfidenceThreshold = 0.9,
                ClarificationThreshold = 0.5
            }
        };
    }

    internal static DagStore CreateDagStore(WorkspaceService workspace)
    {
        var services = new ServiceCollection();
        services.AddAevatarGraphInMemory();
        services.AddKnowledgeGraph();

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IKnowledgeGraphClientFactory>();
        var secrets = new TestSecretsStore();
        return new DagStore(workspace, factory, secrets, NullLogger<DagStore>.Instance);
    }

    internal static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "aevatar-agent-framework.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }

    internal static LLMProviderConfig BuildProviderConfig(string providerName)
    {
        return new LLMProviderConfig
        {
            Name = providerName,
            ProviderType = "test",
            Model = "test-model",
            ApiKey = "test-key",
            Temperature = 0.1,
            MaxTokens = 256
        };
    }

    private static Dictionary<string, object> GetWorkflowVars(WorkflowCoordinatorAgent agent)
    {
        var field = typeof(WorkflowCoordinatorAgent)
            .GetField("_workflowVariables", BindingFlags.Instance | BindingFlags.NonPublic);
        field.ShouldNotBeNull();
        return (Dictionary<string, object>)field!.GetValue(agent)!;
    }
}
