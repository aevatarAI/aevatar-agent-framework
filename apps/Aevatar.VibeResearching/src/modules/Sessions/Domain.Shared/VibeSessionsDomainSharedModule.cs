using Volo.Abp.Authorization;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Aevatar.VibeResearching.Sessions;

[DependsOn(
    typeof(AbpDddDomainSharedModule),
    typeof(AbpAuthorizationAbstractionsModule)
)]
public class VibeSessionsDomainSharedModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<VibeSessionsDomainSharedModule>();
        });
    }
}
