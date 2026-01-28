using Microsoft.Extensions.Logging;
using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Knowledge.Graph;
using Aevatar.Agents.AI.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Sessions;
using Aevatar.VibeResearching.Knowledge;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Knowledge;
using Aevatar.VibeResearching.Agents.Mesh;
using Aevatar.VibeResearching.Agents.Mesh.Services;
using Aevatar.VibeResearching.Agents.Pivot;

namespace Aevatar.VibeResearching.Agents;

// ============================================================
//  Vibe modules (见名知意的依赖分组)
//
//  中文说明：
//  - Orchestrator 的"构造器恐怖"本质是：跨子域依赖混在一个平面列表里
//  - 解决方式是把依赖按子域建模成模块对象（Core/Pivot/Mesh/Host）
//  - 模块对象不是"藏屎"，而是把 wiring 放在 wiring 应该出现的地方：composition root
// ============================================================

internal sealed class VibeCore
{
    public required ResearchRuntime Runtime { get; init; }
    public required IMaterialsService Materials { get; init; }
    public required IWorkspaceService Workspace { get; init; }
    public required IBriefRepository Brief { get; init; }
    public required IDeliveryCenterRepository Delivery { get; init; }
    public required IDagStore Dag { get; init; }
    public required IDagGroundingPolicy DagGrounding { get; init; }
    public required ITraceRepository Trace { get; init; }
    public required IFileMailboxService Mailbox { get; init; }
    public required IPaperService Paper { get; init; }
    public required IAgentProvidersRepository AgentProviders { get; init; }
    public required IDagConsensusService DagConsensus { get; init; }
    public required IAevatarUserSecretsStore UserSecrets { get; init; }
    public required IKnowledgeGraphClientFactory GraphFactory { get; init; }

    public static VibeCore Create(IServiceProvider sp) => new()
    {
        Runtime = sp.GetRequiredService<ResearchRuntime>(),
        Materials = sp.GetRequiredService<IMaterialsService>(),
        Workspace = sp.GetRequiredService<IWorkspaceService>(),
        Brief = sp.GetRequiredService<IBriefRepository>(),
        Delivery = sp.GetRequiredService<IDeliveryCenterRepository>(),
        Dag = sp.GetRequiredService<IDagStore>(),
        DagGrounding = sp.GetRequiredService<IDagGroundingPolicy>(),
        Trace = sp.GetRequiredService<ITraceRepository>(),
        Mailbox = sp.GetRequiredService<IFileMailboxService>(),
        Paper = sp.GetRequiredService<IPaperService>(),
        AgentProviders = sp.GetRequiredService<IAgentProvidersRepository>(),
        DagConsensus = sp.GetRequiredService<IDagConsensusService>(),
        UserSecrets = sp.GetRequiredService<IAevatarUserSecretsStore>(),
        GraphFactory = sp.GetRequiredService<IKnowledgeGraphClientFactory>()
    };
}

internal sealed class VibePivot
{
    public required IDirectionChangeDetector DirectionDetector { get; init; }
    public required IPivotOrchestrator Orchestrator { get; init; }
    public required IPivotQueue Queue { get; init; }
    public required IAgentPivotCoordinator AgentCoordinator { get; init; }
    public required IPivotFeedbackEmitter FeedbackEmitter { get; init; }
    public required PivotOptions Options { get; init; }

    public static VibePivot Create(IServiceProvider sp) => new()
    {
        DirectionDetector = sp.GetRequiredService<IDirectionChangeDetector>(),
        Orchestrator = sp.GetRequiredService<IPivotOrchestrator>(),
        Queue = sp.GetRequiredService<IPivotQueue>(),
        AgentCoordinator = sp.GetRequiredService<IAgentPivotCoordinator>(),
        FeedbackEmitter = sp.GetRequiredService<IPivotFeedbackEmitter>(),
        Options = sp.GetRequiredService<IOptions<PivotOptions>>().Value ?? new PivotOptions()
    };
}

internal sealed class VibeMesh
{
    public required IOptions<MeshOrchestrationOptions> Options { get; init; }
    public required MeshDefinitionStore Store { get; init; }
    public required GlobalAgentYamlRegistry Roles { get; init; }
    public required MeshCompilerService Compiler { get; init; }
    public required MeshExecutionPlanner Planner { get; init; }
    public required MeshExecutionRunner Runner { get; init; }

    public static VibeMesh Create(IServiceProvider sp) => new()
    {
        Options = sp.GetRequiredService<IOptions<MeshOrchestrationOptions>>(),
        Store = sp.GetRequiredService<MeshDefinitionStore>(),
        Roles = sp.GetRequiredService<GlobalAgentYamlRegistry>(),
        Compiler = sp.GetRequiredService<MeshCompilerService>(),
        Planner = sp.GetRequiredService<MeshExecutionPlanner>(),
        Runner = sp.GetRequiredService<MeshExecutionRunner>()
    };
}

internal sealed class VibeHost
{
    public required IHostEnvironment Env { get; init; }
    public required ILogger<VibeOrchestrator> Logger { get; init; }

    public static VibeHost Create(IServiceProvider sp) => new()
    {
        Env = sp.GetRequiredService<IHostEnvironment>(),
        Logger = sp.GetRequiredService<ILogger<VibeOrchestrator>>()
    };
}

