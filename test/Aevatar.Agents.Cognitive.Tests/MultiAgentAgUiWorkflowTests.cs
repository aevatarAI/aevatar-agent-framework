using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Tracing;
using Aevatar.Agents.AGUI;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Hooks;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Execution;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Runtime.Local;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Cognitive.Tests;

[Collection("EnvVarNonParallel")]
public sealed class MultiAgentAgUiWorkflowTests
{
    private const string WorkflowName = "complex_multi_agent";

    [Fact]
    public async Task ComplexWorkflow_ShouldLoadAgentYamlByRole()
    {
        var run = await RunComplexWorkflowAsync();

        run.SystemPrompts.Any(p => p.Contains("Planner agent", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue("planner system_prompt not captured");
        run.SystemPrompts.Any(p => p.Contains("Researcher agent", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue("researcher system_prompt not captured");
        run.SystemPrompts.Any(p => p.Contains("Critic agent", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue("critic system_prompt not captured");
        run.SystemPrompts.Any(p => p.Contains("Writer agent", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue("writer system_prompt not captured");
    }

    [Fact]
    public async Task ComplexWorkflow_ShouldAggregateFanOutResults()
    {
        var run = await RunComplexWorkflowAsync();

        run.WorkerCount.ShouldBe(3);
        run.WorkerIds.All(id => id.StartsWith("RoleAIGAgent:", StringComparison.Ordinal)).ShouldBeTrue();
        run.Result.Success.ShouldBeTrue(run.Result.Error ?? "workflow failed");

        var output = run.Result.Output.ShouldBeOfType<Dictionary<string, object?>>();
        var insights = output["insights"].ShouldBeOfType<List<object>>();
        insights.Count.ShouldBe(3);

        insights[0].ToString().ShouldBe("Facts: Edge AI launch baseline");
        insights[1].ToString().ShouldBe("Risks: timeline, budget");
        insights[2].ToString().ShouldBe("Plan: research, prototype, launch");
    }

    [Fact]
    public async Task ComplexWorkflow_ShouldEmitAgUiEvents()
    {
        var run = await RunComplexWorkflowAsync();

        var aguiEvents = run.TraceEvents
            .SelectMany(evt => AgUiTraceProjector.Map(evt))
            .ToList();

        aguiEvents.OfType<StepStartedEvent>().Any(e => e.StepName == "kickoff").ShouldBeTrue();
        aguiEvents.OfType<StepFinishedEvent>().Any(e => e.StepName == "gather").ShouldBeTrue();
        aguiEvents.OfType<CustomEvent>()
            .Any(e => string.Equals(e.Name, AgUiExecutionTraceMapper.WorkflowExecutionEventName, StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue("missing workflow execution events");
    }

    private static async Task<WorkflowRunData> RunComplexWorkflowAsync()
    {
        var workspaceRoot = MakeTempDir();
        var oldRoot = Environment.GetEnvironmentVariable(WorkspacePathGuard.WorkspaceRootEnvVar);

        ServiceProvider? provider = null;
        IGAgentActorManager? actorManager = null;

        try
        {
            Environment.SetEnvironmentVariable(WorkspacePathGuard.WorkspaceRootEnvVar, workspaceRoot);
            CopyAgentYamlFixtures(workspaceRoot);

            var hook = new SystemPromptCaptureHook();
            provider = BuildServiceProvider(hook);
            actorManager = provider.GetRequiredService<IGAgentActorManager>();

            var coordinatorActor = await actorManager.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>("coordinator");
            var coordinator = (CognitiveCoordinatorGAgent)coordinatorActor.GetAgent();
            coordinator.SetActorManager(actorManager);
            await coordinator.InitializeAsync("test-provider");

            var parser = new WorkflowParser();
            var workflow = parser.ParseFile(Path.Combine(TestProjectRoot(), "workflows", "complex_multi_agent.yaml"));
            coordinator.RegisterWorkflow(workflow);
            var roles = ExtractRolesFromWorkflow(workflow);
            await coordinator.CreateWorkerPoolAsync(poolSize: roles.Count);
            await ConfigureWorkerRolesAsync(coordinator, actorManager, roles, workspaceRoot);

            var collectorActor = await actorManager.CreateAndRegisterAsync<TraceCollectorAgent>("trace");
            await actorManager.LinkParentChildAsync(coordinatorActor.Id, collectorActor.Id);

            await coordinator.StartWorkflowAsync(WorkflowName);

            var result = coordinator.GetResult();
            var collector = (TraceCollectorAgent)collectorActor.GetAgent();
            var traceEvents = await WaitForTraceEventsAsync(collector, TimeSpan.FromSeconds(2));
            var workers = await actorManager.GetActorsByTypeAsync<RoleAIGAgent>();
            var workerIds = workers.Select(w => w.Id).ToList();

            return new WorkflowRunData(
                result,
                traceEvents,
                hook.SnapshotSystemPrompts(),
                workerIds);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkspacePathGuard.WorkspaceRootEnvVar, oldRoot);

            if (actorManager != null)
            {
                await actorManager.DeactivateAllAsync();
            }

            provider?.Dispose();
            TryDeleteDir(workspaceRoot);
        }
    }

    private static List<string> ExtractRolesFromWorkflow(WorkflowDefinition workflow)
    {
        var input = workflow.Inputs.FirstOrDefault(i => string.Equals(i.Name, "tasks", StringComparison.OrdinalIgnoreCase));
        var raw = input?.DefaultValue;
        var roles = new List<string>();

        if (raw is IEnumerable<object> list)
        {
            foreach (var item in list)
            {
                var role = TryGetRole(item);
                if (!string.IsNullOrWhiteSpace(role))
                    roles.Add(role!);
            }
        }

        return roles.Count > 0 ? roles : new List<string> { "researcher", "critic", "planner" };
    }

    private static string? TryGetRole(object? item)
    {
        if (item is IDictionary<string, object> dict &&
            dict.TryGetValue("role", out var value))
        {
            return value?.ToString();
        }

        if (item is System.Collections.IDictionary nd &&
            nd.Contains("role"))
        {
            return nd["role"]?.ToString();
        }

        return null;
    }

    private static async Task ConfigureWorkerRolesAsync(
        CognitiveCoordinatorGAgent coordinator,
        IGAgentActorManager actorManager,
        IReadOnlyList<string> roles,
        string workspaceRoot)
    {
        var field = typeof(CognitiveCoordinatorGAgent)
            .GetField("_workerIds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var workerIds = field?.GetValue(coordinator) as List<string> ?? new List<string>();

        var loader = new AgentYamlConfigLoader();

        for (var i = 0; i < roles.Count && i < workerIds.Count; i++)
        {
            var role = roles[i];
            var actor = await actorManager.GetActorAsync(workerIds[i]);
            if (actor?.GetAgent() is not RoleAIGAgent agent)
                continue;

            agent.InitializeRole(role);

            var key = GlobalAgentYamlRegistry.NormalizeRoleKey(role);
            var yamlPath = Path.Combine(workspaceRoot, "aevatar", "agents", $"{key}.yaml");
            var yaml = loader.TryLoadFromFile(yamlPath);
            if (yaml != null)
            {
                await AgentYamlConfigApplier.ApplyAsync(agent, yaml, role, CancellationToken.None);
            }
        }
    }

    private static ServiceProvider BuildServiceProvider(SystemPromptCaptureHook hook)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddOptions();
        services.AddSingleton<ILLMProviderFactory, TestLLMProviderFactory>();
        services.AddSingleton<IAevatarAgentHook>(hook);
        services.AddAevatarAgentSystem(builder => builder.UseLocalRuntime());

        return services.BuildServiceProvider();
    }

    private static void CopyAgentYamlFixtures(string workspaceRoot)
    {
        var sourceDir = Path.Combine(TestProjectRoot(), "agents");
        var targetDir = Path.Combine(workspaceRoot, "aevatar", "agents");

        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*.yaml"))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }
    }

    private static string TestProjectRoot()
        => Path.Combine(RepoRoot(), "test", "Aevatar.Agents.Cognitive.Tests");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Directory.Packages.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found (Directory.Packages.props missing).");
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

    private sealed record WorkflowRunData(
        WorkflowResult Result,
        IReadOnlyList<ExecutionTraceEvent> TraceEvents,
        IReadOnlyList<string> SystemPrompts,
        IReadOnlyList<string> WorkerIds)
    {
        public int WorkerCount => WorkerIds.Count;
    }

    private static async Task<IReadOnlyList<ExecutionTraceEvent>> WaitForTraceEventsAsync(
        TraceCollectorAgent collector,
        TimeSpan timeout)
    {
        var start = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - start < timeout)
        {
            var events = collector.SnapshotEvents();
            if (events.Any(e => string.Equals(e.Phase, ExecutionTraceEventPhase.SessionStop, StringComparison.OrdinalIgnoreCase)))
                return events;

            await Task.Delay(50);
        }

        return collector.SnapshotEvents();
    }

    private sealed class SystemPromptCaptureHook : IAevatarAgentHook
    {
        private readonly ConcurrentQueue<string> _prompts = new();

        public IReadOnlyList<string> SnapshotSystemPrompts()
            => _prompts.ToList();

        public Task BeforeLLMRequestAsync(AevatarAgentHookContext context, CancellationToken cancellationToken)
        {
            var prompt = context.LlmRequest?.SystemPrompt;
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                _prompts.Enqueue(prompt);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TraceCollectorAgent : GAgentBase<Empty>
    {
        private readonly ConcurrentQueue<ExecutionTraceEvent> _events = new();

        [EventHandler]
        public Task HandleExecutionTraceAsync(ExecutionTraceEvent evt)
        {
            if (evt != null)
                _events.Enqueue(evt);
            return Task.CompletedTask;
        }

        public IReadOnlyList<ExecutionTraceEvent> SnapshotEvents()
            => _events.ToList();

        public override Task<string> GetDescriptionAsync()
            => Task.FromResult("TraceCollectorAgent");
    }

    private sealed class TestLLMProviderFactory : ILLMProviderFactory
    {
        private readonly TestLLMProvider _provider = new();

        public IAevatarLLMProvider GetProvider(string providerName)
            => _provider;

        public IAevatarLLMProvider GetDefaultProvider()
            => _provider;

        public IReadOnlyList<string> GetAvailableProviderNames()
            => new[] { "test-provider" };

        public bool HasProvider(string providerName)
            => !string.IsNullOrWhiteSpace(providerName);

        public LLMProviderConfig GetProviderConfig(string providerName)
            => BuildConfig(providerName);

        public LLMProviderConfig GetDefaultProviderConfig()
            => BuildConfig("test-provider");

        public IAevatarLLMProvider CreateProvider(
            LLMProviderConfig providerConfig,
            CancellationToken cancellationToken = default)
            => _provider;

        public Task<IAevatarLLMProvider> GetProviderAsync(
            string providerName,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IAevatarLLMProvider>(_provider);

        public Task<IAevatarLLMProvider> GetDefaultProviderAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IAevatarLLMProvider>(_provider);

        private static LLMProviderConfig BuildConfig(string providerName)
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
    }

    private sealed class TestLLMProvider : IAevatarLLMProvider
    {
        public Task<AevatarLLMResponse> GenerateAsync(
            AevatarLLMRequest request,
            CancellationToken cancellationToken = default)
        {
            var content = BuildResponse(request);
            var response = new AevatarLLMResponse
            {
                Content = content,
                AevatarStopReason = AevatarStopReason.Complete,
                Usage = new AevatarTokenUsage
                {
                    PromptTokens = 12,
                    CompletionTokens = 8,
                    TotalTokens = 20
                }
            };
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<AevatarLLMToken> GenerateStreamAsync(
            AevatarLLMRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new AevatarLLMToken
            {
                Content = BuildResponse(request),
                IsComplete = true
            };
            await Task.CompletedTask;
        }

        public Task<AevatarModelInfo> GetModelInfoAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AevatarModelInfo
            {
                Name = "test-model",
                MaxTokens = 1024,
                SupportsStreaming = true,
                SupportsFunctions = false
            });
        }

        private static string BuildResponse(AevatarLLMRequest request)
        {
            var user = request.Messages.LastOrDefault(m => m.Role == AevatarChatRole.User)?.Content ?? string.Empty;
            var system = request.SystemPrompt ?? string.Empty;
            var hint = request.Context?.TryGetValue("stage_hint", out var value) == true
                ? value?.ToString() ?? string.Empty
                : string.Empty;

            if (hint.StartsWith("gather[", StringComparison.OrdinalIgnoreCase))
            {
                if (hint.Contains("[0]", StringComparison.OrdinalIgnoreCase))
                    return "Facts: Edge AI launch baseline";
                if (hint.Contains("[1]", StringComparison.OrdinalIgnoreCase))
                    return "Risks: timeline, budget";
                if (hint.Contains("[2]", StringComparison.OrdinalIgnoreCase))
                    return "Plan: research, prototype, launch";
            }
            if (string.Equals(hint, "kickoff", StringComparison.OrdinalIgnoreCase))
                return "Goal: Edge AI launch scope";
            if (string.Equals(hint, "synthesize", StringComparison.OrdinalIgnoreCase))
                return "Brief: concise launch summary";

            if (system.Contains("Researcher agent", StringComparison.OrdinalIgnoreCase))
                return "Facts: Edge AI launch baseline";
            if (system.Contains("Critic agent", StringComparison.OrdinalIgnoreCase))
                return "Risks: timeline, budget";
            if (system.Contains("Writer agent", StringComparison.OrdinalIgnoreCase))
                return "Brief: concise launch summary";
            if (system.Contains("Planner agent", StringComparison.OrdinalIgnoreCase) &&
                user.Contains("Create a 3-step plan", StringComparison.OrdinalIgnoreCase))
                return "Plan: research, prototype, launch";
            if (system.Contains("Planner agent", StringComparison.OrdinalIgnoreCase) &&
                user.Contains("Define the goal", StringComparison.OrdinalIgnoreCase))
                return "Goal: Edge AI launch scope";

            if (user.Contains("Define the goal", StringComparison.OrdinalIgnoreCase))
                return "Goal: Edge AI launch scope";
            if (user.Contains("Gather 3 facts", StringComparison.OrdinalIgnoreCase))
                return "Facts: Edge AI launch baseline";
            if (user.Contains("List 2 risks", StringComparison.OrdinalIgnoreCase))
                return "Risks: timeline, budget";
            if (user.Contains("Create a 3-step plan", StringComparison.OrdinalIgnoreCase))
                return "Plan: research, prototype, launch";
            if (user.Contains("Build a short brief", StringComparison.OrdinalIgnoreCase))
                return "Brief: concise launch summary";
            return "ok";
        }
    }
}
