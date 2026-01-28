using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;
using Aevatar.VibeResearching.Agents.Pivot;
using Aevatar.VibeResearching.Agents.Mesh;
using Aevatar.VibeResearching.Agents.Mesh.Services;
using Aevatar.VibeResearching.Agents.Orchestration;
using Aevatar.VibeResearching.Agents.ReviewAgent;
using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Knowledge;
using Aevatar.Agents.Knowledge.Graph;

namespace Aevatar.VibeResearching.Agents;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(VibeAgentsDomainSharedModule)
)]
public class VibeAgentsDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // ==========================================
        // Research Run Execution & Orchestration
        // ==========================================

        // Vibe module objects (composition root for VibeOrchestrator)
        services.AddSingleton(sp => VibeCore.Create(sp));
        services.AddSingleton(sp => VibePivot.Create(sp));
        services.AddSingleton(sp => VibeMesh.Create(sp));
        services.AddSingleton(sp => VibeHost.Create(sp));

        // VibeOrchestrator (internal ctor, uses module composition)
        services.AddSingleton(sp => new VibeOrchestrator(
            sp.GetRequiredService<VibeCore>(),
            sp.GetRequiredService<VibePivot>(),
            sp.GetRequiredService<VibeMesh>(),
            sp.GetRequiredService<VibeHost>()));

        // Loop runners
        services.AddSingleton(sp => new VibeGoalLoopRunner(
            sp.GetRequiredService<VibeOrchestrator>(),
            sp.GetRequiredService<ILogger<VibeGoalLoopRunner>>()));

        services.AddSingleton(sp => new VibeMilestoneLoopRunner(
            sp.GetRequiredService<VibeOrchestrator>(),
            sp.GetRequiredService<IBriefRepository>(),
            sp.GetRequiredService<IDagStore>(),
            sp.GetRequiredService<IKnowledgeGraphClientFactory>(),
            sp.GetRequiredService<ILogger<VibeMilestoneLoopRunner>>()));

        // ResearchRunExecutor (depends on orchestration types above)
        services.AddSingleton<ResearchRunExecutor>();

        // ==========================================
        // Pivot Services
        // ==========================================
        services.AddSingleton<PivotSnapshotManager>();
        services.AddSingleton<IPivotSnapshotManager>(sp => sp.GetRequiredService<PivotSnapshotManager>());
        services.AddSingleton<PivotQueue>();
        services.AddSingleton<IPivotQueue>(sp => sp.GetRequiredService<PivotQueue>());
        services.AddSingleton<PivotEventPublisher>();
        services.AddSingleton<IPivotEventPublisher>(sp => sp.GetRequiredService<PivotEventPublisher>());
        services.AddSingleton<PivotMetrics>();
        services.AddTransient<DirectionChangeDetector>();
        services.AddTransient<IDirectionChangeDetector>(sp => sp.GetRequiredService<DirectionChangeDetector>());
        services.AddTransient<PivotOrchestrator>();
        services.AddTransient<IPivotOrchestrator>(sp => sp.GetRequiredService<PivotOrchestrator>());
        services.AddTransient<AgentPivotCoordinator>();
        services.AddTransient<IAgentPivotCoordinator>(sp => sp.GetRequiredService<AgentPivotCoordinator>());
        services.AddTransient<PivotFeedbackEmitter>();
        services.AddTransient<IPivotFeedbackEmitter>(sp => sp.GetRequiredService<PivotFeedbackEmitter>());
        services.AddSingleton<InMemoryPivotStatusService>();
        services.AddSingleton<IPivotStatusService>(sp => sp.GetRequiredService<InMemoryPivotStatusService>());

        // ==========================================
        // Mesh Services
        // ==========================================
        services.AddSingleton<EmptyGlobalAgentYamlRegistry>();
        services.AddSingleton<IGlobalAgentYamlRegistry>(sp => sp.GetRequiredService<EmptyGlobalAgentYamlRegistry>());
        services.AddSingleton<MeshDefinitionStore>();
        services.AddSingleton<IMeshDefinitionStore>(sp => sp.GetRequiredService<MeshDefinitionStore>());
        services.AddTransient<MeshCompilerService>();
        services.AddTransient<IMeshCompilerService>(sp => sp.GetRequiredService<MeshCompilerService>());
        services.AddTransient<MeshExecutionPlanner>();
        services.AddTransient<IMeshExecutionPlanner>(sp => sp.GetRequiredService<MeshExecutionPlanner>());
        services.AddTransient<MeshExecutionRunner>();
        services.AddTransient<IMeshExecutionRunner>(sp => sp.GetRequiredService<MeshExecutionRunner>());

        // ==========================================
        // Review Agent
        // ==========================================
        services.AddSingleton<DefaultReviewAgentTrigger>();
        services.AddSingleton<IReviewAgentTrigger>(sp => sp.GetRequiredService<DefaultReviewAgentTrigger>());
    }
}
