using System;
using System.Collections.Generic;
using System.IO;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Core.Configuration;
using Aevatar.Agents.AI.Core.Embeddings;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.Persistence.InMemory.Graph;
using Aevatar.Agents.Runtime.Local;
using Aevatar.Agents.Sessions;
using Aevatar.Agents.Sessions.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using VibeResearching.Api.Infrastructure;
using VibeResearching.Api.Materials;
using VibeResearching.Api.Paper;
using VibeResearching.Api.Sessions;
using VibeResearching.Api.Vibe;
using VibeResearching.Api.Vibe.Brief;
using VibeResearching.Api.Vibe.Dag;
using VibeResearching.Api.Vibe.Delivery;
using VibeResearching.Api.Vibe.Trace;
using VibeResearching.Api.Workspace;
using VibeResearching.Vibe.Pivot;

namespace VibeResearching.Api.Tests;

internal sealed class TestEnv : IDisposable
{
    public required string Root { get; init; }
    public required IHostEnvironment Host { get; init; }

    public static TestEnv Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "sra-paper-collab-tests", Guid.NewGuid().ToString("N"));
        var systemRoot = Path.Combine(root, "Aevatar.VibeResearching");
        var contentRoot = Path.Combine(systemRoot, "src", "VibeResearching.Api");
        Directory.CreateDirectory(contentRoot);

        // Facts/sources are optional; facts dir may be created later by promote.
        Directory.CreateDirectory(Path.Combine(systemRoot, "sources"));

        var host = new SimpleHostEnvironment { ContentRootPath = contentRoot };
        return new TestEnv { Root = root, Host = host };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}

internal sealed class VibeStepEnv : IDisposable
{
    public required string Root { get; init; }
    public required ServiceProvider ServiceProvider { get; init; }

    public WorkspaceService Workspace => ServiceProvider.GetRequiredService<WorkspaceService>();
    public DagStore DagStore => ServiceProvider.GetRequiredService<DagStore>();
    public MaterialsService Materials => ServiceProvider.GetRequiredService<MaterialsService>();
    public PaperService Paper => ServiceProvider.GetRequiredService<PaperService>();
    public DeliveryCenterStore Delivery => ServiceProvider.GetRequiredService<DeliveryCenterStore>();
    public TraceStore Trace => ServiceProvider.GetRequiredService<TraceStore>();
    public BriefStore Brief => ServiceProvider.GetRequiredService<BriefStore>();
    public ResearchSessionManager Sessions => ServiceProvider.GetRequiredService<ResearchSessionManager>();
    public VibeWorkflowParsing Parsing => ServiceProvider.GetRequiredService<VibeWorkflowParsing>();

    public static VibeStepEnv Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "sra-vibe-step-tests", Guid.NewGuid().ToString("N"));
        var systemRoot = Path.Combine(root, "Aevatar.VibeResearching");
        var contentRoot = Path.Combine(systemRoot, "src", "VibeResearching.Api");
        Directory.CreateDirectory(contentRoot);
        Directory.CreateDirectory(Path.Combine(systemRoot, "sources"));

        var toolsDir = Path.Combine(root, "tools");
        Directory.CreateDirectory(toolsDir);

        var workflowsDir = Path.Combine(WorkflowTestHelpers.ResolveRepoRoot(),
            "apps", "Aevatar.VibeResearching", "src", "Aevatar.VibeResearching.Api", "workflows");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IHostEnvironment>(_ => new SimpleHostEnvironment
        {
            ContentRootPath = contentRoot,
            ApplicationName = "VibeResearching.Api.Tests",
            EnvironmentName = "Test"
        });

        services.Configure<LLMProvidersConfig>(options =>
        {
            options.Default = "test-provider";
            options.Providers["test-provider"] = WorkflowTestHelpers.BuildProviderConfig("test-provider");
        });
        services.Configure<SkillPacksOptions>(_ => { });
        services.Configure<MaterialsOptions>(_ => { });
        services.Configure<PivotOptions>(_ => { });

        services.AddSingleton<ILLMProviderFactory, TestLLMProviderFactory>();
        services.AddSingleton<IAIAgentEmbeddingFactory, NullEmbeddingFactory>();
        services.AddSingleton<GlobalAgentYamlRegistry>(_ => new GlobalAgentYamlRegistry(NullLogger<GlobalAgentYamlRegistry>.Instance));
        services.AddSingleton<SkillPacksSyncProgress>();
        services.AddSingleton<SkillPacksSyncService>();

        services.AddAevatarAgentSystem(builder => builder.UseLocalRuntime());
        services.AddAevatarCognitiveSessions(options =>
        {
            options.WorkflowsDirectory = workflowsDir;
            options.LazyLoadRoles = true;
        });
        services.AddAevatarSessionRuntime(options =>
        {
            options.SystemPrompt = "test";
            options.WorkflowName = "vibe_researching";
            options.MaxSnapshotMessages = 16;
            options.StreamChunkEveryN = 1;
        });
        services.AddAevatarSessionTooling(options =>
        {
            options.DotNetToolDirectories = new List<string> { toolsDir };
            options.DotNetToolMaxFiles = 4;
        });

        services.AddAevatarGraphInMemory();
        services.AddKnowledgeGraph();
        services.AddSingleton<IAevatarUserSecretsStore, TestSecretsStore>();

        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<FileMailboxService>();
        services.AddSingleton<PaperService>();
        services.AddSingleton<SessionUiSnapshotStore>();
        services.AddSingleton<ResearchRuntime>();
        services.AddSingleton<SessionUiTraceRecorder>();
        services.AddSingleton<AgentProvidersStore>();
        services.AddSingleton<ResearchSessionManager>();
        services.AddSingleton<BriefStore>();
        services.AddSingleton<DeliveryCenterStore>();
        services.AddSingleton<TraceStore>();
        services.AddSingleton<DagStore>();
        services.AddSingleton<VibeWorkflowParsing>();
        services.AddSingleton<MaterialsService>();

        return new VibeStepEnv
        {
            Root = root,
            ServiceProvider = services.BuildServiceProvider()
        };
    }

    public void Dispose()
    {
        ServiceProvider.Dispose();
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}

internal sealed class SimpleHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "VibeResearching.Api.Tests";
    public string ContentRootPath { get; set; } = "";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
