using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Infrastructure;

[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(VibeInfrastructureDomainSharedModule)
)]
public class VibeInfrastructureApplicationContractsModule : AbpModule
{
}
