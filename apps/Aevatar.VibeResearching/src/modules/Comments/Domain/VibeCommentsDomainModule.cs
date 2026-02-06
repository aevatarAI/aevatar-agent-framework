using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Comments;

[DependsOn(
    typeof(AbpDddDomainModule),
    typeof(VibeCommentsDomainSharedModule)
)]
public class VibeCommentsDomainModule : AbpModule
{
}
