using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Knowledge;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(VibeKnowledgeDomainSharedModule)
)]
public class VibeKnowledgeDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // FactLifecycleService: manages fact proposal/vote/verify/promote lifecycle
        context.Services.AddTransient<FactLifecycleService>();
        context.Services.AddTransient<IFactLifecycleService>(sp => sp.GetRequiredService<FactLifecycleService>());
    }
}
