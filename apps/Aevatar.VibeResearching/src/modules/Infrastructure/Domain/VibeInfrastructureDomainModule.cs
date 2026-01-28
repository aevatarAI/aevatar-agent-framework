using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Infrastructure;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(VibeInfrastructureDomainSharedModule)
)]
public class VibeInfrastructureDomainModule : AbpModule
{
}
