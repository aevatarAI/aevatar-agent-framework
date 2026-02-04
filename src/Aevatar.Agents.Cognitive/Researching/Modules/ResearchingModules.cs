using Aevatar.Agents.Core.Secrets;
using Aevatar.Agents.Knowledge.Graph;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Aevatar.Agents.Cognitive.Researching.Materials;
using Aevatar.Agents.Cognitive.Researching.Paper;
using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Researching.Brief;
using Aevatar.Agents.Cognitive.Researching.Dag;
using Aevatar.Agents.Cognitive.Researching.Delivery;
using Aevatar.Agents.Cognitive.Researching.Mesh;
using Aevatar.Agents.Cognitive.Researching.Trace;
using Aevatar.Agents.Cognitive.Researching.Workspace;
using VibeResearching.Vibe.Pivot;
using Aevatar.Agents.Cognitive.Researching.Round;
using Aevatar.Agents.Cognitive.Researching.Runtime;
using Aevatar.Agents.AI.Core.Configuration;

namespace Aevatar.Agents.Cognitive.Researching.Modules;

// ============================================================
//  Researching modules (依赖分组)
//
//  中文说明：
//  - Orchestrator 的“构造器恐怖”本质是：跨子域依赖混在一个平面列表里
//  - 解决方式是把依赖按子域建模成模块对象（Core/Pivot/Mesh/Host）
//  - 模块对象不是“藏屎”，而是把 wiring 放在 wiring 应该出现的地方：composition root
// ============================================================

public sealed class ResearchingCore
{
    public required IResearchingRuntime Runtime { get; init; }
    public required MaterialsService Materials { get; init; }
    public required WorkspaceService Workspace { get; init; }
    public required BriefStore Brief { get; init; }
    public required DeliveryCenterStore Delivery { get; init; }
    public required DagStore Dag { get; init; }
    public required IDagGroundingPolicy DagGrounding { get; init; }
    public required TraceStore Trace { get; init; }
    public required FileMailboxService Mailbox { get; init; }
    public required PaperService Paper { get; init; }
    public required AgentProvidersStore AgentProviders { get; init; }
    public required DagConsensusRunner DagConsensus { get; init; }
    public required IAevatarUserSecretsStore UserSecrets { get; init; }
    public required IKnowledgeGraphClientFactory GraphFactory { get; init; }

    public static ResearchingCore Create(IServiceProvider sp) => new()
    {
        Runtime = sp.GetRequiredService<IResearchingRuntime>(),
        Materials = sp.GetRequiredService<MaterialsService>(),
        Workspace = sp.GetRequiredService<WorkspaceService>(),
        Brief = sp.GetRequiredService<BriefStore>(),
        Delivery = sp.GetRequiredService<DeliveryCenterStore>(),
        Dag = sp.GetRequiredService<DagStore>(),
        DagGrounding = sp.GetRequiredService<IDagGroundingPolicy>(),
        Trace = sp.GetRequiredService<TraceStore>(),
        Mailbox = sp.GetRequiredService<FileMailboxService>(),
        Paper = sp.GetRequiredService<PaperService>(),
        AgentProviders = sp.GetRequiredService<AgentProvidersStore>(),
        DagConsensus = sp.GetRequiredService<DagConsensusRunner>(),
        UserSecrets = sp.GetRequiredService<IAevatarUserSecretsStore>(),
        GraphFactory = sp.GetRequiredService<IKnowledgeGraphClientFactory>()
    };
}

public sealed class ResearchingPivot
{
    public required IDirectionChangeDetector DirectionDetector { get; init; }
    public required IPivotOrchestrator Orchestrator { get; init; }
    public required IPivotQueue Queue { get; init; }
    public required IAgentPivotCoordinator AgentCoordinator { get; init; }
    public required IPivotFeedbackEmitter FeedbackEmitter { get; init; }
    public required PivotOptions Options { get; init; }

    public static ResearchingPivot Create(IServiceProvider sp) => new()
    {
        DirectionDetector = sp.GetRequiredService<IDirectionChangeDetector>(),
        Orchestrator = sp.GetRequiredService<IPivotOrchestrator>(),
        Queue = sp.GetRequiredService<IPivotQueue>(),
        AgentCoordinator = sp.GetRequiredService<IAgentPivotCoordinator>(),
        FeedbackEmitter = sp.GetRequiredService<IPivotFeedbackEmitter>(),
        Options = sp.GetRequiredService<IOptions<PivotOptions>>().Value ?? new PivotOptions()
    };
}

public sealed class ResearchingMesh
{
    public required IOptions<MeshOrchestrationOptions> Options { get; init; }
    public required MeshDefinitionStore Store { get; init; }
    public required GlobalAgentYamlRegistry Roles { get; init; }
    public required MeshCompilerService Compiler { get; init; }
    public required MeshExecutionPlanner Planner { get; init; }
    public required MeshExecutionRunner Runner { get; init; }

    public static ResearchingMesh Create(IServiceProvider sp) => new()
    {
        Options = sp.GetRequiredService<IOptions<MeshOrchestrationOptions>>(),
        Store = sp.GetRequiredService<MeshDefinitionStore>(),
        Roles = sp.GetRequiredService<GlobalAgentYamlRegistry>(),
        Compiler = sp.GetRequiredService<MeshCompilerService>(),
        Planner = sp.GetRequiredService<MeshExecutionPlanner>(),
        Runner = sp.GetRequiredService<MeshExecutionRunner>()
    };
}

public sealed class ResearchingHost
{
    public required IHostEnvironment Env { get; init; }
    public required ILogger<ResearchingRoundServices> Logger { get; init; }

    public static ResearchingHost Create(IServiceProvider sp) => new()
    {
        Env = sp.GetRequiredService<IHostEnvironment>(),
        Logger = sp.GetRequiredService<ILogger<ResearchingRoundServices>>()
    };
}


