using Aevatar.VibeResearching.Infrastructure;
using Aevatar.VibeResearching.Knowledge;
using Aevatar.VibeResearching.Sessions;
using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.Agents;

[DependsOn(
    typeof(AbpDddApplicationContractsModule),
    typeof(VibeAgentsDomainSharedModule),
    typeof(VibeSessionsApplicationContractsModule),
    typeof(VibeKnowledgeApplicationContractsModule),
    typeof(VibeInfrastructureApplicationContractsModule)
)]
public class VibeAgentsApplicationContractsModule : AbpModule
{
}
