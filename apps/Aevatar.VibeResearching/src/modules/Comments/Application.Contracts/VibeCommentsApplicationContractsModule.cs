using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Comments;

[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(VibeCommentsDomainSharedModule)
)]
public class VibeCommentsApplicationContractsModule : AbpModule
{
}
