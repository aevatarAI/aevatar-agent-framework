using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Sessions;

[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(VibeSessionsDomainSharedModule)
)]
public class VibeSessionsApplicationContractsModule : AbpModule
{
}
