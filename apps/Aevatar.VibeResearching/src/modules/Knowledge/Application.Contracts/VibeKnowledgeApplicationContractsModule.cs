using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Knowledge;

[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(VibeKnowledgeDomainSharedModule)
)]
public class VibeKnowledgeApplicationContractsModule : AbpModule
{
}
