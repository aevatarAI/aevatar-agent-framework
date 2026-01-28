using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Infrastructure;

[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(VibeInfrastructureApplicationContractsModule)
)]
public class VibeInfrastructureHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(VibeInfrastructureHttpApiModule).Assembly);
        });
    }
}
