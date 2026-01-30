using Volo.Abp.Authorization;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Comments;

[DependsOn(
    typeof(AbpDddDomainSharedModule),
    typeof(AbpAuthorizationAbstractionsModule)
)]
public class VibeCommentsDomainSharedModule : AbpModule
{
}
