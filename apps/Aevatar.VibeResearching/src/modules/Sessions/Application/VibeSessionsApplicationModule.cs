using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Sessions;

[DependsOn(
    typeof(AbpDddApplicationModule),
    typeof(VibeSessionsDomainModule),
    typeof(VibeSessionsApplicationContractsModule),
    typeof(AbpAutoMapperModule)
)]
public class VibeSessionsApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<VibeSessionsApplicationModule>();
        });
    }
}
