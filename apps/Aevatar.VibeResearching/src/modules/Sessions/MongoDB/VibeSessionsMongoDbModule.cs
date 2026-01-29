using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
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
        // Register repositories
        // Uses IStateStore<T> abstraction — resolves to MongoDB / SQLite / InMemory
        // based on host-level StateStoreType configuration.
        context.Services.AddTransient<IVibeSessionRepository, MongoVibeSessionRepository>();
        context.Services.AddTransient<IAgentProvidersRepository, MongoAgentProvidersRepository>();

        // Register services
        context.Services.AddTransient<ISessionUiTraceService, SessionUiTraceRecorder>();
        context.Services.AddTransient<SessionUiSnapshotStore>();
        context.Services.AddTransient<SessionFilesService>();
        context.Services.AddTransient<AgentProvidersStore>();
    }
}
