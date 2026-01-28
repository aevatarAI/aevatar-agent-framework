using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.MongoDB.DependencyInjection;
using Aevatar.VibeResearching.Sessions.Repositories;
using Aevatar.VibeResearching.Sessions.Services;
using Aevatar.VibeResearching.Sessions.MongoDB.Repositories;
using Aevatar.VibeResearching.Sessions.MongoDB.Services;

namespace Aevatar.VibeResearching.Sessions.MongoDB;

[DependsOn(
    typeof(AbpMongoDbModule),
    typeof(VibeSessionsDomainModule)
)]
public class VibeSessionsMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // TODO: Register MongoDB context when switching to actual MongoDB implementation
        // context.Services.AddAbpDbContext<SessionsMongoDbContext>();

        // Register repositories
        // Note: Using file-based implementations for now
        context.Services.AddTransient<IVibeSessionRepository, FileVibeSessionRepository>();
        context.Services.AddTransient<IAgentProvidersRepository, MongoAgentProvidersRepository>();

        // Register services
        context.Services.AddTransient<ISessionUiTraceService, SessionUiTraceRecorder>();
        context.Services.AddTransient<SessionUiSnapshotStore>();
        context.Services.AddTransient<SessionFilesService>();
        context.Services.AddTransient<AgentProvidersStore>();
    }
}
