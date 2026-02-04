using Aevatar.VibeResearching.Infrastructure.Localization;
using Aevatar.VibeResearching.Sessions;
using Volo.Abp.Authorization;
using Volo.Abp.Domain;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Aevatar.VibeResearching.Infrastructure;

[DependsOn(
    typeof(AbpDddDomainSharedModule),
    typeof(AbpAuthorizationAbstractionsModule),
    typeof(VibeSessionsDomainSharedModule)
)]
public class VibeInfrastructureDomainSharedModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<VibeInfrastructureDomainSharedModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<PlatformResource>("en")
                .AddVirtualJson("/Localization");
        });
    }
}
