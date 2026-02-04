using Volo.Abp.Application;
using Volo.Abp.AutoMapper;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Comments;

[DependsOn(
    typeof(AbpDddApplicationModule),
    typeof(VibeCommentsDomainModule),
    typeof(VibeCommentsApplicationContractsModule),
    typeof(AbpAutoMapperModule)
)]
public class VibeCommentsApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<VibeCommentsApplicationModule>();
        });
    }
}
