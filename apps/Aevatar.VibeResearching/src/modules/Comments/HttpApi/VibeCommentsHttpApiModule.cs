using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Comments;

[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(VibeCommentsApplicationContractsModule)
)]
public class VibeCommentsHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(VibeCommentsHttpApiModule).Assembly);
        });
    }
}
