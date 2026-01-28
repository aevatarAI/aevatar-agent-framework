using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Agents;

[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(VibeAgentsApplicationContractsModule)
)]
public class VibeAgentsHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(VibeAgentsHttpApiModule).Assembly);
        });
    }
}
