using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Sessions;

[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(VibeSessionsApplicationContractsModule)
)]
public class VibeSessionsHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(VibeSessionsHttpApiModule).Assembly);
        });
    }
}
