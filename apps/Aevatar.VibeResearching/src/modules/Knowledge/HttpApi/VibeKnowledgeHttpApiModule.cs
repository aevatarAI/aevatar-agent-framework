using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Knowledge;

[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(VibeKnowledgeApplicationContractsModule)
)]
public class VibeKnowledgeHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(VibeKnowledgeHttpApiModule).Assembly);
        });
    }
}
