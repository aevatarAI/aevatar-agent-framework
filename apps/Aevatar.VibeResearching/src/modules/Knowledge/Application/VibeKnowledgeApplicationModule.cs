using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Knowledge;

[DependsOn(
    typeof(AbpDddApplicationModule),
    typeof(VibeKnowledgeDomainModule),
    typeof(VibeKnowledgeApplicationContractsModule),
    typeof(AbpAutoMapperModule)
)]
public class VibeKnowledgeApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<VibeKnowledgeApplicationModule>();
        });
    }
}
