using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Trace;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Delivery;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Compute;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Workspace;
using Aevatar.VibeResearching.Infrastructure.MongoDB.SkillPacks;
using Aevatar.VibeResearching.Infrastructure.MongoDB.SkillsMp;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Secrets;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Paper;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Materials;
using Aevatar.VibeResearching.Infrastructure.MongoDB.Knowledge.Dag;
using Aevatar.VibeResearching.Knowledge;
using Aevatar.VibeResearching.Infrastructure;

namespace Aevatar.VibeResearching.Infrastructure.MongoDB;

[DependsOn(
    typeof(AbpMongoDbModule),
    typeof(VibeInfrastructureDomainModule)
)]
public class VibeInfrastructureMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // MongoDB Context (not needed - Infrastructure module is file-based)
        // context.Services.AddAbpDbContext<InfrastructureMongoDbContext>();

        // Workspace Services (foundational)
        services.AddSingleton<WorkspaceService>();
        services.AddSingleton<IWorkspaceService>(sp => sp.GetRequiredService<WorkspaceService>());
        services.AddTransient<FileMailboxService>();
        services.AddTransient<IFileMailboxService, FileMailboxService>();
        services.AddTransient<SessionFilesService>();
        services.AddTransient<ISessionFilesService>(sp => sp.GetRequiredService<SessionFilesService>());

        // Delivery Repository Services
        services.AddTransient<TraceRepository>();
        services.AddTransient<ITraceRepository>(sp => sp.GetRequiredService<TraceRepository>());
        services.AddTransient<BriefRepository>();
        services.AddTransient<IBriefRepository>(sp => sp.GetRequiredService<BriefRepository>());
        services.AddTransient<GoalsRepository>();
        services.AddTransient<IGoalsRepository>(sp => sp.GetRequiredService<GoalsRepository>());
        services.AddTransient<DeliveryCenterRepository>();
        services.AddTransient<IDeliveryCenterRepository>(sp => sp.GetRequiredService<DeliveryCenterRepository>());

        // New Delivery Repositories (thin delegates to DeliveryCenterRepository)
        services.AddTransient<ConclusionCardsRepository>();
        services.AddTransient<IConclusionCardsRepository>(sp => sp.GetRequiredService<ConclusionCardsRepository>());
        services.AddTransient<EvidenceTableRepository>();
        services.AddTransient<IEvidenceTableRepository>(sp => sp.GetRequiredService<EvidenceTableRepository>());
        services.AddTransient<NextTasksRepository>();
        services.AddTransient<INextTasksRepository>(sp => sp.GetRequiredService<NextTasksRepository>());

        // Compute Services
        services.AddTransient<ComputeDecisionRepository>();
        services.AddTransient<IComputeDecisionRepository>(sp => sp.GetRequiredService<ComputeDecisionRepository>());

        // Knowledge Services
        services.AddTransient<DagStore>();
        services.AddTransient<IDagStore, DagStore>();

        // SkillPacks Services
        services.AddSingleton<SkillPacksSyncProgress>();
        services.AddTransient<SkillPacksConfigFileStore>();
        services.AddTransient<SkillPacksSyncService>();
        services.AddTransient<ISkillPacksSyncService, SkillPacksSyncService>();
        services.AddSingleton<DefaultSkillPacksManager>();
        services.AddSingleton<ISkillPacksManager>(sp => sp.GetRequiredService<DefaultSkillPacksManager>());

        // SkillsMP Services
        services.AddHttpClient<SkillsMpClient>();

        // LLM Secrets Services
        services.AddSingleton<DefaultLlmSecretsManager>();
        services.AddSingleton<ILlmSecretsManager>(sp => sp.GetRequiredService<DefaultLlmSecretsManager>());

        // Paper Services
        services.AddTransient<PaperService>();
        services.AddTransient<IPaperService, PaperService>();

        // Materials Services
        services.AddTransient<MaterialsService>();
        services.AddTransient<IMaterialsService, MaterialsService>();
    }
}
