using Aevatar.VibeResearching.Sessions;
using Volo.Abp.Authorization;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Aevatar.VibeResearching.Knowledge;

[DependsOn(
    typeof(AbpDddDomainSharedModule),
    typeof(AbpAuthorizationAbstractionsModule),
    typeof(VibeSessionsDomainSharedModule)
)]
public class VibeKnowledgeDomainSharedModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<VibeKnowledgeDomainSharedModule>();
        });
    }
}
