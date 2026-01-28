using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Infrastructure;

[DependsOn(
    typeof(AbpDddApplicationModule),
    typeof(VibeInfrastructureDomainModule),
    typeof(VibeInfrastructureApplicationContractsModule),
    typeof(AbpAutoMapperModule)
)]
public class VibeInfrastructureApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<VibeInfrastructureApplicationModule>();
        });
    }
}
