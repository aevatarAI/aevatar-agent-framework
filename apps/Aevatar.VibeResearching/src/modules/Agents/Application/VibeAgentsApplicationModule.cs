using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Agents;

[DependsOn(
    typeof(AbpDddApplicationModule),
    typeof(VibeAgentsDomainModule),
    typeof(VibeAgentsApplicationContractsModule),
    typeof(AbpAutoMapperModule)
)]
public class VibeAgentsApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<VibeAgentsApplicationModule>();
        });
    }
}
