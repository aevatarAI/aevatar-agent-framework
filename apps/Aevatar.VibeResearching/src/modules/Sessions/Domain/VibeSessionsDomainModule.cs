using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;
using Aevatar.VibeResearching.Sessions.Services;

namespace Aevatar.VibeResearching.Sessions;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(VibeSessionsDomainSharedModule)
)]
public class VibeSessionsDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // ResearchSessionManager: singleton in-memory session registry
        context.Services.AddSingleton<ResearchSessionManager>();
    }
}
