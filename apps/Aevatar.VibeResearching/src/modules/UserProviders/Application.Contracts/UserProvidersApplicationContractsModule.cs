using Volo.Abp.Modularity;

namespace Aevatar.VibeResearching.UserProviders;

[DependsOn(typeof(UserProvidersDomainSharedModule))]
public class UserProvidersApplicationContractsModule : AbpModule
{
}
